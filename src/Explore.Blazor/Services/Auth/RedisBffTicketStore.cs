using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Event.Web.BffHosting.Security;
using Explore.ServiceDefaults.DataProtection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using StackExchange.Redis;

namespace Explore.Blazor.Services.Auth;

public sealed class RedisBffTicketStore : ITicketStore, IBffSessionAuthority, IDisposable
{
    private const int MaximumTicketBytes = 64 * 1024;
    private const int MaximumProtectedBytes = MaximumTicketBytes + 4096;
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(3);
    private static readonly string[] ImmutableClaimTypes = ["auth_provider", "did", "tenant_id", "tenant_slug"];
    private readonly IDataProtectionProvider protection;
    private readonly TimeProvider clock;
    private readonly string? keyPrefix;
    private readonly Lazy<Task<ConnectionMultiplexer>> connection;
    private int disposed;

    public RedisBffTicketStore(BffSessionAuthorityOptions options, IDataProtectionProvider protection,
        ExternalDataProtectionPartition partition, TimeProvider clock)
    {
        this.protection = protection;
        this.clock = clock;
        string? endpoint = options.Endpoint;
        string? password = options.Password;
        string? deploymentNamespace = options.Namespace;
        if (partition.Name is "bff" or "combined"
            && deploymentNamespace is { Length: > 0 and <= 64 }
            && deploymentNamespace.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))
        {
            string authority = string.Join('\n', deploymentNamespace, partition.Name,
                ExternalDataProtectionRegistration.ApplicationName(partition.Name), CookieAuthenticationDefaults.AuthenticationScheme);
            keyPrefix = $"event:bff-session:v1:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(authority)))}:";
        }
        connection = new(() => ConnectAsync(endpoint, password));
    }

    public Task<string> StoreAsync(AuthenticationTicket ticket) => StoreAsync(ticket, CancellationToken.None);
    public async Task<string> StoreAsync(AuthenticationTicket ticket, HttpContext httpContext, CancellationToken cancellationToken)
    {
        using var request = CancellationTokenSource.CreateLinkedTokenSource(httpContext.RequestAborted, cancellationToken);
        return await StoreAsync(ticket, request.Token).ConfigureAwait(false);
    }

    public async Task<string> StoreAsync(AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (ticket.Properties.Items.ContainsKey(BffSessionIdentity.HandleProperty)
                || ticket.Properties.Items.ContainsKey(BffSessionIdentity.RevisionProperty)
                || !IsValidIdentity(ticket)) throw Unavailable();
            string handle = FreshValue();
            var candidate = Clone(ticket);
            foreach (var identity in candidate.Principal.Identities)
                foreach (var claim in identity.FindAll(BffSessionIdentity.HandleClaim).ToArray()) identity.RemoveClaim(claim);
            ((ClaimsIdentity)candidate.Principal.Identity!).AddClaim(new Claim(BffSessionIdentity.HandleClaim, handle));
            candidate.Properties.Items[BffSessionIdentity.HandleProperty] = handle;
            candidate.Properties.Items[BffSessionIdentity.RevisionProperty] = FreshValue();
            byte[] payload = Protect(handle, candidate);
            var database = await DatabaseAsync(cancellationToken).ConfigureAwait(false);
            bool stored = await database.StringSetAsync(Key(handle), payload, Lifetime(candidate), When.NotExists)
                .WaitAsync(OperationTimeout, cancellationToken).ConfigureAwait(false);
            if (!stored) throw Unavailable();
            CopyBinding(candidate, ticket);
            return handle;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { throw Unavailable(); }
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket) => RenewAsync(key, ticket, CancellationToken.None);
    public async Task RenewAsync(string key, AuthenticationTicket ticket, HttpContext httpContext, CancellationToken cancellationToken)
    {
        using var request = CancellationTokenSource.CreateLinkedTokenSource(httpContext.RequestAborted, cancellationToken);
        await RenewAsync(key, ticket, request.Token).ConfigureAwait(false);
    }

    public async Task RenewAsync(string key, AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!IsValidStoredTicket(key, ticket)) throw Unavailable();
            var database = await DatabaseAsync(cancellationToken).ConfigureAwait(false);
            RedisKey storageKey = Key(key);
            RedisValue currentValue = await database.StringGetAsync(storageKey)
                .WaitAsync(OperationTimeout, cancellationToken).ConfigureAwait(false);
            var current = Unprotect(key, currentValue);
            if (current is null || !SameIdentity(current, ticket)
                || current.Properties.Items[BffSessionIdentity.RevisionProperty] != ticket.Properties.Items[BffSessionIdentity.RevisionProperty])
                throw Unavailable();
            var candidate = Clone(ticket);
            candidate.Properties.Items[BffSessionIdentity.RevisionProperty] = FreshValue();
            byte[] payload = Protect(key, candidate);
            var transaction = database.CreateTransaction();
            transaction.AddCondition(Condition.StringEqual(storageKey, currentValue));
            Task<bool> replacement = transaction.StringSetAsync(storageKey, payload, Lifetime(candidate), When.Exists);
            // Redis transaction commands complete only after execution; never await the queued write first.
            bool committed = await transaction.ExecuteAsync().WaitAsync(OperationTimeout, cancellationToken).ConfigureAwait(false);
            if (!committed) throw Unavailable();
            if (!await replacement.WaitAsync(OperationTimeout, cancellationToken).ConfigureAwait(false)) throw Unavailable();
            ticket.Properties.Items[BffSessionIdentity.RevisionProperty] = candidate.Properties.Items[BffSessionIdentity.RevisionProperty];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { throw Unavailable(); }
    }

    public Task<AuthenticationTicket?> RetrieveAsync(string key) => RetrieveAsync(key, CancellationToken.None);
    public async Task<AuthenticationTicket?> RetrieveAsync(string key, HttpContext httpContext, CancellationToken cancellationToken)
    {
        using var request = CancellationTokenSource.CreateLinkedTokenSource(httpContext.RequestAborted, cancellationToken);
        return await RetrieveAsync(key, request.Token).ConfigureAwait(false);
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!BffSessionIdentity.IsValidHandle(key)) return null;
        try
        {
            var database = await DatabaseAsync(cancellationToken).ConfigureAwait(false);
            RedisValue payload = await database.StringGetAsync(Key(key)).WaitAsync(OperationTimeout, cancellationToken).ConfigureAwait(false);
            return Unprotect(key, payload);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { throw Unavailable(); }
    }

    public Task RemoveAsync(string key) => RemoveAsync(key, CancellationToken.None);
    public async Task RemoveAsync(string key, HttpContext httpContext, CancellationToken cancellationToken)
    {
        using var request = CancellationTokenSource.CreateLinkedTokenSource(httpContext.RequestAborted, cancellationToken);
        await RemoveAsync(key, request.Token).ConfigureAwait(false);
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var database = await DatabaseAsync(cancellationToken).ConfigureAwait(false);
            _ = await database.KeyDeleteAsync(Key(key)).WaitAsync(OperationTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { throw Unavailable(); }
    }

    public async Task<bool> IsActiveAsync(BffSessionBinding binding, CancellationToken cancellationToken)
    {
        var ticket = await RetrieveAsync(binding.Handle, cancellationToken).ConfigureAwait(false);
        return ticket is not null && BffSessionIdentity.TryGetBinding(ticket.Principal, out var actual) && actual == binding;
    }

    private async Task<IDatabase> DatabaseAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref disposed) != 0 || keyPrefix is null) throw Unavailable();
        var multiplexer = await connection.Value.WaitAsync(OperationTimeout, cancellationToken).ConfigureAwait(false);
        return multiplexer.GetDatabase();
    }

    private static async Task<ConnectionMultiplexer> ConnectAsync(string? endpoint, string? password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length > 4096
            || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme is not ("redis" or "rediss")
            || string.IsNullOrWhiteSpace(uri.Host) || uri.Port is <= 0 or > 65535
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0
            || uri.AbsolutePath is not ("" or "/")) throw Unavailable();
        var configuration = new ConfigurationOptions
        {
            Password = password,
            Ssl = uri.Scheme == "rediss",
            AbortOnConnectFail = false,
            ConnectTimeout = 1000,
            ConnectRetry = 0,
            AsyncTimeout = 1000,
            SyncTimeout = 1000,
            BacklogPolicy = BacklogPolicy.FailFast
        };
        configuration.EndPoints.Add(uri.Host, uri.Port);
        return await ConnectionMultiplexer.ConnectAsync(configuration).ConfigureAwait(false);
    }

    private RedisKey Key(string handle) => keyPrefix is not null && BffSessionIdentity.IsValidHandle(handle)
        ? keyPrefix + handle : throw Unavailable();

    private IDataProtector Protector(string handle) => protection.CreateProtector("islamu-event:bff-session-storage:v1")
        .CreateProtector(keyPrefix ?? throw Unavailable()).CreateProtector(handle);

    private byte[] Protect(string handle, AuthenticationTicket ticket)
    {
        if (!IsValidStoredTicket(handle, ticket)) throw Unavailable();
        byte[] serialized = TicketSerializer.Default.Serialize(ticket);
        if (serialized.Length > MaximumTicketBytes) throw Unavailable();
        byte[] payload = Protector(handle).Protect(serialized);
        return payload.Length <= MaximumProtectedBytes ? payload : throw Unavailable();
    }

    private AuthenticationTicket? Unprotect(string handle, RedisValue value)
    {
        if (value.IsNull || value.Length() > MaximumProtectedBytes) return null;
        try
        {
            byte[] serialized = Protector(handle).Unprotect((byte[])value!);
            if (serialized.Length > MaximumTicketBytes) return null;
            var ticket = TicketSerializer.Default.Deserialize(serialized);
            return ticket is not null && IsValidStoredTicket(handle, ticket) ? ticket : null;
        }
        catch { return null; }
    }

    private bool IsValidIdentity(AuthenticationTicket ticket) =>
        ticket.AuthenticationScheme == CookieAuthenticationDefaults.AuthenticationScheme
        && ticket.Principal.TryGetCircuitSubject(out _)
        && ticket.Properties.ExpiresUtc is { } expiry && expiry > clock.GetUtcNow()
        && ImmutableClaimTypes.All(type => ticket.Principal.FindAll(type).Take(2).Count() <= 1);

    private bool IsValidStoredTicket(string handle, AuthenticationTicket ticket) => IsValidIdentity(ticket)
        && BffSessionIdentity.TryGetBinding(ticket.Principal, out var binding) && binding.Handle == handle
        && ticket.Properties.Items.TryGetValue(BffSessionIdentity.HandleProperty, out var marker) && marker == handle
        && ticket.Properties.Items.TryGetValue(BffSessionIdentity.RevisionProperty, out var revision)
        && BffSessionIdentity.IsValidHandle(revision);

    private static bool SameIdentity(AuthenticationTicket current, AuthenticationTicket candidate) =>
        BffSessionIdentity.TryGetBinding(current.Principal, out var oldBinding)
        && BffSessionIdentity.TryGetBinding(candidate.Principal, out var newBinding) && oldBinding == newBinding
        && ImmutableClaimTypes.All(type => current.Principal.FindAll(type).Select(claim => claim.Value)
            .SequenceEqual(candidate.Principal.FindAll(type).Select(claim => claim.Value), StringComparer.Ordinal));

    private TimeSpan Lifetime(AuthenticationTicket ticket)
    {
        TimeSpan remaining = ticket.Properties.ExpiresUtc!.Value - clock.GetUtcNow();
        return remaining >= TimeSpan.FromMilliseconds(1) ? remaining : throw Unavailable();
    }

    private static AuthenticationTicket Clone(AuthenticationTicket ticket) =>
        new(new ClaimsPrincipal(ticket.Principal.Identities.Select(identity => identity.Clone())),
            new AuthenticationProperties(new Dictionary<string, string?>(ticket.Properties.Items)), ticket.AuthenticationScheme);

    private static void CopyBinding(AuthenticationTicket source, AuthenticationTicket destination)
    {
        foreach (var identity in destination.Principal.Identities)
            foreach (var claim in identity.FindAll(BffSessionIdentity.HandleClaim).ToArray()) identity.RemoveClaim(claim);
        ((ClaimsIdentity)destination.Principal.Identity!).AddClaim(source.Principal.FindFirst(BffSessionIdentity.HandleClaim)!.Clone());
        destination.Properties.Items[BffSessionIdentity.HandleProperty] = source.Properties.Items[BffSessionIdentity.HandleProperty];
        destination.Properties.Items[BffSessionIdentity.RevisionProperty] = source.Properties.Items[BffSessionIdentity.RevisionProperty];
    }

    private static string FreshValue() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private static BffSessionAuthorityUnavailableException Unavailable() => new();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0 && connection.IsValueCreated)
            _ = DisposeConnectionAsync(connection.Value);
        GC.SuppressFinalize(this);
    }

    private static async Task DisposeConnectionAsync(Task<ConnectionMultiplexer> pending)
    {
        try { (await pending.ConfigureAwait(false)).Dispose(); }
        catch { }
    }
}
