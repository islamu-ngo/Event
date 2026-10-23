using System.Security.Cryptography;
using DotNet.Testcontainers.Builders;
using Event.Testing.DataProtection;
using Explore.Blazor.Services.Auth;
using Explore.ServiceDefaults.DataProtection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Testcontainers.Redis;
using TUnit.Core.Interfaces;

namespace Explore.Blazor.IntegrationTests.Services;

[ClassDataSource<RedisBffTicketStoreTests.Authority>(Shared = SharedType.PerClass)]
public sealed class RedisBffTicketStoreTests(RedisBffTicketStoreTests.Authority authority)
{
    [Test]
    public async Task PeerReadsTicketAndLogoutRevokesOnlyThatBrowser()
    {
        using var first = authority.Create();
        using var peer = authority.Create();
        var ticket = Ticket();
        string handle = await first.StoreAsync(ticket);
        string other = await first.StoreAsync(Ticket());
        var restored = await peer.RetrieveAsync(handle);
        await Assert.That(restored).IsNotNull();
        await Assert.That(BffSessionIdentity.TryGetBinding(restored!.Principal, out var binding)).IsTrue();
        await Assert.That(await peer.IsActiveAsync(binding, CancellationToken.None)).IsTrue();
        await Assert.That(await peer.IsActiveAsync(new(handle, "foreign-owner"), CancellationToken.None)).IsFalse();
        await first.RemoveAsync(handle);
        await Assert.That(await peer.RetrieveAsync(handle)).IsNull();
        await Assert.That(await peer.IsActiveAsync(binding, CancellationToken.None)).IsFalse();
        await Assert.That(await peer.RetrieveAsync(other)).IsNotNull();
        await Assert.That(() => peer.RenewAsync(handle, restored)).Throws<BffSessionAuthorityUnavailableException>();
        await Assert.That(async () => { _ = await peer.StoreAsync(restored); }).Throws<BffSessionAuthorityUnavailableException>();
    }

    [Test]
    public async Task RenewalBeforeLogoutCannotSurviveAcknowledgedRemoval()
    {
        using var first = authority.Create();
        using var peer = authority.Create();
        string handle = await first.StoreAsync(Ticket());
        var ticket = (await peer.RetrieveAsync(handle))!;
        ticket.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(6);
        await peer.RenewAsync(handle, ticket);
        await first.RemoveAsync(handle);
        await Assert.That(await peer.RetrieveAsync(handle)).IsNull();
    }

    [Test]
    public async Task RemovalDuringRenewalCannotRecreateEntry()
    {
        using var first = authority.Create();
        string handle = await first.StoreAsync(Ticket());
        var ticket = (await first.RetrieveAsync(handle))!;
        using var gate = new ReadGate(authority.Protection, 1);
        using var peer = authority.Create(gate);
        Task renewal = Task.Run(() => peer.RenewAsync(handle, ticket));
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await first.RemoveAsync(handle);
        gate.Release.Set();
        await Assert.That(async () => await renewal).Throws<BffSessionAuthorityUnavailableException>();
        await Assert.That(await first.RetrieveAsync(handle)).IsNull();
    }

    [Test]
    public async Task ConcurrentRefreshRejectsStaleReplacement()
    {
        using var first = authority.Create();
        string handle = await first.StoreAsync(Ticket());
        var leftTicket = (await first.RetrieveAsync(handle))!;
        var rightTicket = (await first.RetrieveAsync(handle))!;
        using var gate = new ReadGate(authority.Protection, 2);
        using var left = authority.Create(gate);
        using var right = authority.Create(gate);
        Task<bool> leftResult = Task.Run(() => Renew(left, handle, leftTicket));
        Task<bool> rightResult = Task.Run(() => Renew(right, handle, rightTicket));
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        gate.Release.Set();
        bool[] results = await Task.WhenAll(leftResult, rightResult);
        await Assert.That(results.Count(result => result)).IsEqualTo(1);
        await Assert.That(await first.RetrieveAsync(handle)).IsNotNull();
    }

    [Test]
    public async Task RefreshStartingAfterAnotherCommittedCannotOverwriteItsNewTicket()
    {
        using var first = authority.Create();
        using var peer = authority.Create();
        string handle = await first.StoreAsync(Ticket());
        var winner = (await first.RetrieveAsync(handle))!;
        var stale = (await peer.RetrieveAsync(handle))!;
        string winnerToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(48));
        winner.Properties.StoreTokens([new AuthenticationToken { Name = "access_token", Value = winnerToken }]);
        stale.Properties.StoreTokens([new AuthenticationToken { Name = "access_token", Value = "stale-candidate" }]);
        await first.RenewAsync(handle, winner);
        await Assert.That(() => peer.RenewAsync(handle, stale)).Throws<BffSessionAuthorityUnavailableException>();
        var restored = (await peer.RetrieveAsync(handle))!;
        await Assert.That(string.Equals(restored.Properties.GetTokenValue("access_token"), winnerToken, StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task ExpiryRejectsReadAndRenewalWithoutRecreation()
    {
        using var first = authority.Create();
        string handle = await first.StoreAsync(Ticket(DateTimeOffset.UtcNow.AddSeconds(2)));
        var ticket = (await first.RetrieveAsync(handle))!;
        await Task.Delay(2200);
        ticket.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(5);
        await Assert.That(await first.RetrieveAsync(handle)).IsNull();
        await Assert.That(() => first.RenewAsync(handle, ticket)).Throws<BffSessionAuthorityUnavailableException>();
        await Assert.That(await first.RetrieveAsync(handle)).IsNull();
    }

    [Test]
    public async Task NamespacePartitionSchemeAndPayloadSwappingAreRejected()
    {
        using var first = authority.Create();
        using var foreign = authority.Create(sessionNamespace: Guid.NewGuid().ToString("N"));
        using var partition = authority.Create(partition: "combined");
        string handle = await first.StoreAsync(Ticket());
        await Assert.That(await foreign.RetrieveAsync(handle)).IsNull();
        await Assert.That(await partition.RetrieveAsync(handle)).IsNull();
        var invalid = Ticket();
        invalid = new(invalid.Principal, invalid.Properties, "foreign-scheme");
        await Assert.That(async () => { _ = await first.StoreAsync(invalid); }).Throws<BffSessionAuthorityUnavailableException>();
        string second = await first.StoreAsync(Ticket());
        var database = authority.Connection.GetDatabase();
        var keys = await authority.Keys();
        RedisKey firstKey = keys.Single(key => key.ToString().EndsWith(handle, StringComparison.Ordinal));
        RedisKey secondKey = keys.Single(key => key.ToString().EndsWith(second, StringComparison.Ordinal));
        await database.StringSetAsync(secondKey, await database.StringGetAsync(firstKey), TimeSpan.FromMinutes(5));
        await Assert.That(await first.RetrieveAsync(second)).IsNull();
        await database.StringSetAsync(firstKey, RandomNumberGenerator.GetBytes(100), TimeSpan.FromMinutes(5));
        await Assert.That(await first.RetrieveAsync(handle)).IsNull();
    }

    [Test]
    public async Task MissingAndDeniedAuthorityExposeOnlyOpaqueFailure()
    {
        using var missing = new RedisBffTicketStore(new(), authority.Protection, new("bff"), TimeProvider.System);
        await Assert.That(async () => { _ = await missing.StoreAsync(Ticket()); }).Throws<BffSessionAuthorityUnavailableException>();
        var options = authority.Options();
        options.Password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        using var denied = new RedisBffTicketStore(options, authority.Protection, new("bff"), TimeProvider.System);
        Exception? failure = null;
        try { await denied.StoreAsync(Ticket()); }
        catch (BffSessionAuthorityUnavailableException exception) { failure = exception; }
        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.InnerException).IsNull();
        await Assert.That(failure.Message).IsEqualTo("bff_session_authority_unavailable");
        await Assert.That(failure.ToString().Contains(options.Password, StringComparison.Ordinal)).IsFalse();
        await Assert.That(options.ToString().Contains(options.Password, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task FreshLoginReplacesUntrustedClaimButRejectsEveryRetainedSessionMarker()
    {
        using var store = authority.Create();
        foreach (string property in new[] { BffSessionIdentity.HandleProperty, BffSessionIdentity.RevisionProperty })
        foreach (string? marker in new string?[] { null, "", "malformed", Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) })
        {
            var retained = Ticket();
            retained.Properties.Items[property] = marker;
            await Assert.That(async () => { _ = await store.StoreAsync(retained); }).Throws<BffSessionAuthorityUnavailableException>();
        }
        var fresh = Ticket();
        ((ClaimsIdentity)fresh.Principal.Identity!).AddClaim(new Claim(BffSessionIdentity.HandleClaim, "untrusted"));
        string handle = await store.StoreAsync(fresh);
        var restored = (await store.RetrieveAsync(handle))!;
        await Assert.That(BffSessionIdentity.TryGetBinding(restored.Principal, out var binding)).IsTrue();
        await Assert.That(string.Equals(binding.Handle, handle, StringComparison.Ordinal)).IsTrue();
        await Assert.That(binding.ToString().Contains(handle, StringComparison.Ordinal)).IsFalse();
        await Assert.That(binding.ToString().Contains(binding.OwnerPartitionKey, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task RenewalPreservesIssuedIdentityFactsButAllowsTokenAndAdministratorRefresh()
    {
        using var store = authority.Create();
        var initial = Ticket();
        var identity = (ClaimsIdentity)initial.Principal.Identity!;
        identity.AddClaims([new Claim("auth_provider", "atproto"), new Claim("did", "did:plc:issued"),
            new Claim("tenant_id", Guid.NewGuid().ToString()), new Claim("tenant_slug", "issued-tenant")]);
        string handle = await store.StoreAsync(initial);
        foreach (string claimType in new[] { "sub", "auth_provider", "did", "tenant_id", "tenant_slug", BffSessionIdentity.HandleClaim })
        {
            var changed = (await store.RetrieveAsync(handle))!;
            var changedIdentity = (ClaimsIdentity)changed.Principal.Identity!;
            changedIdentity.RemoveClaim(changed.Principal.FindFirst(claimType)!);
            changedIdentity.AddClaim(new Claim(claimType, "changed"));
            await Assert.That(() => store.RenewAsync(handle, changed)).Throws<BffSessionAuthorityUnavailableException>();
            var removed = (await store.RetrieveAsync(handle))!;
            ((ClaimsIdentity)removed.Principal.Identity!).RemoveClaim(removed.Principal.FindFirst(claimType)!);
            await Assert.That(() => store.RenewAsync(handle, removed)).Throws<BffSessionAuthorityUnavailableException>();
            var duplicate = (await store.RetrieveAsync(handle))!;
            ((ClaimsIdentity)duplicate.Principal.Identity!).AddClaim(duplicate.Principal.FindFirst(claimType)!.Clone());
            await Assert.That(() => store.RenewAsync(handle, duplicate)).Throws<BffSessionAuthorityUnavailableException>();
        }
        var refreshed = (await store.RetrieveAsync(handle))!;
        ((ClaimsIdentity)refreshed.Principal.Identity!).AddClaims([new Claim("is_instance_admin", "true"),
            new Claim("internal_user_id", Guid.NewGuid().ToString())]);
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(48));
        refreshed.Properties.StoreTokens([new AuthenticationToken { Name = "access_token", Value = token }]);
        refreshed.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(8);
        await store.RenewAsync(handle, refreshed);
        var restored = (await store.RetrieveAsync(handle))!;
        await Assert.That(string.Equals(restored.Properties.GetTokenValue("access_token"), token, StringComparison.Ordinal)).IsTrue();
        var bytes = (byte[])(await authority.Connection.GetDatabase().StringGetAsync(
            (await authority.Keys()).Single(key => key.ToString().EndsWith(handle, StringComparison.Ordinal))))!;
        await Assert.That(System.Text.Encoding.UTF8.GetString(bytes).Contains(token, StringComparison.Ordinal)).IsFalse();
        await Assert.That(System.Text.Encoding.UTF8.GetString(bytes)).DoesNotContain("session-owner");
    }

    [Test]
    public async Task AddingPreviouslyAbsentIdentityFactOrLosingMarkerRejectsRenewal()
    {
        using var store = authority.Create();
        string handle = await store.StoreAsync(Ticket());
        foreach (string claimType in new[] { "auth_provider", "did", "tenant_id", "tenant_slug" })
        {
            var changed = (await store.RetrieveAsync(handle))!;
            ((ClaimsIdentity)changed.Principal.Identity!).AddClaim(new Claim(claimType, "added"));
            await Assert.That(() => store.RenewAsync(handle, changed)).Throws<BffSessionAuthorityUnavailableException>();
        }
        var noMarker = (await store.RetrieveAsync(handle))!;
        noMarker.Properties.Items.Remove(BffSessionIdentity.HandleProperty);
        await Assert.That(() => store.RenewAsync(handle, noMarker)).Throws<BffSessionAuthorityUnavailableException>();
    }

    [Test]
    public async Task NativeCancellationOverloadsHonorBothCallerAndRequestAbort()
    {
        using var store = authority.Create();
        ITicketStore native = store;
        using var aborted = new CancellationTokenSource();
        await aborted.CancelAsync();
        string key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await Assert.That(async () => { _ = await native.StoreAsync(Ticket(), aborted.Token); }).Throws<OperationCanceledException>();
        await Assert.That(() => native.RenewAsync(key, Ticket(), aborted.Token)).Throws<OperationCanceledException>();
        await Assert.That(() => native.RetrieveAsync(key, aborted.Token)).Throws<OperationCanceledException>();
        await Assert.That(() => native.RemoveAsync(key, aborted.Token)).Throws<OperationCanceledException>();
        var context = new DefaultHttpContext { RequestAborted = aborted.Token };
        await Assert.That(async () => { _ = await native.StoreAsync(Ticket(), context, CancellationToken.None); }).Throws<OperationCanceledException>();
        await Assert.That(() => native.RenewAsync(key, Ticket(), context, CancellationToken.None)).Throws<OperationCanceledException>();
        await Assert.That(() => native.RetrieveAsync(key, context, CancellationToken.None)).Throws<OperationCanceledException>();
        await Assert.That(() => native.RemoveAsync(key, context, CancellationToken.None)).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task OversizeAndMalformedTicketCannotBecomeAuthenticatedSession()
    {
        using var store = authority.Create();
        var oversized = Ticket();
        oversized.Properties.Items["oversized"] = new string('x', 70 * 1024);
        await Assert.That(async () => { _ = await store.StoreAsync(oversized); }).Throws<BffSessionAuthorityUnavailableException>();
        await Assert.That(async () => { _ = await store.StoreAsync(Ticket(DateTimeOffset.UtcNow.AddSeconds(-1))); }).Throws<BffSessionAuthorityUnavailableException>();
        var ownerless = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([], "Cookies")),
            new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(5) }, "Cookies");
        await Assert.That(async () => { _ = await store.StoreAsync(ownerless); }).Throws<BffSessionAuthorityUnavailableException>();
        string key = await store.StoreAsync(Ticket());
        var database = authority.Connection.GetDatabase();
        RedisKey storageKey = (await authority.Keys()).Single(item => item.ToString().EndsWith(key, StringComparison.Ordinal));
        await database.StringSetAsync(storageKey, new byte[70 * 1024], TimeSpan.FromMinutes(5));
        await Assert.That(await store.RetrieveAsync(key)).IsNull();
    }

    [Test]
    public async Task UnavailableBackendCannotReadRenewCreateOrAcknowledgeLogout()
    {
        await using var unavailable = new Authority();
        await unavailable.InitializeAsync();
        using var store = unavailable.Create();
        string key = await store.StoreAsync(Ticket());
        var ticket = (await store.RetrieveAsync(key))!;
        BffSessionIdentity.TryGetBinding(ticket.Principal, out var binding);
        await unavailable.StopAsync();
        await Assert.That(() => store.RetrieveAsync(key)).Throws<BffSessionAuthorityUnavailableException>();
        await Assert.That(() => store.RenewAsync(key, ticket)).Throws<BffSessionAuthorityUnavailableException>();
        await Assert.That(async () => { _ = await store.StoreAsync(Ticket()); }).Throws<BffSessionAuthorityUnavailableException>();
        await Assert.That(() => store.RemoveAsync(key)).Throws<BffSessionAuthorityUnavailableException>();
        await Assert.That(() => store.IsActiveAsync(binding, CancellationToken.None)).Throws<BffSessionAuthorityUnavailableException>();
    }

    [Test]
    public async Task NativeOpaqueCookieSurvivesPeerRestartAndCannotReplayAfterPeerLogout()
    {
        using var first = authority.Create();
        string cookie;
        await using (var app = await Endpoints.BffNativeSessionStoreLifecycleTests.Host(first, authority.Source))
        {
            using var client = app.GetTestClient();
            using var login = await client.GetAsync("/login");
            await Assert.That(login.StatusCode).IsEqualTo(System.Net.HttpStatusCode.OK);
            cookie = Endpoints.BffNativeSessionStoreLifecycleTests.Cookie(login);
            var format = app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get("Cookies").TicketDataFormat;
            var browserTicket = format.Unprotect(Uri.UnescapeDataString(cookie[(cookie.IndexOf('=', StringComparison.Ordinal) + 1)..]));
            await Assert.That(browserTicket is not null && browserTicket.Principal.Claims.Count() == 1).IsTrue();
            await Assert.That(browserTicket!.Properties.GetTokens().Any()).IsFalse();
            await Assert.That(BffSessionIdentity.TryGetBinding(browserTicket.Principal, out _)).IsFalse();
            string handle = browserTicket.Principal.Claims.Single().Value;
            var serverTicket = (await first.RetrieveAsync(handle))!;
            await Assert.That(serverTicket.Properties.GetTokens().Any()).IsTrue();
        }
        using var peer = authority.Create();
        await using var restarted = await Endpoints.BffNativeSessionStoreLifecycleTests.Host(peer, authority.Source);
        using var replayClient = restarted.GetTestClient();
        replayClient.DefaultRequestHeaders.Add("Cookie", cookie);
        using var accepted = await replayClient.GetAsync("/who");
        await Assert.That(accepted.StatusCode).IsEqualTo(System.Net.HttpStatusCode.OK);
        using var renewed = await replayClient.GetAsync("/refresh-retained");
        await Assert.That(renewed.StatusCode).IsEqualTo(System.Net.HttpStatusCode.OK);
        using var logout = await replayClient.GetAsync("/logout");
        await Assert.That(logout.StatusCode).IsEqualTo(System.Net.HttpStatusCode.OK);
        using var denied = await replayClient.GetAsync("/who");
        await Assert.That(denied.StatusCode).IsEqualTo(System.Net.HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task NamespaceAdmissionMatchesDocumentedDeploymentBoundary()
    {
        using var accepted = authority.Create(sessionNamespace: new string('A', 62) + "_-");
        string handle = await accepted.StoreAsync(Ticket());
        await Assert.That(await accepted.RetrieveAsync(handle)).IsNotNull();
        foreach (string invalidNamespace in new[] { "", new string('A', 65), "deployment.name", " deployment", "dépôt" })
        {
            using var rejected = authority.Create(sessionNamespace: invalidNamespace);
            await Assert.That(async () => { _ = await rejected.StoreAsync(Ticket()); }).Throws<BffSessionAuthorityUnavailableException>();
        }
    }

    private static AuthenticationTicket Ticket(DateTimeOffset? expires = null) => new(
        new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "session-owner")], CookieAuthenticationDefaults.AuthenticationScheme)),
        new AuthenticationProperties { ExpiresUtc = expires ?? DateTimeOffset.UtcNow.AddMinutes(5) },
        CookieAuthenticationDefaults.AuthenticationScheme);

    private static async Task<bool> Renew(RedisBffTicketStore store, string handle, AuthenticationTicket ticket)
    {
        try { await store.RenewAsync(handle, ticket); return true; }
        catch (BffSessionAuthorityUnavailableException) { return false; }
    }

    public sealed class Authority : IAsyncInitializer, IAsyncDisposable
    {
        private readonly string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        private readonly string sessionNamespace = Guid.NewGuid().ToString("N");
        private RedisContainer container = null!;
        private ExternalDataProtectionProvider protection = null!;
        public IConnectionMultiplexer Connection { get; private set; } = null!;
        public IDataProtectionProvider Protection => protection;
        public IExternalDataProtectionSource Source { get; } = new ExternalDataProtectionTestAuthority("bff");

        public async Task InitializeAsync()
        {
            container = new RedisBuilder("redis:7.2.16-alpine@sha256:f8548b29a1f1cb358b5fcfec1e0126beab57ad15776c1e87623f865579f16c5a")
                .WithCommand("redis-server", "--requirepass", password)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(6379)).Build();
            await container.StartAsync();
            var connectionOptions = new ConfigurationOptions { Password = password };
            connectionOptions.EndPoints.Add(container.Hostname, container.GetMappedPublicPort(6379));
            Connection = await ConnectionMultiplexer.ConnectAsync(connectionOptions);
            protection = ExternalDataProtectionProvider.Create(Source.Read(), "bff", TimeProvider.System);
        }

        public BffSessionAuthorityOptions Options(string? selectedNamespace = null) => new()
        {
            Endpoint = $"redis://{container.Hostname}:{container.GetMappedPublicPort(6379)}",
            Namespace = selectedNamespace ?? sessionNamespace,
            Password = password
        };

        public RedisBffTicketStore Create(IDataProtectionProvider? selectedProtection = null, string? sessionNamespace = null, string partition = "bff") =>
            new(Options(sessionNamespace), selectedProtection ?? Protection, new(partition), TimeProvider.System);

        public async Task<RedisKey[]> Keys()
        {
            var keys = new List<RedisKey>();
            await foreach (var key in Connection.GetServer(Connection.GetEndPoints()[0]).KeysAsync()) keys.Add(key);
            return keys.ToArray();
        }

        public Task StopAsync() => container.StopAsync();
        public Task StartAsync() => container.StartAsync();

        public async ValueTask DisposeAsync()
        {
            protection?.Dispose();
            if (Connection is not null) await Connection.DisposeAsync();
            if (container is not null) await container.DisposeAsync();
        }
    }

    private sealed class ReadGate(IDataProtectionProvider provider, int arrivals) : IDataProtectionProvider, IDisposable
    {
        private int remaining = arrivals;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Release { get; } = new();
        public IDataProtector CreateProtector(string purpose) => new Protector(provider.CreateProtector(purpose), this);
        public void Dispose() => Release.Dispose();

        private sealed class Protector(IDataProtector protector, ReadGate gate) : IDataProtector
        {
            public IDataProtector CreateProtector(string purpose) => new Protector(protector.CreateProtector(purpose), gate);
            public byte[] Protect(byte[] plaintext) => protector.Protect(plaintext);
            public byte[] Unprotect(byte[] protectedData)
            {
                byte[] data = protector.Unprotect(protectedData);
                if (Interlocked.Decrement(ref gate.remaining) == 0) gate.Entered.TrySetResult();
                if (!gate.Release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("session_test_gate_timeout");
                return data;
            }
        }
    }
}
