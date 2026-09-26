using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.DTOs.User;
using Explore.Application.Features.Users.Requests.Commands;
using Explore.Application.Models;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Secrets.Database;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Explore.Application.Contracts.Operations;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class ConfiguredAdministratorBootstrapTests
{
    private const string StatusRoute = "/api/instanceOnboarding/status";
    private const string SyncRoute = "/api/user/sync";
    private const string ExpectedIssuer = "https://auth.example.test/realms/ISLAMU";
    private const string ExpectedSubject = "configured-admin-subject";
    private const string Fingerprint = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Test]
    [Arguments(ExpectedIssuer, "keycloak", true)]
    [Arguments(ExpectedIssuer, "google", true)]
    [Arguments("https://accounts.google.com", "google", false)]
    public async Task SyncUser_OnlyExactNormalizedIssuerAndSubject_CompletesConfiguredClaim(string issuer, string providerHint, bool matches)
    {
        ProviderAccountKey expected = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(
            ExpectedIssuer,
            ExpectedSubject);
        await using var factory = new ConfiguredClaimFactory(expected);
        using var client = factory.CreateClient();
        await SeedPendingAsync(factory);

        using var request = CreateSyncRequest(
            ("sub", ExpectedSubject),
            ("iss", issuer.Replace("https://", "HTTPS://", StringComparison.Ordinal) + "/"),
            ("idp", providerHint),
            ("email", "configured-admin@example.test"),
            ("given_name", "Configured"),
            ("family_name", "Administrator"));

        using HttpResponseMessage response = await client.SendAsync(request);

        if (!matches)
        {
            await Assert.That(response.IsSuccessStatusCode).IsFalse();
            await Assert.That(await ReadCountsAsync(factory)).IsEqualTo(new DatabaseCounts(0, 0, 0, 0, 0, 0));
            return;
        }
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK)
            .Because($"Actual HTTP status: {(int)response.StatusCode}.");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        InstanceBootstrapState state = await db.InstanceBootstrapStates.SingleAsync();
        UserExternalLogin login = await db.UserExternalLogins.SingleAsync();
        var result = await response.Content.ReadFromJsonAsync<BaseCommandResponse<Guid>>();
        await Assert.That(state.Status).IsEqualTo(InstanceBootstrapStatus.Completed);
        await Assert.That(login.ProviderKey).IsEqualTo(expected.Value);
        await Assert.That(login.AuthenticationProviderId).IsEqualTo((int)expected.ProviderKind);
        await Assert.That(result!.Id).IsEqualTo(login.UserId);
        await Assert.That(state.CompletedByUserId).IsEqualTo(login.UserId);
        await Assert.That(await db.PlatformUserRoles.CountAsync()).IsEqualTo(1);
        await Assert.That((await db.Tenants.SingleAsync()).TenantStatusId).IsEqualTo((int)TenantStatusEnum.Provisioning);
        await AssertPrivateSessionAsync(client, issuer);
    }

    [Test]
    public async Task SyncUser_ExactConfiguredRetryAfterPostCommitFailure_ReplaysClaimEffects()
    {
        ProviderAccountKey expected = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(
            ExpectedIssuer,
            ExpectedSubject);
        var notifier = new FailFirstJwtAuthorityRefreshNotifier();
        await using var factory = new ConfiguredClaimFactory(expected, notifier);
        using var client = factory.CreateClient();
        await SeedPendingAsync(factory);

        using var firstRequest = CreateSyncRequest(
            ("sub", ExpectedSubject),
            ("iss", ExpectedIssuer),
            ("idp", "keycloak"),
            ("email", "configured-admin@example.test"));
        using HttpResponseMessage first = await client.SendAsync(firstRequest);
        using var retryRequest = CreateSyncRequest(
            ("sub", ExpectedSubject),
            ("iss", ExpectedIssuer),
            ("email", "configured-admin@example.test"));
        using HttpResponseMessage retry = await client.SendAsync(retryRequest);

        await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(retry.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(notifier.CallCount).IsEqualTo(2);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await Assert.That(await db.InstanceBootstrapStates.CountAsync(
            state => state.Status == InstanceBootstrapStatus.Completed)).IsEqualTo(1);
        await Assert.That(await db.UserExternalLogins.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task SyncUser_CompletedExactLoginRemainsUsableWhenSelectorAuthorityIsRemoved()
    {
        ProviderAccountKey expected = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(
            ExpectedIssuer,
            ExpectedSubject);
        var provider = new OneShotConfiguredProvider(expected);
        await using var factory = new ConfiguredClaimFactory(expected, configuredProvider: provider);
        using var client = factory.CreateClient();
        await SeedPendingAsync(factory);

        using HttpRequestMessage firstRequest = CreateSyncRequest(
            ("sub", ExpectedSubject),
            ("iss", ExpectedIssuer),
            ("idp", "keycloak"),
            ("email", "configured-admin@example.test"));
        using HttpResponseMessage first = await client.SendAsync(firstRequest);
        using var secondRequest = CreateSyncRequest(
            ("sub", ExpectedSubject),
            ("iss", ExpectedIssuer),
            ("email", "configured-admin@example.test"));
        using HttpResponseMessage second = await client.SendAsync(secondRequest);

        await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(provider.CallCount).IsEqualTo(2);
        DatabaseCounts counts = await ReadCountsAsync(factory);
        await Assert.That(counts).IsEqualTo(new DatabaseCounts(1, 1, 1, 1, 1, 0));
        await AssertPrivateSessionAsync(client, ExpectedIssuer);
    }

    [Test]
    [Arguments("wrong-issuer")]
    [Arguments("session-id")]
    [Arguments("email")]
    [Arguments("username")]
    [Arguments("provider-role")]
    [Arguments("nonmatching-provider")]
    [Arguments("realm-only-issuer")]
    [Arguments("subject-case")]
    [Arguments("subject-whitespace")]
    [Arguments("unrelated-visitor")]
    public async Task SyncUser_IndirectOrWrongAuthority_RejectsTakeoverWithZeroWrites(string attack)
    {
        ProviderAccountKey expected = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(
            ExpectedIssuer,
            ExpectedSubject);
        await using var factory = new ConfiguredClaimFactory(expected);
        using var client = factory.CreateClient();
        await SeedPendingAsync(factory);
        DatabaseCounts before = await ReadCountsAsync(factory);

        (string Type, string Value)[] claims = CreateAttackClaims(attack);
        using var request = CreateSyncRequest(claims);
        using HttpResponseMessage response = await client.SendAsync(request);

        await Assert.That(response.IsSuccessStatusCode).IsFalse();
        await Assert.That(response.Headers.Contains("Set-Cookie")).IsFalse();
        DatabaseCounts after = await ReadCountsAsync(factory);
        await Assert.That(after).IsEqualTo(before);
        await AssertDeniedSessionAsync(client, claims);
    }

    [Test]
    [Arguments("no-bootstrap")]
    [Arguments("interactive")]
    [Arguments("superseded")]
    [Arguments("no-tenant")]
    [Arguments("wrong-tenant")]
    [Arguments("suspended")]
    [Arguments("archived")]
    public async Task SyncUser_IneligiblePrivateInstance_DeniesHttpAndNativeWithoutWrites(string state)
    {
        ProviderAccountKey expected = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(ExpectedIssuer, ExpectedSubject);
        Guid boundTenantId = state == "wrong-tenant" ? Guid.CreateVersion7() : Explore.Domain.Constants.PlatformDefaults.DefaultTenantId;
        await using var factory = new ConfiguredClaimFactory(expected, boundTenantId: boundTenantId);
        using var client = factory.CreateClient();
        await SeedPendingAsync(factory);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var bootstrap = await db.InstanceBootstrapStates.SingleAsync();
            var tenant = await db.Tenants.SingleAsync();
            if (state is "no-bootstrap" or "interactive")
            {
                db.InstanceBootstrapStates.Remove(bootstrap);
                if (state == "interactive")
                    db.InstanceBootstrapStates.Add(InstanceBootstrapState.CreateInteractivePending(
                        Guid.CreateVersion7(), DeploymentMode.SingleTenant, DateTime.UtcNow));
            }
            if (state == "superseded")
                bootstrap.Supersede(Guid.CreateVersion7(), expected.ProviderKind, DeploymentMode.MultiTenant,
                    8, new string('c', 64), Fingerprint, DateTime.UtcNow);
            if (state == "no-tenant") db.Tenants.Remove(tenant);
            if (state == "wrong-tenant") db.Tenants.Add(new Event.Api.IntegrationTests.Builders.TenantBuilder()
                .WithId(boundTenantId).WithStatus(TenantStatusEnum.Provisioning).Build());
            if (state == "suspended") tenant.TenantStatusId = (int)TenantStatusEnum.Suspended;
            if (state == "archived") tenant.TenantStatusId = (int)TenantStatusEnum.Archived;
            await db.SaveChangesAsync();
        }
        DatabaseCounts before = await ReadCountsAsync(factory);
        using var request = CreateSyncRequest(
            ("sub", ExpectedSubject), ("iss", ExpectedIssuer), ("email", "configured-admin@example.test"));
        using var response = await client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(await ReadCountsAsync(factory)).IsEqualTo(before);

        await using var nativeScope = factory.Services.CreateAsyncScope();
        nativeScope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(boundTenantId);
        var native = await nativeScope.ServiceProvider.GetRequiredService<ICommandHandler<SyncUserCommand, BaseCommandResponse<Guid>>>()
            .ExecuteAsync(new SyncUserCommand
            {
                AccountKey = expected,
                UserDto = new UserDto
                {
                    Email = "configured-admin@example.test",
                    AuthProvider = "keycloak",
                    FirstName = "Configured",
                    LastName = "Administrator"
                }
            });
        await Assert.That(native.IsSuccess).IsFalse();
        await Assert.That(await ReadCountsAsync(factory)).IsEqualTo(before);
    }

    [Test]
    public async Task SyncUser_AnonymousCannotClaimOrCreateSession()
    {
        var expected = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(ExpectedIssuer, ExpectedSubject);
        await using var factory = new ConfiguredClaimFactory(expected);
        using var client = factory.CreateClient();
        await SeedPendingAsync(factory);
        DatabaseCounts before = await ReadCountsAsync(factory);
        using var response = await client.PostAsync(SyncRoute, null);
        await Assert.That(response.IsSuccessStatusCode).IsFalse();
        await Assert.That(response.Headers.Contains("Set-Cookie")).IsFalse();
        await Assert.That(await ReadCountsAsync(factory)).IsEqualTo(before);
    }

    [Test]
    public async Task SyncUser_CompletedClaimDoesNotAdmitAnotherIdentityOrAnotherTenant()
    {
        var expected = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(ExpectedIssuer, ExpectedSubject);
        await using var factory = new ConfiguredClaimFactory(expected);
        using var client = factory.CreateClient();
        await SeedPendingAsync(factory);
        using var completionRequest = CreateSyncRequest(("sub", ExpectedSubject), ("iss", ExpectedIssuer));
        using var completed = await client.SendAsync(completionRequest);
        await Assert.That(completed.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            db.Tenants.Add(new Event.Api.IntegrationTests.Builders.TenantBuilder()
                .WithSlug("other-private").WithStatus(TenantStatusEnum.Provisioning).Build());
            await db.SaveChangesAsync();
        }
        await factory.Services.GetRequiredService<ITenantSlugCache>().RefreshAsync();
        DatabaseCounts before = await ReadCountsAsync(factory);
        using var attackerRequest = CreateSyncRequest(CreateAttackClaims("email"));
        using var attacker = await client.SendAsync(attackerRequest);
        await Assert.That(attacker.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await AssertDeniedSessionAsync(client, CreateAttackClaims("email"));
        foreach (string? slug in new string?[] { "other-private", "unknown-tenant", null })
        {
            using var request = CreateSyncRequest(("sub", ExpectedSubject), ("iss", ExpectedIssuer));
            request.Headers.Remove("X-Tenant-Slug");
            if (slug is not null) request.Headers.Add("X-Tenant-Slug", slug);
            using var response = await client.SendAsync(request);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        }
        await Assert.That(await ReadCountsAsync(factory)).IsEqualTo(before);
    }

    [Test]
    public async Task SyncUser_IdempotencyKeyCannotReplayRemovedAccountBinding()
    {
        var expected = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(ExpectedIssuer, ExpectedSubject);
        await using var factory = new ConfiguredClaimFactory(expected);
        using var client = factory.CreateClient();
        await SeedPendingAsync(factory);
        string key = Guid.CreateVersion7().ToString();
        using var firstRequest = CreateSyncRequest(("sub", ExpectedSubject), ("iss", ExpectedIssuer));
        firstRequest.Headers.Add("Idempotency-Key", key);
        using var first = await client.SendAsync(firstRequest);
        await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            await Assert.That(await db.Set<IdempotencyRecord>().CountAsync()).IsEqualTo(0);
            db.UserExternalLogins.Remove(await db.UserExternalLogins.SingleAsync());
            await db.SaveChangesAsync();
        }
        DatabaseCounts before = await ReadCountsAsync(factory);
        using var retryRequest = CreateSyncRequest(("sub", ExpectedSubject), ("iss", ExpectedIssuer));
        retryRequest.Headers.Add("Idempotency-Key", key);
        using var retry = await client.SendAsync(retryRequest);
        await Assert.That(retry.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(retry.Headers.Contains("X-Idempotency-Replay")).IsFalse();
        await Assert.That(await ReadCountsAsync(factory)).IsEqualTo(before);
    }

    [Test]
    public async Task GetStatus_ConfiguredPending_IsStableGetOnlyAndValueFree()
    {
        ProviderAccountKey expected = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(
            ExpectedIssuer,
            ExpectedSubject);
        await using var factory = new ConfiguredClaimFactory(expected);
        using var client = factory.CreateClient();
        await SeedPendingAsync(factory);

        using HttpResponseMessage response = await client.GetAsync(StatusRoute);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        JsonElement root = body.RootElement;

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(root.GetProperty("state").GetString()).IsEqualTo("ConfiguredAdministratorPending");
        await Assert.That(root.GetProperty("mode").GetString()).IsEqualTo("ConfiguredAdministrator");
        await Assert.That(root.GetProperty("provider").GetString()).IsEqualTo("Keycloak");
        await Assert.That(root.GetProperty("generation").GetInt64()).IsEqualTo(7L);
        string[] relations = root.GetProperty("_links")
            .EnumerateObject()
            .Select(property => property.Name)
            .ToArray();
        await Assert.That(relations).IsEquivalentTo(["self"]);

        string serialized = root.GetRawText();
        await Assert.That(serialized).DoesNotContain(ExpectedIssuer);
        await Assert.That(serialized).DoesNotContain(ExpectedSubject);
        await Assert.That(serialized).DoesNotContain("configured-admin@example.test");
        await Assert.That(serialized).DoesNotContain(Fingerprint);

        using HttpResponseMessage post = await client.PostAsJsonAsync(StatusRoute, new
        {
            issuer = ExpectedIssuer,
            subject = ExpectedSubject
        });
        await Assert.That(post.StatusCode).IsEqualTo(HttpStatusCode.MethodNotAllowed);
        await Assert.That((await ReadCountsAsync(factory)).Users).IsEqualTo(0);
    }

    private static (string Type, string Value)[] CreateAttackClaims(string attack)
    {
        if (attack == "unrelated-visitor")
            return [("sub", "visitor-subject"), ("iss", ExpectedIssuer), ("email", "visitor@example.test")];

        var claims = new List<(string Type, string Value)>
        {
            ("sub", attack is "wrong-issuer" or "nonmatching-provider" ? ExpectedSubject : "attacker-subject"),
            ("iss", attack == "wrong-issuer"
                ? "https://other.example.test/realms/ISLAMU"
                : attack == "nonmatching-provider" ? "https://accounts.google.com"
                : attack == "realm-only-issuer" ? "ISLAMU" : ExpectedIssuer),
            ("idp", attack == "nonmatching-provider" ? "google" : "keycloak"),
            ("email", "configured-admin@example.test"),
            ("preferred_username", ExpectedSubject),
            ("roles", "instance-admin"),
            ("sid", ExpectedSubject),
            ("handle", ExpectedSubject)
        };

        if (attack == "session-id")
        {
            claims.RemoveAll(claim => claim.Type == "sub");
            claims.Add(("name", "attacker"));
        }

        if (attack is "subject-case" or "subject-whitespace")
        {
            claims.RemoveAll(claim => claim.Type == "sub");
            claims.Add(("sub", attack == "subject-case" ? ExpectedSubject.ToUpperInvariant() : ExpectedSubject + " "));
        }
        return [.. claims];
    }

    private static async Task AssertPrivateSessionAsync(HttpClient client, string issuer)
    {
        foreach (string path in new[] { "/api/user", "/api/user/admin-authority", "/api/PublicExperience/settings", "/api/Event" })
        {
            using var request = CreateSyncRequest(("sub", ExpectedSubject), ("iss", issuer));
            request.Method = HttpMethod.Get;
            request.RequestUri = new Uri(path, UriKind.Relative);
            using var response = await client.SendAsync(request);
            await Assert.That(response.StatusCode).IsEqualTo(path.StartsWith("/api/user", StringComparison.Ordinal)
                ? HttpStatusCode.OK : HttpStatusCode.NotFound);
            await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        }
    }

    private static async Task AssertDeniedSessionAsync(HttpClient client, (string Type, string Value)[] claims)
    {
        foreach (string path in new[] { "/api/user", "/api/user/admin-authority", "/api/PublicExperience/settings" })
        {
            using var request = CreateSyncRequest(claims);
            request.Method = HttpMethod.Get;
            request.RequestUri = new Uri(path, UriKind.Relative);
            using var response = await client.SendAsync(request);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        }
    }

    private static HttpRequestMessage CreateSyncRequest(params (string Type, string Value)[] claims)
    {
        string payload = JsonSerializer.Serialize(claims.Select(claim => new
        {
            claim.Type,
            claim.Value
        }));
        var request = new HttpRequestMessage(HttpMethod.Post, SyncRoute);
        request.Headers.Add("X-Tenant-Slug", "configured-bootstrap");
        request.Headers.Add(
            TestAuthHandler.AuthHeaderName,
            Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)));
        return request;
    }

    private static async Task SeedPendingAsync(ConfiguredClaimFactory factory,
        AuthenticationProviderKind provider = AuthenticationProviderKind.Keycloak)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        db.Tenants.Add(new Event.Api.IntegrationTests.Builders.TenantBuilder()
            .WithId(Explore.Domain.Constants.PlatformDefaults.DefaultTenantId)
            .WithSlug("configured-bootstrap")
            .WithStatus(TenantStatusEnum.Provisioning)
            .Build());
        db.InstanceBootstrapStates.Add(InstanceBootstrapState.CreateConfiguredAdministratorPending(
            Guid.CreateVersion7(),
            provider,
            DeploymentMode.MultiTenant,
            7,
            new string('b', 64),
            Fingerprint,
            DateTime.UtcNow));
        await db.SaveChangesAsync();
        await factory.Services.GetRequiredService<ITenantSlugCache>().RefreshAsync();
    }

    private static async Task<DatabaseCounts> ReadCountsAsync(ConfiguredClaimFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        return new DatabaseCounts(
            await db.Users.CountAsync(),
            await db.UserExternalLogins.CountAsync(),
            await db.PlatformUserRoles.CountAsync(),
            await db.InstanceBootstrapStates.CountAsync(state => state.Status == InstanceBootstrapStatus.Completed),
            await db.Actors.CountAsync(),
            await db.Set<UserAuthenticationToken>().CountAsync());
    }

    private sealed record DatabaseCounts(int Users, int ExternalLogins, int PlatformRoles, int CompletedStates,
        int Actors, int AuthenticationTokens);

    private sealed class ConfiguredClaimFactory(
        ProviderAccountKey expectedAccount,
        IJwtAuthorityRefreshNotifier? notifier = null,
        IConfiguredAdministratorBootstrapProvider? configuredProvider = null,
        Guid? boundTenantId = null)
        : AuthenticatedWebApplicationFactory
    {
        private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"configured-bootstrap-{Guid.CreateVersion7():N}.db");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            if (boundTenantId.HasValue)
                AdditionalConfiguration["Deployment:DefaultTenantId"] = boundTenantId.Value.ToString();
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveExploreDbContextRegistrations();
                var options = new DbContextOptionsBuilder<ExploreDbContext>();
                ConfigureDatabase(options);
                using (var db = new ExploreDbContext(options.Options)) db.Database.EnsureCreated();
                services.AddDbContextFactory<ExploreDbContext>(ConfigureDatabase);
                services.AddScoped(provider =>
                {
                    var db = provider.GetRequiredService<IDbContextFactory<ExploreDbContext>>().CreateDbContext();
                    db.TenantContext = provider.GetRequiredService<ITenantContext>();
                    db.CurrentUserService = provider.GetRequiredService<ICurrentUserService>();
                    db.ClearTenantFilterBypass();
                    return db;
                });
                services.RemoveAll<IConfiguredAdministratorBootstrapProvider>();
                services.AddSingleton<IConfiguredAdministratorBootstrapProvider>(
                    configuredProvider ?? new ExactConfiguredProvider(expectedAccount));
                if (notifier is not null)
                {
                    services.RemoveAll<IJwtAuthorityRefreshNotifier>();
                    services.AddSingleton(notifier);
                }
            });
        }

        private void ConfigureDatabase(DbContextOptionsBuilder options)
        {
            PrimaryDatabaseProviderComposition.ConfigureApplication(options, new PrimaryDatabaseConnectionOptions
            {
                Role = PrimaryDatabaseRole.Runtime,
                Provider = PrimaryDatabaseProvider.Sqlite,
                Database = _databasePath
            });
            options.UseSnakeCaseNamingConvention();
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            var options = new DbContextOptionsBuilder<ExploreDbContext>();
            ConfigureDatabase(options);
            await using var db = new ExploreDbContext(options.Options);
            SqliteConnection.ClearPool((SqliteConnection)db.Database.GetDbConnection());
            File.Delete(_databasePath);
            File.Delete(_databasePath + "-wal");
            File.Delete(_databasePath + "-shm");
        }
    }

    private sealed class FailFirstJwtAuthorityRefreshNotifier : IJwtAuthorityRefreshNotifier
    {
        public int CallCount { get; private set; }

        public Task ReloadAsync(CancellationToken ct = default)
        {
            CallCount++;
            return CallCount == 1
                ? Task.FromException(new InvalidOperationException("Injected post-commit effect failure."))
                : Task.CompletedTask;
        }
    }

    private sealed class ExactConfiguredProvider(ProviderAccountKey expectedAccount)
        : IConfiguredAdministratorBootstrapProvider
    {
        public Task<ConfiguredAdministratorBootstrapBinding?> GetVerifiedBindingAsync(
            ProviderAccountKey authenticatedAccount,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConfiguredAdministratorBootstrapBinding? binding = authenticatedAccount == expectedAccount
                ? new ConfiguredAdministratorBootstrapBinding(
                    expectedAccount,
                    7,
                    Fingerprint,
                    new CompleteInstanceOnboardingRequest
                    {
                        DeploymentMode = DeploymentMode.MultiTenant,
                        SiteProfile = new SelfHostOnboardingProfileDto { SiteName = "Integration Instance" },
                        AdministrationAccessMode = CompleteInstanceOnboardingRequest.EmbeddedAdministrationAccess
                    },
                    new ConfiguredAdministratorProfile(
                        "configured-admin@example.test",
                        "Configured",
                        "Administrator"))
                : null;
            return Task.FromResult(binding);
        }
    }

    private sealed class OneShotConfiguredProvider(ProviderAccountKey expectedAccount)
        : IConfiguredAdministratorBootstrapProvider
    {
        private readonly ExactConfiguredProvider _inner = new(expectedAccount);

        public int CallCount { get; private set; }

        public Task<ConfiguredAdministratorBootstrapBinding?> GetVerifiedBindingAsync(
            ProviderAccountKey authenticatedAccount,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return CallCount == 1
                ? _inner.GetVerifiedBindingAsync(authenticatedAccount, cancellationToken)
                : Task.FromResult<ConfiguredAdministratorBootstrapBinding?>(null);
        }
    }
}
