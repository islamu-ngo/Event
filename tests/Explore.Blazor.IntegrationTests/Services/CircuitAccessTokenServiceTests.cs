// ABOUTME: Tests for CircuitAccessTokenService and AccessTokenForwardingHandler using the bounded ICircuitTokenStore.
// ABOUTME: Verifies cross-user isolation, session scoping, token refresh propagation, and deterministic cleanup.

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using Explore.Blazor.Services;
using Event.Web.BffHosting.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Options = Microsoft.Extensions.Options.Options;

namespace Explore.Blazor.IntegrationTests.Services;

public class CircuitAccessTokenServiceTests
{
    private readonly ICircuitTokenStore _tokenStore = new CircuitTokenStore(
        NullLogger<CircuitTokenStore>.Instance);

    [Test]
    public async Task RevokeSessionPermanentlyRejectsStaleCircuitFallbackWhileFreshSessionRemainsUsable()
    {
        string userId = Guid.NewGuid().ToString("D");
        string sessionA = Guid.NewGuid().ToString("D");
        string sessionB = Guid.NewGuid().ToString("D");
        string tokenA = CreateJwt(userId, sessionId: sessionA);
        string tokenB = CreateJwt(userId, sessionId: sessionB);
        var contextA = CreateHttpContextWithSession(userId, sessionA);
        var contextB = CreateHttpContextWithSession(userId, sessionB);
        var accessor = new HttpContextAccessor { HttpContext = contextA };
        ICircuitAccessTokenService serviceA = new CircuitAccessTokenService(
            _tokenStore, accessor, NullLogger<CircuitAccessTokenService>.Instance);
        serviceA.SetToken(tokenA);
        accessor.HttpContext = contextB;
        ICircuitAccessTokenService serviceB = new CircuitAccessTokenService(
            _tokenStore, accessor, NullLogger<CircuitAccessTokenService>.Instance);
        serviceB.SetToken(tokenB);
        accessor.HttpContext = contextA;
        await Assert.That(contextA.User.TryGetCircuitSubject(out var subject)).IsTrue();
        await Assert.That(contextA.User.TryGetSessionId(out var session)).IsTrue();

        accessor.HttpContext = contextB;
        serviceA.RevokeSession(originalSubject: subject, originalSession: session);
        accessor.HttpContext = contextA;

        await Assert.That(_tokenStore.Resolve(subject.PartitionKey, session.PartitionKey).Found).IsFalse();
        await Assert.That(_tokenStore.ResolveByUserId(subject.PartitionKey).Token == tokenB).IsTrue();
        await Assert.That(serviceA.AccessToken is null).IsTrue();

        await Assert.That(contextB.User.TryGetCircuitSubject(out var subjectB)).IsTrue();
        await Assert.That(contextB.User.TryGetSessionId(out var resolvedSessionB)).IsTrue();
        serviceA.RevokeSession(originalSubject: subjectB, originalSession: resolvedSessionB);
        await Assert.That(_tokenStore.Resolve(subjectB.PartitionKey, resolvedSessionB.PartitionKey).Token == tokenB).IsTrue();

        serviceA.SetToken(tokenA);
        await Assert.That(serviceA.AccessToken is null).IsTrue();
        await Assert.That(_tokenStore.Resolve(subject.PartitionKey, session.PartitionKey).Found).IsFalse();
        serviceA.ClearToken();
        serviceA.SetToken(tokenB);

        await Assert.That(serviceA.AccessToken is null).IsTrue();
        await Assert.That(_tokenStore.Resolve(subject.PartitionKey, session.PartitionKey).Found).IsFalse();
        await Assert.That(_tokenStore.Resolve(CircuitKey(contextB), SessionKey(contextB)).Token == tokenB).IsTrue();
        accessor.HttpContext = contextB;
        ICircuitAccessTokenService freshScopeB = new CircuitAccessTokenService(
            _tokenStore, accessor, NullLogger<CircuitAccessTokenService>.Instance);
        await Assert.That(freshScopeB.AccessToken == tokenB).IsTrue();
        await Assert.That(serviceB.AccessToken == tokenB).IsTrue();
    }

    [Test]
    [Arguments(true, true)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    public async Task RevocationWithoutCompleteOriginalIdentityCannotClearAnotherSession(
        bool missingSubject, bool missingSession)
    {
        string userId = Guid.NewGuid().ToString("D");
        string sessionA = Guid.NewGuid().ToString("D");
        string sessionB = Guid.NewGuid().ToString("D");
        string tokenA = CreateJwt(userId, sessionId: sessionA);
        string tokenB = CreateJwt(userId, sessionId: sessionB);
        var contextA = CreateHttpContextWithSession(userId, sessionA);
        var contextB = CreateHttpContextWithSession(userId, sessionB);
        var accessor = new HttpContextAccessor { HttpContext = contextA };
        ICircuitAccessTokenService revokedScope = new CircuitAccessTokenService(
            _tokenStore, accessor, NullLogger<CircuitAccessTokenService>.Instance);
        revokedScope.SetToken(tokenA);
        await Assert.That(_tokenStore.Store(CircuitKey(contextB), SessionKey(contextB), tokenB).Accepted).IsTrue();
        await Assert.That(contextA.User.TryGetCircuitSubject(out var subject)).IsTrue();
        await Assert.That(contextA.User.TryGetSessionId(out var session)).IsTrue();
        accessor.HttpContext = contextB;

        revokedScope.RevokeSession(
            originalSubject: missingSubject ? null : subject,
            originalSession: missingSession ? null : session);
        revokedScope.ClearToken();
        revokedScope.SetToken(tokenB);

        await Assert.That(revokedScope.AccessToken is null).IsTrue();
        await Assert.That(_tokenStore.Resolve(CircuitKey(contextA), SessionKey(contextA)).Token == tokenA).IsTrue();
        await Assert.That(_tokenStore.Resolve(CircuitKey(contextB), SessionKey(contextB)).Token == tokenB).IsTrue();
        ICircuitAccessTokenService freshScopeB = new CircuitAccessTokenService(
            _tokenStore, accessor, NullLogger<CircuitAccessTokenService>.Instance);
        await Assert.That(freshScopeB.AccessToken == tokenB).IsTrue();
    }

    [Test]
    public async Task SetupSecretSessionService_UserActivityExtendsIdleExpiration()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-07-27T00:00:00Z"));
        var service = new SetupSecretSessionService(timeProvider);
        service.SetForUser("user-1", "setup-secret");

        timeProvider.Advance(TimeSpan.FromMinutes(29));
        await Assert.That(service.GetForUser("user-1")).IsEqualTo("setup-secret");
        timeProvider.Advance(TimeSpan.FromMinutes(29));
        await Assert.That(service.GetForUser("user-1")).IsEqualTo("setup-secret");
        timeProvider.Advance(TimeSpan.FromMinutes(30) + TimeSpan.FromTicks(1));

        await Assert.That(service.GetForUser("user-1")).IsNull();
    }

    [Test]
    public async Task SetupSecretSessionService_AnonymousSessionExpiresAfterIdleTimeout()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-07-27T00:00:00Z"));
        var service = new SetupSecretSessionService(timeProvider);
        var sessionId = service.CreateAnonymousSession("setup-secret");

        timeProvider.Advance(TimeSpan.FromMinutes(30) + TimeSpan.FromTicks(1));

        await Assert.That(service.GetForAnonymousSession(sessionId)).IsNull();
    }

    [Test]
    public async Task AccessTokenForwardingHandler_UsesHttpContextToken_WhenAvailable()
    {
        var userId = Guid.NewGuid().ToString();
        var storeToken = CreateJwt(userId);
        var contextToken = CreateJwt(userId);

        var storeContext = CreateHttpContext(userId);
        var storeService = new CircuitAccessTokenService(
            _tokenStore,
            new HttpContextAccessor { HttpContext = storeContext },
            NullLogger<CircuitAccessTokenService>.Instance);
        storeService.SetToken(storeToken);

        var requestContext = CreateHttpContext(userId, contextToken);
        var accessor = new HttpContextAccessor { HttpContext = requestContext };
        var handler = new TestableAccessTokenForwardingHandler(
            accessor,
            Substitute.For<ICircuitAccessTokenService>(),
            Substitute.For<ICircuitUserContext>(),
            _tokenStore,
            NullLogger<AccessTokenForwardingHandler>.Instance);
        var terminal = new CaptureHandler();
        handler.InnerHandler = terminal;

        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/api/protected");
        var response = await handler.InvokeAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(terminal.Request?.Headers.Authorization?.Parameter).IsEqualTo(contextToken);
    }

    [Test]
    public async Task AccessTokenForwardingHandler_UsesUserStoreToken_WhenHttpContextTokenMissing()
    {
        var userId = Guid.NewGuid().ToString();
        var storeToken = CreateJwt(userId);

        var storeContext = CreateHttpContext(userId);
        var storeService = new CircuitAccessTokenService(
            _tokenStore,
            new HttpContextAccessor { HttpContext = storeContext },
            NullLogger<CircuitAccessTokenService>.Instance);
        storeService.SetToken(storeToken);

        var requestContext = CreateHttpContext(userId);
        var accessor = new HttpContextAccessor { HttpContext = requestContext };
        var handler = new TestableAccessTokenForwardingHandler(
            accessor,
            Substitute.For<ICircuitAccessTokenService>(),
            Substitute.For<ICircuitUserContext>(),
            _tokenStore,
            NullLogger<AccessTokenForwardingHandler>.Instance);
        var terminal = new CaptureHandler();
        handler.InnerHandler = terminal;

        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/api/protected");
        var response = await handler.InvokeAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(terminal.Request?.Headers.Authorization?.Parameter).IsEqualTo(storeToken);
    }

    [Test]
    public async Task AccessTokenForwardingHandler_DoesNotUseOtherUsersToken()
    {
        var ownerUserId = Guid.NewGuid().ToString();
        var requesterUserId = Guid.NewGuid().ToString();
        var ownerToken = CreateJwt(ownerUserId);

        var storeContext = CreateHttpContext(ownerUserId);
        var storeService = new CircuitAccessTokenService(
            _tokenStore,
            new HttpContextAccessor { HttpContext = storeContext },
            NullLogger<CircuitAccessTokenService>.Instance);
        storeService.SetToken(ownerToken);

        var requestContext = CreateHttpContext(requesterUserId);
        var accessor = new HttpContextAccessor { HttpContext = requestContext };
        var handler = new TestableAccessTokenForwardingHandler(
            accessor,
            Substitute.For<ICircuitAccessTokenService>(),
            Substitute.For<ICircuitUserContext>(),
            _tokenStore,
            NullLogger<AccessTokenForwardingHandler>.Instance);
        var terminal = new CaptureHandler();
        handler.InnerHandler = terminal;

        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/api/protected");
        var response = await handler.InvokeAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(terminal.Request?.Headers.Authorization).IsNull();
    }

    [Test]
    public async Task AccessTokenForwardingHandler_UsesCircuitUserContext_WhenHttpContextIsNull()
    {
        var userId = Guid.NewGuid().ToString();
        var storeToken = CreateJwt(userId);

        var storeContext = CreateHttpContext(userId);
        var storeService = new CircuitAccessTokenService(
            _tokenStore,
            new HttpContextAccessor { HttpContext = storeContext },
            NullLogger<CircuitAccessTokenService>.Instance);
        storeService.SetToken(storeToken);

        var accessor = new HttpContextAccessor { HttpContext = null };
        var circuitUserContext = Substitute.For<ICircuitUserContext>();
        circuitUserContext.UserId.Returns(CircuitKey(storeContext));

        var handler = new TestableAccessTokenForwardingHandler(
            accessor,
            Substitute.For<ICircuitAccessTokenService>(),
            circuitUserContext,
            _tokenStore,
            NullLogger<AccessTokenForwardingHandler>.Instance);
        var terminal = new CaptureHandler();
        handler.InnerHandler = terminal;

        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/api/protected");
        var response = await handler.InvokeAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(terminal.Request?.Headers.Authorization?.Parameter).IsEqualTo(storeToken);
    }

    [Test]
    public async Task AccessTokenForwardingHandler_WithHttpContextUserSession_FallsBackToUserStoredToken()
    {
        var userId = Guid.NewGuid().ToString();
        var sessionA = Guid.NewGuid().ToString();
        var sessionB = Guid.NewGuid().ToString();
        var tokenA = CreateJwt(userId, sessionId: sessionA);

        var storeService = new CircuitAccessTokenService(
            _tokenStore,
            new HttpContextAccessor { HttpContext = CreateHttpContextWithSession(userId, sessionA) },
            NullLogger<CircuitAccessTokenService>.Instance);
        storeService.SetToken(tokenA);

        var accessor = new HttpContextAccessor { HttpContext = CreateHttpContextWithSession(userId, sessionB) };
        var handler = new TestableAccessTokenForwardingHandler(
            accessor,
            Substitute.For<ICircuitAccessTokenService>(),
            Substitute.For<ICircuitUserContext>(),
            _tokenStore,
            NullLogger<AccessTokenForwardingHandler>.Instance);
        var terminal = new CaptureHandler();
        handler.InnerHandler = terminal;

        var response = await handler.InvokeAsync(new HttpRequestMessage(HttpMethod.Get, "https://localhost/api/protected"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(terminal.Request?.Headers.Authorization?.Parameter).IsEqualTo(tokenA);
    }

    [Test]
    public async Task AccessTokenForwardingHandler_AfterClear_DoesNotForwardStaticToken()
    {
        var userId = Guid.NewGuid().ToString();
        var sessionId = Guid.NewGuid().ToString();
        var token = CreateJwt(userId, sessionId: sessionId);
        var context = CreateHttpContextWithSession(userId, sessionId);
        var storeService = new CircuitAccessTokenService(
            _tokenStore,
            new HttpContextAccessor { HttpContext = context },
            NullLogger<CircuitAccessTokenService>.Instance);
        storeService.SetToken(token);
        storeService.ClearToken();

        var handler = new TestableAccessTokenForwardingHandler(
            new HttpContextAccessor { HttpContext = CreateHttpContextWithSession(userId, sessionId) },
            Substitute.For<ICircuitAccessTokenService>(),
            Substitute.For<ICircuitUserContext>(),
            _tokenStore,
            NullLogger<AccessTokenForwardingHandler>.Instance);
        var terminal = new CaptureHandler();
        handler.InnerHandler = terminal;

        var response = await handler.InvokeAsync(new HttpRequestMessage(HttpMethod.Get, "https://localhost/api/protected"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(terminal.Request?.Headers.Authorization).IsNull();
    }

    [Test]
    public async Task CircuitTokenStore_Resolve_ReturnsOnlyMatchingUserToken()
    {
        var userA = Guid.NewGuid().ToString();
        var userB = Guid.NewGuid().ToString();
        var tokenA = CreateJwt(userA);

        var context = CreateHttpContext(userA);
        var service = new CircuitAccessTokenService(
            _tokenStore,
            new HttpContextAccessor { HttpContext = context },
            NullLogger<CircuitAccessTokenService>.Instance);
        service.SetToken(tokenA);

        var forA = _tokenStore.Resolve(CircuitKey(context), null);
        var forB = _tokenStore.Resolve(CircuitKey(CreateHttpContext(userB)), null);

        await Assert.That(forA.Token).IsEqualTo(tokenA);
        await Assert.That(forB.Found).IsFalse();
    }

    [Test]
    public async Task CircuitAccessTokenService_AccessToken_PrefersSharedStoreTokenOverStaleLocalToken()
    {
        var userId = Guid.NewGuid().ToString();
        var staleToken = CreateJwt(userId);
        var refreshedToken = CreateJwt(userId, DateTime.UtcNow.AddMinutes(30));

        var circuitContext = CreateHttpContext(userId);
        var circuitService = new CircuitAccessTokenService(
            _tokenStore,
            new HttpContextAccessor { HttpContext = circuitContext },
            NullLogger<CircuitAccessTokenService>.Instance);
        circuitService.SetToken(staleToken);

        var refreshRequestContext = CreateHttpContext(userId);
        var refreshScopeService = new CircuitAccessTokenService(
            _tokenStore,
            new HttpContextAccessor { HttpContext = refreshRequestContext },
            NullLogger<CircuitAccessTokenService>.Instance);
        refreshScopeService.SetToken(refreshedToken);

        await Assert.That(circuitService.AccessToken).IsEqualTo(refreshedToken);
    }

    [Test]
    public async Task CircuitAccessTokenService_SetToken_PersistsSidFallbackToSharedStore()
    {
        var userId = Guid.NewGuid().ToString();
        var sidToken = CreateJwtWithSid(userId);
        var context = CreateHttpContextWithSid(userId);
        var service = new CircuitAccessTokenService(
            _tokenStore,
            new HttpContextAccessor { HttpContext = context },
            NullLogger<CircuitAccessTokenService>.Instance);

        service.SetToken(sidToken);

        // A trusted sid-only principal remains independently partitioned as subject fallback and session.
        var partitionedUserId = CircuitKey(context);
        var partitionedSessionId = SessionKey(context);
        var resolution = _tokenStore.Resolve(partitionedUserId, partitionedSessionId);
        await Assert.That(resolution.Token).IsEqualTo(sidToken);
    }

    [Test]
    public async Task CircuitAccessTokenService_SetTokenNull_ClearsLocalAndSharedSessionToken()
    {
        var userId = Guid.NewGuid().ToString();
        var sessionId = Guid.NewGuid().ToString();
        var token = CreateJwt(userId, sessionId: sessionId);
        var context = CreateHttpContextWithSession(userId, sessionId);
        var service = new CircuitAccessTokenService(
            _tokenStore,
            new HttpContextAccessor { HttpContext = context },
            NullLogger<CircuitAccessTokenService>.Instance);

        service.SetToken(token);
        service.SetToken(null);

        await Assert.That(service.AccessToken).IsNull();
        var resolution = _tokenStore.Resolve(CircuitKey(context), SessionKey(context));
        await Assert.That(resolution.Found).IsFalse();
    }

    [Test]
    public async Task CircuitAccessTokenService_ClearToken_DoesNotClearOtherSessionForSameUser()
    {
        var userId = Guid.NewGuid().ToString();
        var sessionA = Guid.NewGuid().ToString();
        var sessionB = Guid.NewGuid().ToString();
        var tokenA = CreateJwt(userId, sessionId: sessionA);
        var tokenB = CreateJwt(userId, sessionId: sessionB);
        var contextA = CreateHttpContextWithSession(userId, sessionA);
        var contextB = CreateHttpContextWithSession(userId, sessionB);

        var serviceA = new CircuitAccessTokenService(
            _tokenStore,
            new HttpContextAccessor { HttpContext = contextA },
            NullLogger<CircuitAccessTokenService>.Instance);
        serviceA.SetToken(tokenA);

        var serviceB = new CircuitAccessTokenService(
            _tokenStore,
            new HttpContextAccessor { HttpContext = contextB },
            NullLogger<CircuitAccessTokenService>.Instance);
        serviceB.SetToken(tokenB);
        serviceA.ClearToken();

        var resA = _tokenStore.Resolve(CircuitKey(contextA), SessionKey(contextA));
        var resB = _tokenStore.Resolve(CircuitKey(contextB), SessionKey(contextB));
        await Assert.That(resA.Found).IsFalse();
        await Assert.That(resB.Token).IsEqualTo(tokenB);
    }

    [Test]
    public async Task CircuitAccessTokenService_AccessToken_FallsBackToUserTokenFromDifferentSession()
    {
        var userId = Guid.NewGuid().ToString();
        var sessionA = Guid.NewGuid().ToString();
        var sessionB = Guid.NewGuid().ToString();
        var tokenA = CreateJwt(userId, sessionId: sessionA);

        var ownerService = new CircuitAccessTokenService(
            _tokenStore,
            new HttpContextAccessor { HttpContext = CreateHttpContextWithSession(userId, sessionA) },
            NullLogger<CircuitAccessTokenService>.Instance);
        ownerService.SetToken(tokenA);

        var requesterService = new CircuitAccessTokenService(
            _tokenStore,
            new HttpContextAccessor { HttpContext = CreateHttpContextWithSession(userId, sessionB) },
            NullLogger<CircuitAccessTokenService>.Instance);

        await Assert.That(requesterService.AccessToken).IsEqualTo(tokenA);
    }

    [Test]
    public async Task CircuitAccessTokenService_SetTokenRefreshSuccess_UpdatesSharedSessionTokenAndExistingCircuitSeesNewToken()
    {
        var userId = Guid.NewGuid().ToString();
        var sessionId = Guid.NewGuid().ToString();
        var staleToken = CreateJwt(userId, expires: DateTime.UtcNow.AddMinutes(20), sessionId: sessionId);
        var refreshedToken = CreateJwt(userId, expires: DateTime.UtcNow.AddMinutes(40), sessionId: sessionId);

        var circuitService = new CircuitAccessTokenService(
            _tokenStore,
            new HttpContextAccessor { HttpContext = CreateHttpContextWithSession(userId, sessionId) },
            NullLogger<CircuitAccessTokenService>.Instance);
        circuitService.SetToken(staleToken);

        var refreshScopeService = new CircuitAccessTokenService(
            _tokenStore,
            new HttpContextAccessor { HttpContext = CreateHttpContextWithSession(userId, sessionId) },
            NullLogger<CircuitAccessTokenService>.Instance);
        refreshScopeService.SetToken(refreshedToken);

        await Assert.That(circuitService.AccessToken).IsEqualTo(refreshedToken);
    }

    [Test]
    public async Task AccessTokenForwardingHandler_DoesNotForwardSetupSecret_ForInstanceOnboardingEndpoints()
    {
        // Setup-secret forwarding is now handled by SetupSecretForwardingHandler.
        // AccessTokenForwardingHandler should NOT add X-Setup-Secret headers.
        var userId = Guid.NewGuid().ToString();
        var context = CreateHttpContext(userId);
        context.Request.Headers.Cookie = "setup-secret=test-setup-secret";

        var accessor = new HttpContextAccessor { HttpContext = context };
        var handler = new TestableAccessTokenForwardingHandler(
            accessor,
            Substitute.For<ICircuitAccessTokenService>(),
            Substitute.For<ICircuitUserContext>(),
            _tokenStore,
            NullLogger<AccessTokenForwardingHandler>.Instance);
        var terminal = new CaptureHandler();
        handler.InnerHandler = terminal;

        var request = new HttpRequestMessage(HttpMethod.Post, "https://localhost/api/InstanceOnboarding/complete");
        _ = await handler.InvokeAsync(request);

        await Assert.That(terminal.Request?.Headers.Contains("X-Setup-Secret")).IsFalse();
    }

    [Test]
    public async Task AccessTokenForwardingHandler_DoesNotForwardSetupSecret_ForUnrelatedEndpoints()
    {
        var userId = Guid.NewGuid().ToString();
        var context = CreateHttpContext(userId);
        context.Request.Headers.Cookie = "setup-secret=test-setup-secret";

        var accessor = new HttpContextAccessor { HttpContext = context };
        var handler = new TestableAccessTokenForwardingHandler(
            accessor,
            Substitute.For<ICircuitAccessTokenService>(),
            Substitute.For<ICircuitUserContext>(),
            _tokenStore,
            NullLogger<AccessTokenForwardingHandler>.Instance);
        var terminal = new CaptureHandler();
        handler.InnerHandler = terminal;

        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/api/events");
        _ = await handler.InvokeAsync(request);

        await Assert.That(terminal.Request?.Headers.Contains("X-Setup-Secret")).IsFalse();
    }

    [Test]
    public async Task AccessTokenForwardingHandler_DoesNotAddSetupSecret_WhenCookieMissing()
    {
        // Setup-secret forwarding is now handled by SetupSecretForwardingHandler.
        var userId = Guid.NewGuid().ToString();
        var context = CreateHttpContext(userId);

        var accessor = new HttpContextAccessor { HttpContext = context };
        var handler = new TestableAccessTokenForwardingHandler(
            accessor,
            Substitute.For<ICircuitAccessTokenService>(),
            Substitute.For<ICircuitUserContext>(),
            _tokenStore,
            NullLogger<AccessTokenForwardingHandler>.Instance);
        var terminal = new CaptureHandler();
        handler.InnerHandler = terminal;

        var request = new HttpRequestMessage(HttpMethod.Post, "https://localhost/api/InstanceOnboarding/complete");
        _ = await handler.InvokeAsync(request);

        await Assert.That(terminal.Request?.Headers.Contains("X-Setup-Secret")).IsFalse();
    }

    private static DefaultHttpContext CreateHttpContext(string userId, string? authToken = null)
    {
        return CreateHttpContext(new Claim("sub", userId), authToken);
    }

    private static DefaultHttpContext CreateHttpContextWithSid(string userId, string? authToken = null)
    {
        return CreateHttpContext(new Claim("sid", userId), authToken);
    }

    private static DefaultHttpContext CreateHttpContext(Claim userIdClaim, string? authToken = null)
    {
        var claims = new[] { userIdClaim };
        var identity = new ClaimsIdentity(claims, "Cookies");
        var principal = new ClaimsPrincipal(identity);

        var context = new DefaultHttpContext { User = principal };

        var services = new ServiceCollection();

        if (!string.IsNullOrEmpty(authToken))
        {
            var authService = Substitute.For<IAuthenticationService>();
            var properties = new AuthenticationProperties();
            properties.StoreTokens(new[] { new AuthenticationToken { Name = "access_token", Value = authToken } });
            var ticket = new AuthenticationTicket(principal, properties, "Cookies");

            authService.AuthenticateAsync(Arg.Any<HttpContext>(), Arg.Any<string>())
                .Returns(Task.FromResult(AuthenticateResult.Success(ticket)));

            services.AddSingleton(authService);
            services.AddSingleton<IAuthenticationSchemeProvider>(new AuthenticationSchemeProvider(
                Options.Create(new AuthenticationOptions { DefaultAuthenticateScheme = "Cookies" })));
        }

        context.RequestServices = services.BuildServiceProvider();
        return context;
    }

    private static string CreateJwt(string sub, DateTime? expires = null, string? sessionId = null)
    {
        var claims = new List<Claim> { new("sub", sub) };
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            claims.Add(new Claim("sid", sessionId));
        }

        var jwt = new JwtSecurityToken(claims: claims, expires: expires);
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }
    private static string CircuitKey(DefaultHttpContext context)
    {
        context.User.TryGetCircuitSubject(out var identity);
        return identity.PartitionKey;
    }

    private static string? SessionKey(DefaultHttpContext context) =>
        context.User.TryGetSessionId(out var identity) ? identity.PartitionKey : null;


    private static DefaultHttpContext CreateHttpContextWithSession(string userId, string sessionId, string? authToken = null)
    {
        var context = CreateHttpContext(new Claim("sub", userId), authToken);
        ((ClaimsIdentity)context.User.Identity!).AddClaim(new Claim("sid", sessionId));
        return context;
    }

    private static string CreateJwtWithSid(string sid, DateTime? expires = null)
    {
        var jwt = new JwtSecurityToken(claims: new[] { new Claim("sid", sid) }, expires: expires);
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private sealed class TestableAccessTokenForwardingHandler : AccessTokenForwardingHandler
    {
        public TestableAccessTokenForwardingHandler(
            IHttpContextAccessor httpContextAccessor,
            ICircuitAccessTokenService circuitAccessTokenService,
            ICircuitUserContext circuitUserContext,
            ICircuitTokenStore tokenStore,
            ILogger<AccessTokenForwardingHandler> logger)
            : base(httpContextAccessor, circuitAccessTokenService, circuitUserContext, tokenStore, logger)
        {
        }

        public Task<HttpResponseMessage> InvokeAsync(HttpRequestMessage request)
        {
            return SendAsync(request, CancellationToken.None);
        }
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;

        public void Advance(TimeSpan duration) => utcNow += duration;
    }
}
