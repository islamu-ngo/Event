using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Middleware;
using Explore.Application.Authentication;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IO;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

[Category(TestCategories.Security)]
[NotInParallel]
public sealed class ProviderCredentialHttpBoundaryTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private const string InternalPath = "/api/instanceonboarding/auth-provider-configuration/internal";

    public enum Route { SetupPolicySync, SettingsPolicySync }
    public enum Outcome { Success, Validation, ProviderRejection }
    public enum Caller { Anonymous, TenantAdmin, RevokedAdmin, CompletedSetup, ForgedSetup, SetupOnly }

    public static IEnumerable<Route> Routes() => Enum.GetValues<Route>();
    public static IEnumerable<(Route, Outcome)> Outcomes() =>
        from route in Routes() from outcome in Enum.GetValues<Outcome>() select (route, outcome);
    public static IEnumerable<(Route, Caller)> DeniedCallers() =>
        from route in Routes() from caller in Enum.GetValues<Caller>()
        where caller != Caller.SetupOnly || !IsSetup(route)
        select (route, caller);

    [Test]
    [MethodDataSource(nameof(Outcomes))]
    public async Task CredentialResponsesArePrivateNoStore(Route route, Outcome outcome)
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Provider.Reject = outcome == Outcome.ProviderRejection;
        using var response = await fixture.SendAsync(route, invalidBody: outcome == Outcome.Validation);
        await Assert.That(response.StatusCode).IsEqualTo(ExpectedOutcome(route, outcome));
        await Assert.That(fixture.Provider.Calls).IsEqualTo(outcome == Outcome.Validation ? 0 : 1);
        await AssertPrivateAsync(response);
    }

    [Test]
    [MethodDataSource(nameof(DeniedCallers))]
    public async Task CurrentAuthorityDenialsRemainPrivateAndDoNotContactProvider(Route route, Caller caller)
    {
        await using var fixture = await Fixture.CreateAsync();
        bool bearer = caller is Caller.TenantAdmin or Caller.RevokedAdmin or Caller.CompletedSetup;
        string? setup = null;
        if (caller is Caller.TenantAdmin or Caller.RevokedAdmin or Caller.CompletedSetup)
            await fixture.RevokeAsync(tenantAdmin: caller == Caller.TenantAdmin);
        if (caller == Caller.CompletedSetup)
        {
            fixture.Setup.Lock();
            setup = fixture.Setup.Secret;
        }
        if (caller == Caller.ForgedSetup) setup = Canary();
        if (caller == Caller.SetupOnly) setup = fixture.Setup.Secret;

        // A tenant hint and a provider credential are not instance authority. This is an API
        // boundary test, not a claim that the browser's input is trusted BFF forwarding.
        using var response = await fixture.SendAsync(route, bearer: bearer, setup: setup,
            useDefaultSetup: false, forgedTenant: true);
        HttpStatusCode expected = IsSetup(route)
            ? caller == Caller.CompletedSetup ? HttpStatusCode.Gone : HttpStatusCode.Forbidden
            : bearer ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized;
        await Assert.That(response.StatusCode).IsEqualTo(expected);
        await Assert.That(fixture.Provider.Calls).IsEqualTo(0);
        await Assert.That(await fixture.RecordCountAsync()).IsEqualTo(0);
        await AssertPrivateAsync(response);
    }

    [Test]
    [MethodDataSource(nameof(Routes))]
    public async Task HistoricalSuccessCannotReplaceCurrentAuthority(Route route)
    {
        await using var fixture = await Fixture.CreateAsync();
        string key = Canary();
        fixture.Observer.CaptureIdentity = true;
        using var first = await fixture.SendAsync(route, key: key);
        await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);
        int retainedAfterSuccess = await fixture.RecordCountAsync();
        IdempotencyRequestIdentity identity = fixture.Observer.Identity
            ?? throw new InvalidOperationException("The real routed request identity was not observed.");
        fixture.Observer.CaptureIdentity = false;
        string marker = Canary();
        await fixture.SeedHistoricalAsync(key, identity, marker);
        if (IsSetup(route)) fixture.Setup.Lock();
        else await fixture.RevokeAsync();
        int priorCalls = fixture.Provider.Calls;

        // The very same JWT remains valid; only persisted authority/setup state changes.
        using var authenticated = await fixture.Client.GetAsync("/api/user");
        await Assert.That(authenticated.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var denied = await fixture.SendAsync(route, key: key);
        string body = await denied.Content.ReadAsStringAsync();
        bool disclosedMarker = body.Contains(marker, StringComparison.Ordinal);
        using (Assert.Multiple())
        {
            await Assert.That(retainedAfterSuccess).IsEqualTo(0).Because("credential success must not create a generic replay record");
            await Assert.That(denied.StatusCode).IsEqualTo(IsSetup(route) ? HttpStatusCode.Gone : HttpStatusCode.Forbidden)
                .Because("current setup or persisted instance authority must run before historical output can be disclosed");
            await Assert.That(disclosedMarker).IsFalse().Because("historical credential marker must never be disclosed");
            await Assert.That(denied.Headers.Contains("X-Idempotency-Replay")).IsFalse();
            await Assert.That(fixture.Provider.Calls - priorCalls).IsEqualTo(0);
            await AssertPrivateAsync(denied);
        }
    }

    [Test]
    [MethodDataSource(nameof(Routes))]
    public async Task SameKeySequentialRetryReachesCurrentProviderOutcome(Route route)
    {
        await using var fixture = await Fixture.CreateAsync();
        string key = Canary();
        using var first = await fixture.SendAsync(route, key: key);
        await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);
        fixture.Provider.Reject = true;
        using var second = await fixture.SendAsync(route, key: key);
        JsonElement result = await ReadAsync(second);
        using (Assert.Multiple())
        {
            await Assert.That(second.StatusCode).IsEqualTo(ExpectedOutcome(route, Outcome.ProviderRejection));
            await Assert.That(result.TryGetProperty("message", out var message) && message.GetString() == "provider-rejected"
                || result.TryGetProperty("detail", out var detail) && detail.GetString() == "provider-rejected").IsTrue()
                .Because("a same-key retry must report the current provider rejection, not historical success");
            await Assert.That(fixture.Provider.Calls).IsEqualTo(2);
            await Assert.That(second.Headers.Contains("X-Idempotency-Replay")).IsFalse();
            await Assert.That(await fixture.RecordCountAsync()).IsEqualTo(0);
        }
    }

    [Test]
    [MethodDataSource(nameof(Routes))]
    public async Task SameKeyConcurrentRequestsBothReachProviderBeforeCompletion(Route route)
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Provider.Block = true;
        string key = Canary();
        Task firstEntered = fixture.Provider.FirstEntered.Task;
        Task secondEntered = fixture.Provider.SecondEntered.Task;
        Task<HttpResponseMessage> first = fixture.SendAsync(route, key: key);
        await firstEntered.WaitAsync(Deadline);
        Task<HttpResponseMessage> second = fixture.SendAsync(route, key: key);
        // RED returns 409 before the provider; GREEN reaches the second barrier. Neither
        // path waits for a timer to infer concurrency, and cleanup always releases the first.
        bool reachedSecond;
        try
        {
            await Task.WhenAny(secondEntered, second).WaitAsync(Deadline);
            reachedSecond = secondEntered.IsCompletedSuccessfully;
        }
        finally
        {
            fixture.Provider.Release.TrySetResult();
        }
        using var firstResponse = await first.WaitAsync(Deadline);
        using var secondResponse = await second.WaitAsync(Deadline);
        using (Assert.Multiple())
        {
            await Assert.That(reachedSecond).IsTrue().Because("both same-key requests must enter the current provider operation before release");
            await Assert.That(firstResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(secondResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(firstResponse.Headers.Contains("X-Idempotency-Replay") || secondResponse.Headers.Contains("X-Idempotency-Replay")).IsFalse();
            await Assert.That(await fixture.RecordCountAsync()).IsEqualTo(0);
        }
    }

    [Test]
    [MethodDataSource(nameof(Routes))]
    public async Task CancellationLeavesNoGenericReplayRecord(Route route)
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Provider.Block = true;
        fixture.Observer.ObserveCompletion = true;
        using var cancellation = new CancellationTokenSource();
        Task entered = fixture.Provider.FirstEntered.Task;
        Task finished = fixture.Observer.Finished.Task;
        Task<HttpResponseMessage> request = fixture.SendAsync(route, key: Canary(), cancellationToken: cancellation.Token);
        await entered.WaitAsync(Deadline);
        cancellation.Cancel();
        bool cancelled = false;
        try
        {
            using var response = await request.WaitAsync(Deadline);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            cancelled = true;
        }
        await finished.WaitAsync(Deadline);
        await Assert.That(cancelled).IsTrue();
        await Assert.That(await fixture.RecordCountAsync()).IsEqualTo(0)
            .Because("aborted credential requests must not leave even an in-progress generic replay record");
        // No response was sent: do not invent a status or a cache-header assertion.
    }

    [Test]
    [MethodDataSource(nameof(Routes))]
    public async Task SuppressionPrecedesGenericKeyValidation(Route route)
    {
        await using var fixture = await Fixture.CreateAsync();
        using var response = await fixture.SendAsync(route, key: new string('k', 129));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK)
            .Because("suppression must bypass generic idempotency key validation on the actual action");
        await Assert.That(fixture.Provider.Calls).IsEqualTo(1);
        await Assert.That(await fixture.RecordCountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments(false, false, HttpStatusCode.OK)]
    [Arguments(true, false, HttpStatusCode.Forbidden)]
    [Arguments(false, true, HttpStatusCode.Gone)]
    public async Task InternalAuthConfigurationGetIsPrivateNoStore(bool missingSecret, bool completed, HttpStatusCode expected)
    {
        await using var fixture = await Fixture.CreateAsync();
        if (completed) fixture.Setup.Lock();
        using var request = new HttpRequestMessage(HttpMethod.Get, InternalPath);
        if (!missingSecret) request.Headers.Add("X-Setup-Secret", fixture.Setup.Secret);
        request.Headers.Add("Idempotency-Key", Canary());
        using var response = await fixture.Client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(expected);
        await Assert.That(response.Headers.Contains("X-Idempotency-Replay")).IsFalse();
        await Assert.That(await fixture.RecordCountAsync()).IsEqualTo(0);
        await AssertPrivateAsync(response);
    }

    [Test]
    [Arguments("GET", "/api/instance/keycloak/connection", false, HttpStatusCode.OK)]
    [Arguments("POST", "/api/instance/keycloak/inspect", true, HttpStatusCode.BadRequest)]
    [Arguments("POST", "/api/instance/keycloak/plans", true, HttpStatusCode.BadRequest)]
    [Arguments("GET", "/api/instance/keycloak/operations/019db1de-1723-7acd-bada-222222222222", false, HttpStatusCode.NotFound)]
    [Arguments("POST", "/api/instance/keycloak/operations/019db1de-1723-7acd-bada-222222222222/apply", true, HttpStatusCode.BadRequest)]
    [Arguments("POST", "/api/instance/keycloak/operations/019db1de-1723-7acd-bada-222222222222/reconcile", true, HttpStatusCode.BadRequest)]
    [Arguments("POST", "/api/instance/keycloak/operations/019db1de-1723-7acd-bada-222222222222/cancel", false, HttpStatusCode.NotFound)]
    public async Task KeycloakOperationRoutes_AcceptCurrentSetupAuthorityAndRemainPrivate(
        string method,
        string path,
        bool malformedBody,
        HttpStatusCode expected)
    {
        await using var fixture = await Fixture.CreateAsync();
        using var request = new HttpRequestMessage(
            new HttpMethod(method),
            path);
        request.Headers.TryAddWithoutValidation("Authorization", string.Empty);
        request.Headers.Add("X-Setup-Secret", fixture.Setup.Secret);
        request.Headers.Add("Idempotency-Key", new string('k', 129));
        if (malformedBody)
        {
            request.Content = new StringContent(
                "{",
                Encoding.UTF8,
                "application/json");
        }

        using HttpResponseMessage response =
            await fixture.Client.SendAsync(request);
        string body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode)
            .IsEqualTo(expected)
            .Because(body);
        await Assert.That(response.Headers.Contains("X-Idempotency-Replay"))
            .IsFalse();
        await Assert.That(await fixture.RecordCountAsync()).IsEqualTo(0);
        await AssertPrivateAsync(response);
    }

    private static bool IsSetup(Route route) =>
        route == Route.SetupPolicySync;
    private static string Path(Route route) => route switch
    {
        Route.SetupPolicySync => "/api/instanceonboarding/authz-provider-configuration/sync",
        Route.SettingsPolicySync => "/api/instance/settings/authz-provider/sync",
        _ => throw new ArgumentOutOfRangeException(nameof(route))
    };
    private static HttpStatusCode ExpectedOutcome(Route route, Outcome outcome) => outcome switch
    {
        Outcome.Validation => HttpStatusCode.BadRequest,
        Outcome.ProviderRejection => HttpStatusCode.BadRequest,
        _ => HttpStatusCode.OK
    };
    private static string Canary() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }
    private static async Task AssertPrivateAsync(HttpResponseMessage response)
    {
        bool privateNoStore = response.Headers.CacheControl is { Private: true, NoStore: true };
        bool noCache = response.Headers.Pragma.Any(value => value.Name == "no-cache");
        bool noReferrer = response.Headers.TryGetValues("Referrer-Policy", out var values)
            && values.Single() == "no-referrer";
        await Assert.That(privateNoStore && noCache && noReferrer).IsTrue()
            .Because("the real endpoint must emit Cache-Control private/no-store, Pragma no-cache and Referrer-Policy no-referrer, including early denials");
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly ExternalApiPhase0WebApplicationFactory _root;
        private readonly WebApplicationFactory<Program> _host;
        private readonly string _bearer;
        private readonly Dictionary<Route, object> _bodies;
        private readonly Guid _userId;
        public HttpClient Client { get; }
        public SetupAuthority Setup { get; }
        public ProviderBoundary Provider { get; }
        public RequestObserver Observer { get; }

        private Fixture(ExternalApiPhase0WebApplicationFactory root, WebApplicationFactory<Program> host,
            HttpClient client, SetupAuthority setup, ProviderBoundary provider, RequestObserver observer, Guid userId)
        {
            _root = root;
            _host = host;
            Client = client;
            Setup = setup;
            Provider = provider;
            Observer = observer;
            _userId = userId;
            _bearer = root.CreateJwt(userId);
            Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _bearer);
            string username = Canary();
            string password = Canary();
            _bodies = new()
            {
                [Route.SetupPolicySync] = new AuthorizationPolicyPackageSyncRequestDto { AdminUsername = username, AdminPassword = password },
                [Route.SettingsPolicySync] = new AuthorizationPolicyPackageSyncRequestDto { AdminUsername = username, AdminPassword = password }
            };
        }

        public static async Task<Fixture> CreateAsync()
        {
            var setup = new SetupAuthority();
            var provider = new ProviderBoundary();
            var observer = new RequestObserver();
            var configuration = new ConfigurationStore();
            var root = new ExternalApiPhase0WebApplicationFactory
            {
                DeploymentMode = DeploymentMode.SingleTenant,
                SetupSecretProviderOverride = setup,
                AuthProviderConfigurationServiceOverride = configuration
            };
            WebApplicationFactory<Program> host = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPolicyPackageService>();
                services.AddSingleton<IPolicyPackageService>(provider);
                // Only configuration storage/provider I/O is substituted. Authority, native
                // handlers, routing, middleware and IIdempotencyRepository stay production.
                var authorizationConfiguration = Substitute.For<IAuthorizationProviderConfigurationService>();
                authorizationConfiguration.ReadConfigurationAsync().Returns(new AuthorizationProviderConfigurationDto
                {
                    Provider = "cerbos", AuthorizationProviderManagedByDeployment = false
                });
                services.RemoveAll<IAuthorizationProviderConfigurationService>();
                services.AddSingleton(authorizationConfiguration);
                services.RemoveAll<IJwtAuthorityRefreshNotifier>();
                services.AddSingleton(Substitute.For<IJwtAuthorityRefreshNotifier>());
                services.AddSingleton<IStartupFilter>(observer);
            }));
            HttpClient client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            using var scope = host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            Guid userId = db.InstanceBootstrapStates.Single().CompletedByUserId!.Value;
            db.Users.Add(new User
            {
                Id = userId, CreatedAt = DateTime.UtcNow, CreatedBy = userId,
                Pii = new UserPii { UserId = userId, Email = $"{userId:N}@integration.test", FirstName = "Boundary", LastName = "Admin" }
            });
            db.UserExternalLogins.Add(new UserExternalLogin
            {
                Id = Guid.CreateVersion7(), UserId = userId, User = null!,
                AuthenticationProviderId = (int)"keycloak".ParseAuthenticationProviderKind(), AuthenticationProvider = null!,
                ProviderKey = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(ExternalApiPhase0WebApplicationFactory.TestIssuer, userId.ToString()).Value,
                ProviderDisplayName = "keycloak", CreatedAt = DateTime.UtcNow, CreatedBy = userId
            });
            Role? role = db.Roles.SingleOrDefault(candidate => candidate.Id == (int)RoleEnum.Admin);
            if (role is null)
            {
                role = new Role { Id = (int)RoleEnum.Admin, MasterCode = "platform.admin", FullName = "Platform Administrator", Scope = RoleScopeEnum.Platform, RoleScope = null!, IsSystem = true };
                db.Roles.Add(role);
            }
            db.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = Guid.CreateVersion7(), UserId = userId, User = null!, RoleId = role.Id, Role = role,
                GrantedAt = DateTime.UtcNow, GrantedBy = userId
            });
            await db.SaveChangesAsync();
            return new Fixture(root, host, client, setup, provider, observer, userId);
        }

        public async Task<HttpResponseMessage> SendAsync(Route route, string? key = null, bool invalidBody = false,
            bool bearer = true, string? setup = null, bool useDefaultSetup = true, bool forgedTenant = false,
            CancellationToken cancellationToken = default)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Path(route));
            request.Content = invalidBody
                ? new StringContent("{", Encoding.UTF8, "application/json")
                : JsonContent.Create(_bodies[route], _bodies[route].GetType());
            // An explicit empty Authorization value prevents DefaultRequestHeaders inheritance.
            request.Headers.TryAddWithoutValidation("Authorization", bearer ? $"Bearer {_bearer}" : string.Empty);
            if (useDefaultSetup && IsSetup(route)) setup = Setup.Secret;
            if (setup is not null) request.Headers.Add("X-Setup-Secret", setup);
            if (key is not null) request.Headers.Add("Idempotency-Key", key);
            if (forgedTenant) request.Headers.Add("X-Tenant-Id", Guid.CreateVersion7().ToString());
            return await Client.SendAsync(request, cancellationToken);
        }

        public async Task RevokeAsync(bool tenantAdmin = false)
        {
            using var scope = _host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            db.PlatformUserRoles.RemoveRange(db.PlatformUserRoles.Where(grant => grant.UserId == _userId));
            if (tenantAdmin)
            {
                var member = new TenantUser
                {
                    Id = Guid.CreateVersion7(), TenantId = PlatformDefaults.DefaultTenantId, Tenant = null!, UserId = _userId, User = null!,
                    StatusId = (int)TenantUserStatusEnum.Active, JoinedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow
                };
                db.TenantUserRoleGrants.Add(new TenantUserRoleGrant
                {
                    Id = Guid.CreateVersion7(), TenantId = member.TenantId, Tenant = null!, TenantUserId = member.Id, TenantUser = member,
                    RoleId = (int)RoleEnum.TenantAdmin, Role = null!, RoleScopeId = (int)RoleScopeEnum.Tenant,
                    GrantedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow
                });
            }
            await db.SaveChangesAsync();
        }

        public async Task<int> RecordCountAsync()
        {
            using var scope = _host.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<ExploreDbContext>().IdempotencyRecords.CountAsync();
        }

        public async Task SeedHistoricalAsync(string key, IdempotencyRequestIdentity identity, string marker)
        {
            using var scope = _host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            db.IdempotencyRecords.RemoveRange(db.IdempotencyRecords);
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<IIdempotencyRepository>().SaveAsync(new IdempotencyRecord
            {
                Id = Guid.CreateVersion7(), Key = key, TenantId = PlatformDefaults.DefaultTenantId,
                UserId = identity.UserId, RequestMethod = identity.Method, RequestTarget = identity.RequestTarget,
                RequestContentType = identity.ContentType, RequestBodyHash = identity.BodyHash, PrincipalFingerprint = identity.PrincipalFingerprint,
                StatusCode = StatusCodes.Status200OK, ContentType = "application/json", ResponseBody = JsonSerializer.Serialize(new { credential = marker }),
                CreatedAt = DateTime.UtcNow.AddMinutes(-1), ExpiresAt = DateTime.UtcNow.AddHours(1)
            });
        }

        public async ValueTask DisposeAsync()
        {
            Provider.Release.TrySetResult();
            Client.Dispose();
            await _host.DisposeAsync();
            await _root.DisposeAsync();
        }
    }

    private sealed class SetupAuthority : ISetupSecretProvider
    {
        public string Secret { get; } = Canary();
        public bool IsSetupModeActive { get; private set; } = true;
        public bool IsSetupSecretRequired => true;
        public bool IsFromEnvironmentVariable => false;
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public bool ValidateSecret(string? secret) => IsSetupModeActive && string.Equals(secret, Secret, StringComparison.Ordinal);
        public void Lock() => IsSetupModeActive = false;
    }

    // Observes the real request after routing/authentication without manufacturing endpoint metadata
    // or changing its result. Request completion is also an exact cancellation-cleanup barrier.
    private sealed class RequestObserver : IStartupFilter
    {
        public bool CaptureIdentity { get; set; }
        public bool ObserveCompletion { get; set; }
        public IdempotencyRequestIdentity? Identity { get; private set; }
        public TaskCompletionSource Finished { get; } = Signal();
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, continuation) =>
            {
                if (CaptureIdentity) context.Request.EnableBuffering();
                try
                {
                    await continuation(context);
                    if (CaptureIdentity)
                        Identity = await IdempotencyRequestIdentityFactory.CreateAsync(context, new RecyclableMemoryStreamManager(), CancellationToken.None);
                }
                finally
                {
                    if (ObserveCompletion) Finished.TrySetResult();
                }
            });
            next(app);
        };
    }

    private sealed class ConfigurationStore : IAuthProviderConfigurationService
    {
        private AuthProviderConfigurationDto _configuration = new()
        {
            PrimaryProviderId = (int)AuthenticationProviderKind.Keycloak, PrimaryProviderCode = "keycloak",
            KeycloakAuthority = ExternalApiPhase0WebApplicationFactory.TestIssuer, KeycloakClientId = "boundary-bff",
            KeycloakClientSecret = Canary()
        };
        public Task<AuthProviderConfigurationDto> ReadConfigurationAsync() => Task.FromResult(_configuration with { KeycloakClientSecret = string.Empty });
        public Task<AuthProviderConfigurationDto> ReadConfigurationWithSecretsAsync() => Task.FromResult(_configuration with { });
        public Task<bool> IsConfiguredAsync() => Task.FromResult(true);
        public Task ApplyConfigurationAsync(AuthProviderConfigurationDto configuration, IReadOnlySet<string>? suppliedKeys = null, CancellationToken cancellationToken = default)
        {
            _configuration = configuration with { };
            return Task.CompletedTask;
        }
    }

    private sealed class ProviderBoundary : IPolicyPackageService
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public bool Reject { get; set; }
        public bool Block { get; set; }
        public TaskCompletionSource FirstEntered { get; } = Signal();
        public TaskCompletionSource SecondEntered { get; } = Signal();
        public TaskCompletionSource Release { get; } = Signal();
        private string Message => Reject ? "provider-rejected" : "provider-accepted";
        private async Task EnterAsync(CancellationToken cancellationToken)
        {
            int call = Interlocked.Increment(ref _calls);
            if (call == 1) FirstEntered.TrySetResult();
            if (call == 2) SecondEntered.TrySetResult();
            if (Block) await Release.Task.WaitAsync(Deadline, cancellationToken);
        }
        public async Task<PolicyPackagePublishResult> PublishAsync(CancellationToken cancellationToken = default, PolicyPackageAdminCredentials? oneTimeCredentials = null)
        {
            await EnterAsync(cancellationToken);
            return new(!Reject, "boundary", "boundary-revision", Message, DateTimeOffset.UtcNow, []);
        }
        public Task<PolicyPackagePublishResult> PublishInstanceAsync(CancellationToken cancellationToken = default, PolicyPackageAdminCredentials? oneTimeCredentials = null)
            => PublishAsync(cancellationToken, oneTimeCredentials);
        public Task<PolicyPackageManifest> BuildManifestAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected provider manifest operation.");
        public Task<PolicyPackageStatusResult> GetStatusAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected provider status operation.");
        public Task<PolicyPackageArchive> ExportArchiveAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected provider export operation.");
    }
}
