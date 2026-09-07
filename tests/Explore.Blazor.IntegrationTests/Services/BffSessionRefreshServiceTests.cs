using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text.Json;
using Explore.Blazor.Authentication;
using Event.Web.BffHosting.Security;
using Explore.Blazor.Client.Configuration;
using Explore.Blazor.Services;
using Explore.Blazor.Services.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TUnit.Assertions.Enums;

namespace Explore.Blazor.IntegrationTests.Services;

public sealed class BffSessionRefreshServiceTests
{
    private static readonly Guid TenantId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000001");
    private static readonly Guid UserId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000002");

    [Test]
    public async Task RefreshSessionAsync_WithMissingCookieAuthentication_ReturnsUnauthorized()
    {
        var authService = new TestAuthenticationService(AuthenticateResult.NoResult());
        var context = CreateContext(authService: authService);
        var service = CreateService();

        var result = await service.RefreshSessionAsync(context, CancellationToken.None);

        await ExecuteAsync(result, context);
        await Assert.That(context.Response.StatusCode).IsEqualTo(StatusCodes.Status401Unauthorized);
    }

    [Test]
    public async Task RefreshSessionAsync_WithMissingAccessToken_ReturnsConflictAndClearsCircuitState()
    {
        var principal = CreatePrincipal("user-1", "session-1");
        var authService = new TestAuthenticationService(AuthenticateResult.Success(CreateTicket(principal, accessToken: null)));
        var state = CreateRealCircuitState(authService, principal, "user-1", "session-1");
        var service = CreateService();

        var result = await service.RefreshSessionAsync(state.Context, CancellationToken.None);

        await ExecuteAsync(result, state.Context);

        await Assert.That(state.Context.Response.StatusCode).IsEqualTo(StatusCodes.Status409Conflict);
        await Assert.That(state.TokenService.AccessToken).IsNull();
        await Assert.That(state.UserContext.UserId).IsNull();
        await Assert.That(state.UserContext.SessionId).IsNull();
        await Assert.That(state.CookieStore.CookieHeader).IsNull();
        await Assert.That(state.TokenStore.Resolve("user-1", "session-1").Found).IsFalse();
    }

    [Test]
    public async Task RefreshSessionAsync_WithValidAccessToken_ReturnsTokenStatusAndNeverRawToken()
    {
        var accessToken = CreateJwt("user-1", DateTime.UtcNow.AddMinutes(30), "session-1");
        var principal = CreatePrincipal("user-1", "session-1");
        var authService = new TestAuthenticationService(AuthenticateResult.Success(CreateTicket(principal, accessToken)));
        var tokenStore = Substitute.For<ICircuitTokenStore>();
        var (subjectKey, sessionKey) = PartitionKeys(principal);
        tokenStore.Store(subjectKey, sessionKey, accessToken)
            .Returns(new CircuitTokenStoreResult(true, null));
        var onboardingStatusProvider = Substitute.For<IBffOnboardingStatusProvider>();
        onboardingStatusProvider.GetStatusAsync(Arg.Any<CancellationToken>())
            .Returns(CompletedStatus());
        var context = CreateContext(
            authService: authService,
            tokenStore: tokenStore,
            onboardingStatusProvider: onboardingStatusProvider);
        var service = CreateService(onboardingStatusProvider);

        var result = await service.RefreshSessionAsync(context, CancellationToken.None);

        await ExecuteAsync(result, context);
        await Assert.That(context.Response.StatusCode).IsEqualTo(StatusCodes.Status200OK);
        tokenStore.Received(1).Store(subjectKey, sessionKey, accessToken);
        await Assert.That(authService.SignInCalled).IsTrue();

        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        var root = document.RootElement;
        await Assert.That(root.GetProperty("refreshed").GetBoolean()).IsTrue();
        await Assert.That(root.GetProperty("adminClaimsUpdated").GetBoolean()).IsFalse();
        await Assert.That(root.TryGetProperty("tokenStatus", out var tokenStatus)).IsTrue();
        await Assert.That(tokenStatus.GetString()).IsEqualTo("valid_access_token");
        await Assert.That(root.TryGetProperty("token", out _)).IsFalse();
        await Assert.That(root.GetRawText()).DoesNotContain(accessToken);
    }

    [Test]
    public async Task RefreshSessionAsync_WhenTokenSubjectDiffersFromCookiePrincipal_StoresTokenForCookiePrincipal()
    {
        const string principalUserId = "principal-user";
        const string principalSessionId = "principal-session";
        var accessToken = CreateJwt("token-subject", DateTime.UtcNow.AddMinutes(30), "token-session");
        var principal = CreatePrincipal(principalUserId, principalSessionId);
        var authService = new TestAuthenticationService(AuthenticateResult.Success(CreateTicket(principal, accessToken)));
        var tokenStore = new CircuitTokenStore(NullLogger<CircuitTokenStore>.Instance);
        var tokenService = new CircuitAccessTokenService(
            tokenStore,
            new HttpContextAccessor(),
            NullLogger<CircuitAccessTokenService>.Instance);
        var context = CreateContext(authService, tokenService, tokenStore: tokenStore);
        var service = CreateService();

        var result = await service.RefreshSessionAsync(context, CancellationToken.None);

        await ExecuteAsync(result, context);
        var (subjectKey, sessionKey) = PartitionKeys(principal);
        var resolution = tokenStore.Resolve(subjectKey, sessionKey);
        await Assert.That(context.Response.StatusCode).IsEqualTo(StatusCodes.Status200OK);
        await Assert.That(resolution.Found).IsTrue();
        await Assert.That(resolution.Token).IsEqualTo(accessToken);
    }

    [Test]
    public async Task RefreshSessionAsync_WhenCircuitStoreRejectsToken_ReturnsConflictWithoutSigningIn()
    {
        var accessToken = CreateJwt("user-1", DateTime.UtcNow.AddMinutes(30), "session-1");
        var principal = CreatePrincipal("user-1", "session-1");
        var authService = new TestAuthenticationService(AuthenticateResult.Success(CreateTicket(principal, accessToken)));
        var tokenStore = Substitute.For<ICircuitTokenStore>();
        var (subjectKey, sessionKey) = PartitionKeys(principal);
        tokenStore.Store(subjectKey, sessionKey, accessToken)
            .Returns(new CircuitTokenStoreResult(false, "token_rejected"));
        var context = CreateContext(authService: authService, tokenStore: tokenStore);
        var service = CreateService();

        var result = await service.RefreshSessionAsync(context, CancellationToken.None);

        await ExecuteAsync(result, context);
        await Assert.That(context.Response.StatusCode).IsEqualTo(StatusCodes.Status409Conflict);
        await Assert.That(authService.SignInCalled).IsFalse();
        context.Response.Body.Position = 0;
        var responseBody = await new StreamReader(context.Response.Body).ReadToEndAsync();
        await Assert.That(responseBody).Contains("token_handoff_failed");
        await Assert.That(responseBody).DoesNotContain(accessToken);
    }

    [Test]
    public async Task RefreshSessionAsync_WithAtprotoCookie_UsesPrivateBridgeAndStoresOnlyReplacementToken()
    {
        var principal = CreateAtprotoPrincipal();
        var authService = new TestAuthenticationService(AuthenticateResult.Success(CreateTicket(principal, "old-platform-token")));
        var tokenService = Substitute.For<ICircuitAccessTokenService>();
        var handler = new AtprotoBridgeHandler(HttpStatusCode.OK);
        var context = CreateContext(authService: authService, tokenService: tokenService);
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("events.example.com");
        var service = CreateService(bridgeHandler: handler);

        var result = await service.RefreshSessionAsync(context, CancellationToken.None);

        await ExecuteAsync(result, context);
        await Assert.That(context.Response.StatusCode).IsEqualTo(StatusCodes.Status200OK);
        await Assert.That(handler.Method).IsEqualTo(HttpMethod.Post);
        await Assert.That(handler.Authorization).IsEqualTo("Bearer old-platform-token");
        await Assert.That(handler.TenantSlug).IsEqualTo("default");
        await Assert.That(handler.PrivateAssertion).IsNotNull();
        tokenService.Received(1).SetToken("new-platform-token");
        await Assert.That(authService.SignInProperties!.GetTokenValue("access_token"))
            .IsEqualTo("new-platform-token");

        context.Response.Body.Position = 0;
        var responseBody = await new StreamReader(context.Response.Body).ReadToEndAsync();
        await Assert.That(responseBody).DoesNotContain("old-platform-token");
        await Assert.That(responseBody).DoesNotContain("new-platform-token");
    }

    [Test]
    public async Task RefreshSessionAsync_WithRejectedAtprotoSession_ClearsCookieAndRequiresReauthentication()
    {
        var principal = CreateAtprotoPrincipal();
        var authService = new TestAuthenticationService(AuthenticateResult.Success(
            CreateTicket(principal, Guid.CreateVersion7().ToString("N"))));
        var state = CreateRealCircuitState(authService, principal,
            principal.FindFirstValue("sub")!, principal.FindFirstValue("sid")!);
        state.Context.Request.Scheme = "https";
        state.Context.Request.Host = new HostString("events.example.com");
        var service = CreateService(bridgeHandler: new AtprotoBridgeHandler(HttpStatusCode.Unauthorized));

        var result = await service.RefreshSessionAsync(state.Context, CancellationToken.None);

        await ExecuteAsync(result, state.Context);
        await Assert.That(state.Context.Response.StatusCode).IsEqualTo(StatusCodes.Status401Unauthorized);
        await Assert.That(authService.SignOutCalled).IsTrue();
        await AssertCircuitStateClearedAsync(state);
    }

    [Test]
    public async Task RefreshSessionAsyncWithConflictingAtprotoSubjectsFailsClosedAndClearsSession()
    {
        var accessToken = Guid.CreateVersion7().ToString("N");
        var principal = CreateAtprotoPrincipal();
        ((ClaimsIdentity)principal.Identity!).AddClaim(
            new Claim("sub", Guid.Parse("0190f50d-1690-7000-8000-000000000099").ToString("D")));
        var authService = new TestAuthenticationService(
            AuthenticateResult.Success(CreateTicket(principal, accessToken)));
        var subject = principal.FindFirstValue("sub")!;
        var session = principal.FindFirstValue("sid")!;
        var state = CreateRealCircuitState(authService, principal, subject, session);
        state.Context.Request.Scheme = "https";
        state.Context.Request.Host = new HostString("events.example.com");
        using var bridgeHandler = new AtprotoBridgeHandler(HttpStatusCode.OK);
        var service = CreateService(bridgeHandler: bridgeHandler);

        var result = await service.RefreshSessionAsync(state.Context, CancellationToken.None);

        await ExecuteAsync(result, state.Context);
        await Assert.That(state.Context.Response.StatusCode).IsEqualTo(StatusCodes.Status401Unauthorized);
        await Assert.That(authService.SignOutCalled).IsTrue();
        await AssertCircuitStateClearedAsync(state);
    }

    [Test]
    public async Task RefreshSessionAsyncWithMissingAtprotoSubjectFailsClosedAndClearsSession()
    {
        var accessToken = Guid.CreateVersion7().ToString("N");
        var principal = CreateAtprotoPrincipal();
        var identity = (ClaimsIdentity)principal.Identity!;
        identity.RemoveClaim(identity.FindFirst("sub")!);
        identity.RemoveClaim(identity.FindFirst("sid")!);
        identity.AddClaim(new Claim("sid", UserId.ToString("D")));
        var authService = new TestAuthenticationService(
            AuthenticateResult.Success(CreateTicket(principal, accessToken)));
        var subject = UserId.ToString("D");
        var session = principal.FindFirstValue("sid")!;
        var state = CreateRealCircuitState(authService, principal, subject, session);
        state.Context.Request.Scheme = "https";
        state.Context.Request.Host = new HostString("events.example.com");
        using var bridgeHandler = new AtprotoBridgeHandler(HttpStatusCode.OK);
        var service = CreateService(bridgeHandler: bridgeHandler);

        var result = await service.RefreshSessionAsync(state.Context, CancellationToken.None);

        await ExecuteAsync(result, state.Context);
        await Assert.That(state.Context.Response.StatusCode).IsEqualTo(StatusCodes.Status401Unauthorized);
        await Assert.That(bridgeHandler.Method).IsNull();
        await Assert.That(authService.SignOutCalled).IsTrue();
        await AssertCircuitStateClearedAsync(state);
    }

    [Test]
    public async Task RevokeAtprotoSessionAsync_WithRemoteOutage_RemainsBestEffortAndUsesPrivateDelete()
    {
        var principal = CreateAtprotoPrincipal();
        var authentication = AuthenticateResult.Success(CreateTicket(principal, "old-platform-token"));
        var handler = new AtprotoBridgeHandler(HttpStatusCode.ServiceUnavailable);
        var context = CreateContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("events.example.com");
        var service = CreateService(bridgeHandler: handler);

        await service.RevokeAtprotoSessionAsync(context, authentication, CancellationToken.None);

        await Assert.That(handler.Method).IsEqualTo(HttpMethod.Delete);
        await Assert.That(handler.Authorization).IsEqualTo("Bearer old-platform-token");
        await Assert.That(handler.PrivateAssertion).IsNotNull();
    }

    [Test]
    public async Task ConfiguredAdministratorRefreshSyncsThenRefreshesStatusAndCookieExactlyOnce()
    {
        var events = new List<string>();
        var onboarding = new ClaimCompletingOnboardingStatusProvider(events, "Keycloak");
        using var adminHandler = new AdminClaimHandler(events);
        var accessToken = CreateJwt("provider-user", DateTime.UtcNow.AddMinutes(30), "session-1");
        var principal = CreatePrincipal("provider-user", "session-1");
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim("auth_provider", "keycloak"));
        var authService = new TestAuthenticationService(
            AuthenticateResult.Success(CreateTicket(principal, accessToken)));
        var tokenStore = Substitute.For<ICircuitTokenStore>();
        var (subjectKey, sessionKey) = PartitionKeys(principal);
        tokenStore.Store(subjectKey, sessionKey, accessToken)
            .Returns(new CircuitTokenStoreResult(true, null));
        var context = CreateContext(
            authService: authService,
            tokenStore: tokenStore,
            onboardingStatusProvider: onboarding);
        var service = CreateService(onboarding, adminHandler: adminHandler);

        var result = await service.RefreshSessionAsync(context, CancellationToken.None);

        await ExecuteAsync(result, context);
        await Assert.That(context.Response.StatusCode).IsEqualTo(StatusCodes.Status200OK);
        await Assert.That(events).IsEquivalentTo([
            "status:pending",
            "status:pending",
            "http:sync",
            "status:invalidate",
            "status:completed",
            "http:authority",
            "status:completed"
        ], CollectionOrdering.Matching);
        await Assert.That(adminHandler.SyncCount).IsEqualTo(1);
        await Assert.That(adminHandler.AuthorityCount).IsEqualTo(1);
        await Assert.That(onboarding.InvalidationCount).IsEqualTo(1);
        await Assert.That(authService.SignInCount).IsEqualTo(1);
        await Assert.That(authService.SignOutCount).IsEqualTo(0);
        tokenStore.Received(1).Store(subjectKey, sessionKey, accessToken);
        await Assert.That(principal.HasClaim("explore:admin:instance", "true")).IsTrue();

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        await Assert.That(body).DoesNotContain(accessToken);
    }

    [Test]
    public async Task ConfiguredAdministratorWrongProviderClearsStaleClaimsAndSessionWithoutRefresh()
    {
        var events = new List<string>();
        var onboarding = new ClaimCompletingOnboardingStatusProvider(events, "Atproto");
        using var adminHandler = new AdminClaimHandler(events);
        var accessToken = CreateJwt("provider-user", DateTime.UtcNow.AddMinutes(30), "session-1");
        var principal = CreatePrincipal("provider-user", "session-1");
        var identity = (ClaimsIdentity)principal.Identity!;
        identity.AddClaim(new Claim("auth_provider", "keycloak"));
        identity.AddClaim(new Claim("explore:admin:instance", "true"));
        var authService = new TestAuthenticationService(
            AuthenticateResult.Success(CreateTicket(principal, accessToken)));
        var tokenStore = Substitute.For<ICircuitTokenStore>();
        var context = CreateContext(
            authService: authService,
            tokenStore: tokenStore,
            onboardingStatusProvider: onboarding);
        var service = CreateService(onboarding, adminHandler: adminHandler);

        var result = await service.RefreshSessionAsync(context, CancellationToken.None);

        await ExecuteAsync(result, context);
        await Assert.That(context.Response.StatusCode).IsEqualTo(StatusCodes.Status401Unauthorized);
        await Assert.That(adminHandler.SyncCount).IsEqualTo(0);
        await Assert.That(adminHandler.AuthorityCount).IsEqualTo(0);
        await Assert.That(authService.SignInCount).IsEqualTo(0);
        await Assert.That(authService.SignOutCount).IsEqualTo(1);
        await Assert.That(principal.HasClaim("explore:admin:instance", "true")).IsFalse();
        tokenStore.DidNotReceive().Store(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string>());
    }

    private static BffSessionRefreshService CreateService(
        IBffOnboardingStatusProvider? onboardingStatusProvider = null,
        HttpMessageHandler? bridgeHandler = null,
        HttpMessageHandler? adminHandler = null)
    {
        if (onboardingStatusProvider is null)
        {
            onboardingStatusProvider = new FixedOnboardingStatusProvider();
        }

        IHttpClientFactory adminClientFactory = Substitute.For<IHttpClientFactory>();
        if (adminHandler is not null)
        {
            adminClientFactory = new FixedHttpClientFactory(
                new HttpClient(adminHandler) { BaseAddress = new Uri("https://api.example/") });
        }

        var adminClaimsTransformation = new BffAdminClaimsTransformation(
            adminClientFactory,
            new MemoryCache(new MemoryCacheOptions()),
            onboardingStatusProvider,
            NullLogger<BffAdminClaimsTransformation>.Instance);

        bridgeHandler ??= new AtprotoBridgeHandler(HttpStatusCode.ServiceUnavailable);
        var bridgeClient = new HttpClient(bridgeHandler) { BaseAddress = new("https://api.example/") };
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Production);
        return new BffSessionRefreshService(
            adminClaimsTransformation,
            new BffAccessTokenAssessmentService(),
            new FixedHttpClientFactory(bridgeClient),
            new AtprotoBootstrapAssertionService(CreateKeyProvider(), TimeProvider.System),
            new AtprotoTenantOriginResolver(
                Options.Create(new AtprotoAuthenticationOptions { PublicUrl = "https://events.example.com/" }),
                Options.Create(new TenantConfiguration { DefaultTenantId = TenantId, DefaultTenant = "default" }),
                environment),
            new AtprotoAuthenticationMetrics());
    }

    private static RealCircuitState CreateRealCircuitState(
        TestAuthenticationService authService,
        ClaimsPrincipal principal,
        string userId,
        string sessionId)
    {
        var tokenStore = new CircuitTokenStore(NullLogger<CircuitTokenStore>.Instance);
        var accessor = new HttpContextAccessor();
        var tokenService = new CircuitAccessTokenService(
            tokenStore, accessor, NullLogger<CircuitAccessTokenService>.Instance);
        var userContext = new CircuitUserContext();
        var (subjectKey, sessionKey) = PartitionKeys(principal);
        userContext.SetUserId(subjectKey);
        userContext.SetSessionId(sessionKey);
        var cookieStore = new BffAuthCookieStore();
        cookieStore.SetCookieHeader($"bff={Guid.CreateVersion7():N}");
        var context = CreateContext(
            authService, tokenService, userContext, cookieStore, tokenStore);
        context.User = principal;
        accessor.HttpContext = context;
        tokenService.SetToken(CreateJwt(
            userId, DateTime.UtcNow.AddMinutes(30), sessionId));
        return new RealCircuitState(
            context, tokenService, userContext, cookieStore, tokenStore, subjectKey, sessionKey);
    }

    private static async Task AssertCircuitStateClearedAsync(RealCircuitState state)
    {
        await Assert.That(state.TokenService.AccessToken).IsNull();
        await Assert.That(state.UserContext.UserId).IsNull();
        await Assert.That(state.UserContext.SessionId).IsNull();
        await Assert.That(state.CookieStore.CookieHeader).IsNull();
        await Assert.That(state.TokenStore.Resolve(state.UserId, state.SessionId).Found).IsFalse();
    }

    private sealed record RealCircuitState(
        DefaultHttpContext Context,
        CircuitAccessTokenService TokenService,
        CircuitUserContext UserContext,
        BffAuthCookieStore CookieStore,
        CircuitTokenStore TokenStore,
        string UserId,
        string SessionId);

    private static DefaultHttpContext CreateContext(
        TestAuthenticationService? authService = null,
        ICircuitAccessTokenService? tokenService = null,
        ICircuitUserContext? userContext = null,
        IBffAuthCookieStore? cookieStore = null,
        ICircuitTokenStore? tokenStore = null,
        IBffOnboardingStatusProvider? onboardingStatusProvider = null)
    {
        authService ??= new TestAuthenticationService(AuthenticateResult.NoResult());
        tokenService ??= Substitute.For<ICircuitAccessTokenService>();
        userContext ??= Substitute.For<ICircuitUserContext>();
        cookieStore ??= Substitute.For<IBffAuthCookieStore>();
        if (tokenStore is null)
        {
            tokenStore = Substitute.For<ICircuitTokenStore>();
            tokenStore.Store(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string>())
                .Returns(new CircuitTokenStoreResult(true, null));
        }
        onboardingStatusProvider ??= new FixedOnboardingStatusProvider();

        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IAuthenticationService>(authService)
            .AddSingleton(tokenService)
            .AddSingleton(userContext)
            .AddSingleton(cookieStore)
            .AddSingleton(tokenStore)
            .AddSingleton(onboardingStatusProvider)
            .BuildServiceProvider();

        return new DefaultHttpContext { RequestServices = services, Response = { Body = new MemoryStream() } };
    }

    private static async Task ExecuteAsync(IResult result, HttpContext context)
    {
        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
    }

    private static AuthenticationTicket CreateTicket(ClaimsPrincipal principal, string? accessToken)
    {
        var properties = new AuthenticationProperties();
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            properties.StoreTokens([new AuthenticationToken { Name = "access_token", Value = accessToken }]);
        }

        return new AuthenticationTicket(principal, properties, "Cookies");
    }

    private static (string SubjectKey, string SessionKey) PartitionKeys(ClaimsPrincipal principal)
    {
        principal.TryGetCircuitSubject(out var subject);
        principal.TryGetSessionId(out var session);
        return (subject.PartitionKey, session.PartitionKey);
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

    private static ClaimsPrincipal CreatePrincipal(string userId, string sessionId) => new(new ClaimsIdentity([
        new Claim("sub", userId),
        new Claim("sid", sessionId)
    ], "Cookies"));

    private static ClaimsPrincipal CreateAtprotoPrincipal() => new(new ClaimsIdentity([
        new Claim("sub", UserId.ToString("D")),
        new Claim("sid", Guid.CreateVersion7().ToString("D")),
        new Claim("did", "did:plc:alice"),
        new Claim("tenant_id", TenantId.ToString("D")),
        new Claim("auth_provider", "atproto")
    ], "Atproto"));

    private static AtprotoClientKeyProvider CreateKeyProvider()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(true);
        var ring = JsonSerializer.Serialize(new
        {
            keys = new[]
            {
                new
                {
                    kty = "EC",
                    crv = "P-256",
                    x = Encode(parameters.Q.X!),
                    y = Encode(parameters.Q.Y!),
                    d = Encode(parameters.D!),
                    kid = "oauth-active",
                    use = "sig",
                    alg = "ES256",
                    status = "active"
                }
            }
        });
        return new(Options.Create(new AtprotoClientKeyOptions { OAuthClientPrivateJwks = ring }));
    }

    private static string Encode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string CreateJwt(string sub, DateTime expires, string sessionId)
    {
        var jwt = new JwtSecurityToken(
            claims: [new Claim("sub", sub), new Claim("sid", sessionId)],
            expires: expires);
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private sealed class TestAuthenticationService(AuthenticateResult authenticateResult) : IAuthenticationService
    {
        public bool SignInCalled => SignInCount > 0;
        public bool SignOutCalled => SignOutCount > 0;
        public int SignInCount { get; private set; }
        public int SignOutCount { get; private set; }
        public AuthenticationProperties? SignInProperties { get; private set; }

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) =>
            Task.FromResult(authenticateResult);

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            Task.CompletedTask;

        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            Task.CompletedTask;

        public Task SignInAsync(
            HttpContext context,
            string? scheme,
            ClaimsPrincipal principal,
            AuthenticationProperties? properties)
        {
            SignInCount++;
            SignInProperties = properties;
            return Task.CompletedTask;
        }

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        {
            SignOutCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
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

    private sealed class AdminClaimHandler(List<string> events) : HttpMessageHandler
    {
        public int SyncCount { get; private set; }
        public int AuthorityCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post
                && request.RequestUri!.AbsolutePath == "/api/user/sync")
            {
                SyncCount++;
                events.Add("http:sync");
                return Task.FromResult(Json(new { success = true, id = UserId }));
            }

            if (request.Method == HttpMethod.Get
                && request.RequestUri!.AbsolutePath == "/api/user/admin-authority")
            {
                AuthorityCount++;
                events.Add("http:authority");
                return Task.FromResult(Json(new
                {
                    isInstanceAdmin = true,
                    hasAnyAuthority = true,
                    adminTenantIds = Array.Empty<Guid>(),
                    adminOrganizationIds = Array.Empty<Guid>(),
                    adminGroupIds = Array.Empty<Guid>()
                }));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(value)
        };
    }

    private sealed class AtprotoBridgeHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public string? Authorization { get; private set; }
        public string? TenantSlug { get; private set; }
        public string? PrivateAssertion { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            Authorization = request.Headers.Authorization?.ToString();
            TenantSlug = request.Headers.GetValues("X-Tenant-Slug").Single();
            PrivateAssertion = request.Headers
                .GetValues(AtprotoBootstrapAssertionService.SessionBridgeHeaderName)
                .Single();
            var response = new HttpResponseMessage(statusCode);
            if (statusCode == HttpStatusCode.OK)
            {
                response.Content = JsonContent.Create(new
                {
                    userId = UserId,
                    did = "did:plc:alice",
                    accessToken = "new-platform-token",
                    expiresAt = DateTimeOffset.UtcNow.AddMinutes(10)
                });
            }

            return Task.FromResult(response);
        }
    }
}
