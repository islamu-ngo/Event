using System.Text;
using System.Text.Json;
using Event.Web.BffHosting.Authentication;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using TUnit.Assertions.Enums;

namespace Explore.Blazor.IntegrationTests.Services;

public sealed class BffAdminClaimsTransformationTests
{
    private static readonly Guid InternalUserId = Guid.Parse("0190f50d-1690-7000-8000-000000000001");

    [Test]
    public async Task EnrichPrincipalAsync_WhenSynchronizing_SyncsBeforeAuthorityAndUsesSameBearer()
    {
        var handler = new IdentityReadinessHandler();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var client = new HttpClient(handler) { BaseAddress = new("https://api.example/") };
        var service = CreateService(client, cache);
        var principal = CreatePrincipal();

        var result = await service.EnrichPrincipalAsync(
            principal,
            CreateProperties(),
            synchronizeUser: true);

        await Assert.That(result).IsTrue();
        await Assert.That(handler.Requests.Count).IsEqualTo(2);
        await Assert.That(handler.Requests[0].Method).IsEqualTo(HttpMethod.Post.Method);
        await Assert.That(handler.Requests[0].Path).IsEqualTo("/api/user/sync");
        await Assert.That(handler.Requests[1].Method).IsEqualTo(HttpMethod.Get.Method);
        await Assert.That(handler.Requests[1].Path).IsEqualTo("/api/user/admin-authority");
        await Assert.That(handler.Requests.All(request => request.Authorization == "Bearer access-token")).IsTrue();
        await Assert.That(principal.FindFirst("internal_user_id")?.Value).IsEqualTo(InternalUserId.ToString());
        await Assert.That(principal.HasClaim("explore:admin:instance", "true")).IsTrue();
    }

    [Test]
    public async Task EnrichPrincipalAsync_WhenSyncFails_ClearsStaleClaimsAndSkipsAuthority()
    {
        var handler = new IdentityReadinessHandler { SyncStatusCode = HttpStatusCode.BadRequest };
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var client = new HttpClient(handler) { BaseAddress = new("https://api.example/") };
        var service = CreateService(client, cache);
        var principal = CreatePrincipal(("explore:admin:instance", "true"));

        var result = await service.EnrichPrincipalAsync(
            principal,
            CreateProperties(),
            synchronizeUser: true);

        await Assert.That(result).IsFalse();
        await Assert.That(handler.Requests.Count).IsEqualTo(1);
        await Assert.That(handler.Requests[0].Path).IsEqualTo("/api/user/sync");
        await Assert.That(principal.FindFirst("internal_user_id")).IsNull();
        await Assert.That(principal.HasClaim("explore:admin:instance", "true")).IsFalse();
    }

    [Test]
    public async Task EnrichPrincipalAsync_AfterSuccessfulSync_InvalidatesStaleNegativeAuthorityCache()
    {
        var handler = new IdentityReadinessHandler
        {
            Authority = new AdminAuthorityDto { HasAnyAuthority = false }
        };
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var client = new HttpClient(handler) { BaseAddress = new("https://api.example/") };
        var service = CreateService(client, cache);
        var principal = CreatePrincipal();
        var properties = CreateProperties();

        var initialResult = await service.EnrichPrincipalAsync(principal, properties);
        await Assert.That(initialResult).IsFalse();

        handler.Requests.Clear();
        handler.Authority = CreateAdminAuthority();

        var synchronizedResult = await service.EnrichPrincipalAsync(
            principal,
            properties,
            synchronizeUser: true);

        await Assert.That(synchronizedResult).IsTrue();
        await Assert.That(handler.Requests.Count).IsEqualTo(2);
        await Assert.That(handler.Requests[0].Path).IsEqualTo("/api/user/sync");
        await Assert.That(handler.Requests[1].Path).IsEqualTo("/api/user/admin-authority");
    }

    [Test]
    public async Task EnrichPrincipalAsyncUsesProviderSubjectAcrossSessionChanges()
    {
        using var handler = new IdentityReadinessHandler();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var client = new HttpClient(handler) { BaseAddress = new("https://api.example/") };
        var service = CreateService(client, cache);
        var first = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("sub", "provider-subject"),
            new Claim("sid", "session-one")
        ], "Cookies"));
        var second = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("sub", "provider-subject"),
            new Claim("sid", "session-two")
        ], "Cookies"));

        var firstResult = await service.EnrichPrincipalAsync(first, CreateProperties());
        handler.Authority = new AdminAuthorityDto { HasAnyAuthority = false };
        var secondResult = await service.EnrichPrincipalAsync(second, CreateProperties());

        await Assert.That(firstResult).IsTrue();
        await Assert.That(secondResult).IsTrue();
        await Assert.That(second.HasClaim("explore:admin:instance", "true")).IsTrue();
        await Assert.That(handler.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task EnrichPrincipalAsyncWithOnlyPlatformIdentityClaimFailsClosed()
    {
        using var handler = new IdentityReadinessHandler();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var client = new HttpClient(handler) { BaseAddress = new("https://api.example/") };
        var service = CreateService(client, cache);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("internal_user_id", InternalUserId.ToString("D"))
        ], "Cookies"));

        var result = await service.EnrichPrincipalAsync(principal, CreateProperties());

        await Assert.That(result).IsFalse();
        await Assert.That(principal.HasClaim("explore:admin:instance", "true")).IsFalse();
        await Assert.That(handler.Requests).IsEmpty();
    }


    [Test]
    public async Task EnrichPrincipalAsyncSupportsSidOnlyProviderSessionFallback()
    {
        using var handler = new IdentityReadinessHandler();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var client = new HttpClient(handler) { BaseAddress = new("https://api.example/") };
        var service = CreateService(client, cache);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("sid", "session-only-subject")
        ], "Cookies"));

        var result = await service.EnrichPrincipalAsync(principal, CreateProperties());

        await Assert.That(result).IsTrue();
        await Assert.That(principal.HasClaim("explore:admin:instance", "true")).IsTrue();
        await Assert.That(handler.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ConfiguredProviderSyncRefreshesStatusThenEnrichesAuthorityExactlyOnce()
    {
        var events = new List<string>();
        var onboarding = new ClaimCompletingOnboardingStatusProvider(events, "Keycloak");
        using var handler = new IdentityReadinessHandler(events);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var client = new HttpClient(handler) { BaseAddress = new("https://api.example/") };
        var service = CreateService(client, cache, onboarding);
        var principal = CreatePrincipal(("auth_provider", "keycloak"));

        var result = await service.EnrichPrincipalAsync(
            principal,
            CreateProperties(),
            forceRefresh: true,
            synchronizeUser: true);

        await Assert.That(result).IsTrue();
        await Assert.That(events).IsEquivalentTo([
            "status:pending",
            "http:sync",
            "status:invalidate",
            "status:completed",
            "http:authority"
        ], CollectionOrdering.Matching);
        await Assert.That(onboarding.InvalidationCount).IsEqualTo(1);
        await Assert.That(handler.Requests.Count(request => request.Path == "/api/user/sync")).IsEqualTo(1);
        await Assert.That(handler.Requests.Count(request => request.Path == "/api/user/admin-authority")).IsEqualTo(1);
        await Assert.That(principal.FindFirst("internal_user_id")?.Value).IsEqualTo(InternalUserId.ToString("D"));
        await Assert.That(principal.HasClaim("explore:admin:instance", "true")).IsTrue();
    }

    [Test]
    public async Task ConfiguredProviderMismatchFailsClosedAndClearsStaleClaimsBeforeSync()
    {
        var events = new List<string>();
        var onboarding = new ClaimCompletingOnboardingStatusProvider(events, "Atproto");
        using var handler = new IdentityReadinessHandler(events);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var client = new HttpClient(handler) { BaseAddress = new("https://api.example/") };
        var service = CreateService(client, cache, onboarding);
        var principal = CreatePrincipal(
            ("auth_provider", "keycloak"),
            ("explore:admin:instance", "true"));

        var result = await service.EnrichPrincipalAsync(
            principal,
            CreateProperties(),
            synchronizeUser: true);

        await Assert.That(result).IsFalse();
        await Assert.That(events).IsEquivalentTo(["status:pending"], CollectionOrdering.Matching);
        await Assert.That(handler.Requests).IsEmpty();
        await Assert.That(principal.HasClaim("explore:admin:instance", "true")).IsFalse();
    }

    [Test]
    [Arguments("provider")]
    [Arguments("synchronization")]
    [Arguments("completion")]
    [Arguments("authority")]
    public async Task ConfiguredPendingSigningFailureAbortsBeforeCookieCanBeIssued(string failure)
    {
        var state = CreateSigningState(failure);
        Func<Task> signIn = () => state.Handler.OnSigningInAsync(state.Context);

        await Assert.That(signIn).Throws<InvalidOperationException>();
        await Assert.That(state.Context.HttpContext.Response.Headers.SetCookie).IsEmpty();
    }

    [Test]
    public async Task ConfiguredPendingTrustedSchemePermitsExactlyOneSuccessfulSigningPath()
    {
        var state = CreateSigningState(failure: null);

        await state.Handler.OnSigningInAsync(state.Context);

        await Assert.That(state.Api.SyncCount).IsEqualTo(1);
        await Assert.That(state.Api.AuthorityCount).IsEqualTo(1);
        await Assert.That(state.Onboarding.InvalidationCount).IsEqualTo(1);
        await Assert.That(state.Context.Principal!.HasClaim("explore:admin:instance", "true")).IsTrue();
        await Assert.That(state.Context.Principal.FindAll("auth_provider").Select(claim => claim.Value))
            .IsEquivalentTo(["Keycloak"]);
    }

    private static SigningTestState CreateSigningState(string? failure)
    {
        var onboarding = new SigningOnboardingStatusProvider(
            failure == "provider" ? "Google" : "Keycloak",
            completeAfterInvalidation: failure != "completion");
        var api = new SigningApiHandler(
            synchronizationSucceeds: failure != "synchronization",
            hasAuthority: failure != "authority");
        var client = new HttpClient(api) { BaseAddress = new Uri("https://api.example/") };
        var transformation = CreateService(client, new MemoryCache(new MemoryCacheOptions()), onboarding);
        var handler = new ExploreBffCookieSessionHandler(transformation, onboarding);
        var properties = CreateProperties();
        properties.Items[EventBffAuthenticationConstants.OidcSchemePropertyKey] = "Keycloak";
        var context = new CookieSigningInContext(
            new DefaultHttpContext(),
            new AuthenticationScheme("Cookies", null, typeof(CookieAuthenticationHandler)),
            new CookieAuthenticationOptions(),
            CreatePrincipal(("auth_provider", "Google")),
            properties,
            new CookieOptions());
        return new(handler, context, onboarding, api);
    }

    private static BffAdminClaimsTransformation CreateService(
        HttpClient client,
        IMemoryCache cache,
        IBffOnboardingStatusProvider? onboardingStatusProvider = null)
    {
        if (onboardingStatusProvider is null)
        {
            onboardingStatusProvider = new FixedOnboardingStatusProvider();
        }

        return new(
            new FixedHttpClientFactory(client),
            cache,
            onboardingStatusProvider,
            NullLogger<BffAdminClaimsTransformation>.Instance);
    }

    private static BffOnboardingStatus CompletedStatus() => new(
        true,
        "Completed",
        "Interactive",
        null,
        1,
        BffOnboardingDisposition.Completed);

    private sealed class FixedOnboardingStatusProvider : IBffOnboardingStatusProvider
    {
        public Task<BffOnboardingStatus> GetStatusAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CompletedStatus());
        }

        public void Invalidate()
        {
        }
    }

    private static ClaimsPrincipal CreatePrincipal(params (string Type, string Value)[] additionalClaims) => new(
        new ClaimsIdentity(
            new[] { new Claim("sub", "provider-user") }
                .Concat(additionalClaims.Select(claim => new Claim(claim.Type, claim.Value))),
            "Cookies"));

    private static AuthenticationProperties CreateProperties()
    {
        var properties = new AuthenticationProperties();
        properties.StoreTokens(
        [
            new AuthenticationToken { Name = "access_token", Value = "access-token" }
        ]);
        return properties;
    }

    private static AdminAuthorityDto CreateAdminAuthority() => new()
    {
        IsInstanceAdmin = true,
        HasAnyAuthority = true,
        AdminTenantIds = [],
        AdminOrganizationIds = [],
        AdminGroupIds = []
    };

    private sealed class FixedHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class IdentityReadinessHandler(List<string>? events = null) : HttpMessageHandler
    {
        public HttpStatusCode SyncStatusCode { get; init; } = HttpStatusCode.OK;
        public AdminAuthorityDto Authority { get; set; } = CreateAdminAuthority();
        public List<CapturedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new(
                request.Method.Method,
                request.RequestUri!.AbsolutePath,
                request.Headers.Authorization?.ToString()));

            if (request.Method == HttpMethod.Post && request.RequestUri.AbsolutePath == "/api/user/sync")
            {
                events?.Add("http:sync");
                return Task.FromResult(JsonResponse(
                    SyncStatusCode,
                    new BaseCommandResponseOfGuid { Success = true, Id = InternalUserId }));
            }

            if (request.Method == HttpMethod.Get && request.RequestUri.AbsolutePath == "/api/user/admin-authority")
            {
                events?.Add("http:authority");
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, Authority));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        internal static HttpResponseMessage JsonResponse<T>(HttpStatusCode statusCode, T value) => new(statusCode)
        {
            Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
        };
    }

    private sealed class ClaimCompletingOnboardingStatusProvider(
        List<string> events,
        string provider) : IBffOnboardingStatusProvider
    {
        private bool _invalidated;

        public int InvalidationCount { get; private set; }

        public Task<BffOnboardingStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            events.Add(_invalidated ? "status:completed" : "status:pending");
            return Task.FromResult(_invalidated
                ? new BffOnboardingStatus(
                    true,
                    "Completed",
                    "ConfiguredAdministrator",
                    provider,
                    1,
                    BffOnboardingDisposition.Completed)
                : new BffOnboardingStatus(
                    false,
                    "Pending",
                    "ConfiguredAdministrator",
                    provider,
                    1,
                    BffOnboardingDisposition.ConfiguredAdministratorPending));
        }

        public void Invalidate()
        {
            InvalidationCount++;
            events.Add("status:invalidate");
            _invalidated = true;
        }
    }

    private sealed record SigningTestState(
        ExploreBffCookieSessionHandler Handler,
        CookieSigningInContext Context,
        SigningOnboardingStatusProvider Onboarding,
        SigningApiHandler Api);

    private sealed class SigningOnboardingStatusProvider(
        string configuredProvider,
        bool completeAfterInvalidation) : IBffOnboardingStatusProvider
    {
        private bool _invalidated;
        public int InvalidationCount { get; private set; }

        public Task<BffOnboardingStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var completed = _invalidated && completeAfterInvalidation;
            return Task.FromResult(new BffOnboardingStatus(
                completed,
                completed ? "Completed" : "Pending",
                "ConfiguredAdministrator",
                configuredProvider,
                1,
                completed
                    ? BffOnboardingDisposition.Completed
                    : BffOnboardingDisposition.ConfiguredAdministratorPending));
        }

        public void Invalidate()
        {
            InvalidationCount++;
            _invalidated = true;
        }
    }

    private sealed class SigningApiHandler(
        bool synchronizationSucceeds,
        bool hasAuthority) : HttpMessageHandler
    {
        public int SyncCount { get; private set; }
        public int AuthorityCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/api/user/sync")
            {
                SyncCount++;
                return Task.FromResult(IdentityReadinessHandler.JsonResponse(
                    synchronizationSucceeds ? HttpStatusCode.OK : HttpStatusCode.BadRequest,
                    new BaseCommandResponseOfGuid
                    {
                        Success = synchronizationSucceeds,
                        Id = synchronizationSucceeds ? InternalUserId : Guid.Empty
                    }));
            }

            AuthorityCount++;
            return Task.FromResult(IdentityReadinessHandler.JsonResponse(HttpStatusCode.OK, new AdminAuthorityDto
            {
                HasAnyAuthority = hasAuthority,
                IsInstanceAdmin = hasAuthority,
                AdminTenantIds = [],
                AdminOrganizationIds = [],
                AdminGroupIds = []
            }));
        }
    }

    private sealed record CapturedRequest(string Method, string Path, string? Authorization);
}
