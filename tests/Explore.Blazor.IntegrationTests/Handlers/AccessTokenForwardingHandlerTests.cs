using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using Event.Web.BffHosting.Security;
using Explore.Blazor.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Explore.Blazor.IntegrationTests.Handlers;

public class AccessTokenForwardingHandlerTests
{
    private readonly ICircuitTokenStore _tokenStore = new CircuitTokenStore(
        NullLogger<CircuitTokenStore>.Instance);

    [Test]
    public async Task SendAsync_WithAccessTokenInHttpContext_AddsAuthorizationHeader()
    {
        var userId = Guid.NewGuid().ToString();
        var httpContext = CreateHttpContext(userId, "context-token-123");
        var httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };

        var innerHandler = new CapturingHandler();
        var circuitTokenService = Substitute.For<ICircuitAccessTokenService>();
        var circuitUserContext = Substitute.For<ICircuitUserContext>();
        var handler = new AccessTokenForwardingHandler(
            httpContextAccessor, circuitTokenService, circuitUserContext,
            _tokenStore, NullLogger<AccessTokenForwardingHandler>.Instance)
        {
            InnerHandler = innerHandler
        };

        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/protected");
        _ = await invoker.SendAsync(request, CancellationToken.None);

        await Assert.That(innerHandler.CapturedRequest).IsNotNull();
        await Assert.That(innerHandler.CapturedRequest!.Headers.Authorization).IsNotNull();
        await Assert.That(innerHandler.CapturedRequest.Headers.Authorization!.Scheme).IsEqualTo("Bearer");
        await Assert.That(innerHandler.CapturedRequest.Headers.Authorization.Parameter).IsEqualTo("context-token-123");
    }

    [Test]
    public async Task SendAsync_WithoutToken_DoesNotAddAuthorizationHeader()
    {
        var userId = Guid.NewGuid().ToString();
        var httpContext = CreateHttpContext(userId, token: null);
        var httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };

        var innerHandler = new CapturingHandler();
        var circuitTokenService = Substitute.For<ICircuitAccessTokenService>();
        var circuitUserContext = Substitute.For<ICircuitUserContext>();
        var handler = new AccessTokenForwardingHandler(
            httpContextAccessor, circuitTokenService, circuitUserContext,
            _tokenStore, NullLogger<AccessTokenForwardingHandler>.Instance)
        {
            InnerHandler = innerHandler
        };

        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/protected");
        _ = await invoker.SendAsync(request, CancellationToken.None);

        await Assert.That(innerHandler.CapturedRequest).IsNotNull();
        await Assert.That(innerHandler.CapturedRequest!.Headers.Authorization).IsNull();
    }

    [Test]
    public async Task SendAsync_WithExistingAuthorizationHeader_DoesNotOverwrite()
    {
        var userId = Guid.NewGuid().ToString();
        var httpContext = CreateHttpContext(userId, "context-token-123");
        var httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };

        var innerHandler = new CapturingHandler();
        var circuitTokenService = Substitute.For<ICircuitAccessTokenService>();
        var circuitUserContext = Substitute.For<ICircuitUserContext>();
        var handler = new AccessTokenForwardingHandler(
            httpContextAccessor, circuitTokenService, circuitUserContext,
            _tokenStore, NullLogger<AccessTokenForwardingHandler>.Instance)
        {
            InnerHandler = innerHandler
        };

        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/protected");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "preset-token");
        _ = await invoker.SendAsync(request, CancellationToken.None);

        await Assert.That(innerHandler.CapturedRequest).IsNotNull();
        await Assert.That(innerHandler.CapturedRequest!.Headers.Authorization).IsNotNull();
        await Assert.That(innerHandler.CapturedRequest.Headers.Authorization!.Parameter).IsEqualTo("preset-token");
    }

    [Test]
    public async Task SendAsync_WithExpiredHttpContextToken_UsesFreshStoredToken()
    {
        var userId = Guid.NewGuid().ToString();
        var expiredContextToken = CreateUnsignedJwt(userId, DateTime.UtcNow.AddMinutes(-5));
        var freshStoredToken = CreateUnsignedJwt(userId, DateTime.UtcNow.AddMinutes(30));
        var httpContext = CreateHttpContext(userId, expiredContextToken);
        var httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };

        var tokenStoreService = new CircuitAccessTokenService(
            _tokenStore,
            httpContextAccessor,
            NullLogger<CircuitAccessTokenService>.Instance);
        tokenStoreService.SetToken(freshStoredToken);

        var innerHandler = new CapturingHandler();
        var circuitTokenService = Substitute.For<ICircuitAccessTokenService>();
        var circuitUserContext = Substitute.For<ICircuitUserContext>();
        var handler = new AccessTokenForwardingHandler(
            httpContextAccessor, circuitTokenService, circuitUserContext,
            _tokenStore, NullLogger<AccessTokenForwardingHandler>.Instance)
        {
            InnerHandler = innerHandler
        };

        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/protected");
        _ = await invoker.SendAsync(request, CancellationToken.None);

        await Assert.That(innerHandler.CapturedRequest).IsNotNull();
        await Assert.That(innerHandler.CapturedRequest!.Headers.Authorization).IsNotNull();
        await Assert.That(innerHandler.CapturedRequest.Headers.Authorization!.Parameter).IsEqualTo(freshStoredToken);
    }

    [Test]
    public async Task SendAsync_WithNearExpiryHttpContextToken_ForwardsTokenAsLastResort()
    {
        var userId = Guid.NewGuid().ToString();
        var nearExpiryContextToken = CreateUnsignedJwt(userId, DateTime.UtcNow.AddSeconds(10));
        var httpContext = CreateHttpContext(userId, nearExpiryContextToken);
        var httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };

        var innerHandler = new CapturingHandler();
        var circuitTokenService = Substitute.For<ICircuitAccessTokenService>();
        var circuitUserContext = Substitute.For<ICircuitUserContext>();
        var handler = new AccessTokenForwardingHandler(
            httpContextAccessor, circuitTokenService, circuitUserContext,
            _tokenStore, NullLogger<AccessTokenForwardingHandler>.Instance)
        {
            InnerHandler = innerHandler
        };

        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/protected");
        _ = await invoker.SendAsync(request, CancellationToken.None);

        await Assert.That(innerHandler.CapturedRequest).IsNotNull();
        await Assert.That(innerHandler.CapturedRequest!.Headers.Authorization).IsNotNull();
        await Assert.That(innerHandler.CapturedRequest.Headers.Authorization!.Parameter).IsEqualTo(nearExpiryContextToken);
    }

    [Test]
    public async Task SendAsync_WithMissingHttpContextToken_AndCookieRefreshReturnsUsableToken_UsesRefreshedToken()
    {
        var userId = Guid.NewGuid().ToString();
        var freshRefreshedToken = CreateUnsignedJwt(userId);
        var httpContext = CreateHttpContext(userId, token: null, freshRefreshedToken);
        var httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };

        var innerHandler = new CapturingHandler();
        var circuitTokenService = Substitute.For<ICircuitAccessTokenService>();
        var circuitUserContext = Substitute.For<ICircuitUserContext>();
        var handler = new AccessTokenForwardingHandler(
            httpContextAccessor, circuitTokenService, circuitUserContext,
            _tokenStore, NullLogger<AccessTokenForwardingHandler>.Instance)
        {
            InnerHandler = innerHandler
        };

        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/protected");
        _ = await invoker.SendAsync(request, CancellationToken.None);

        await Assert.That(innerHandler.CapturedRequest).IsNotNull();
        await Assert.That(innerHandler.CapturedRequest!.Headers.Authorization).IsNotNull();
        await Assert.That(innerHandler.CapturedRequest.Headers.Authorization!.Scheme).IsEqualTo("Bearer");
        await Assert.That(innerHandler.CapturedRequest.Headers.Authorization.Parameter).IsEqualTo(freshRefreshedToken);
        circuitTokenService.Received(1).SetToken(freshRefreshedToken);
    }

    [Test]
    public async Task SendAsync_WithExpiredHttpContextToken_AndCookieRefreshReturnsUsableToken_UsesRefreshedToken()
    {
        var userId = Guid.NewGuid().ToString();
        var expiredContextToken = CreateUnsignedJwt(userId, DateTime.UtcNow.AddMinutes(-5));
        var freshRefreshedToken = CreateUnsignedJwt(userId);
        var httpContext = CreateHttpContext(userId, expiredContextToken, freshRefreshedToken);
        var httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };

        var innerHandler = new CapturingHandler();
        var circuitTokenService = Substitute.For<ICircuitAccessTokenService>();
        var circuitUserContext = Substitute.For<ICircuitUserContext>();
        var handler = new AccessTokenForwardingHandler(
            httpContextAccessor, circuitTokenService, circuitUserContext,
            _tokenStore, NullLogger<AccessTokenForwardingHandler>.Instance)
        {
            InnerHandler = innerHandler
        };

        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/protected");
        _ = await invoker.SendAsync(request, CancellationToken.None);

        await Assert.That(innerHandler.CapturedRequest).IsNotNull();
        await Assert.That(innerHandler.CapturedRequest!.Headers.Authorization).IsNotNull();
        await Assert.That(innerHandler.CapturedRequest.Headers.Authorization!.Scheme).IsEqualTo("Bearer");
        await Assert.That(innerHandler.CapturedRequest.Headers.Authorization.Parameter).IsEqualTo(freshRefreshedToken);
        circuitTokenService.Received(1).SetToken(freshRefreshedToken);
    }

    [Test]
    public async Task SendAsync_WithExpiredHttpContextToken_AndCookieRefreshFails_UsesFreshStoredToken()
    {
        var userId = Guid.NewGuid().ToString();
        var expiredContextToken = CreateUnsignedJwt(userId, DateTime.UtcNow.AddMinutes(-5));
        var freshStoredToken = CreateUnsignedJwt(userId, DateTime.UtcNow.AddMinutes(30));
        var httpContext = CreateHttpContext(userId, expiredContextToken, null);
        var httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };

        var tokenStoreService = new CircuitAccessTokenService(
            _tokenStore,
            httpContextAccessor,
            NullLogger<CircuitAccessTokenService>.Instance);
        tokenStoreService.SetToken(freshStoredToken);

        var innerHandler = new CapturingHandler();
        var circuitTokenService = Substitute.For<ICircuitAccessTokenService>();
        var circuitUserContext = Substitute.For<ICircuitUserContext>();
        var handler = new AccessTokenForwardingHandler(
            httpContextAccessor, circuitTokenService, circuitUserContext,
            _tokenStore, NullLogger<AccessTokenForwardingHandler>.Instance)
        {
            InnerHandler = innerHandler
        };

        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/protected");
        _ = await invoker.SendAsync(request, CancellationToken.None);

        await Assert.That(innerHandler.CapturedRequest).IsNotNull();
        await Assert.That(innerHandler.CapturedRequest!.Headers.Authorization).IsNotNull();
        await Assert.That(innerHandler.CapturedRequest.Headers.Authorization!.Scheme).IsEqualTo("Bearer");
        await Assert.That(innerHandler.CapturedRequest.Headers.Authorization.Parameter).IsEqualTo(freshStoredToken);
        circuitTokenService.DidNotReceive().SetToken(Arg.Any<string>());
    }

    [Test]
    public async Task SendAsync_WithExpiredHttpContextToken_AndCookieRefreshStillExpired_UsesBffSelfRefreshToken()
    {
        var userId = Guid.NewGuid().ToString();
        var expiredContextToken = CreateUnsignedJwt(userId, DateTime.UtcNow.AddMinutes(-5));
        var freshRefreshedToken = CreateUnsignedJwt(userId, DateTime.UtcNow.AddMinutes(30));
        var httpContext = CreateHttpContext(userId, expiredContextToken, expiredContextToken);
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("localhost:5001");
        httpContext.Request.Headers.Cookie = ".AspNetCore.Cookies=test-cookie";
        var httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };

        var refreshHandler = new BffSelfRefreshHandler(() => _tokenStore.Store(CircuitKey(httpContext), sessionId: null, freshRefreshedToken));
        var selfClientFactory = new TestHttpClientFactory(refreshHandler);
        var innerHandler = new CapturingHandler();
        var circuitTokenService = Substitute.For<ICircuitAccessTokenService>();
        var circuitUserContext = Substitute.For<ICircuitUserContext>();
        var handler = new AccessTokenForwardingHandler(
            httpContextAccessor, circuitTokenService, circuitUserContext,
            _tokenStore, NullLogger<AccessTokenForwardingHandler>.Instance,
            selfClientFactory)
        {
            InnerHandler = innerHandler
        };

        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/api/event");
        _ = await invoker.SendAsync(request, CancellationToken.None);

        await Assert.That(refreshHandler.Called).IsTrue();
        await Assert.That(refreshHandler.CapturedRequest).IsNotNull();
        await Assert.That(refreshHandler.CapturedRequest!.RequestUri!.AbsolutePath).IsEqualTo("/bff/auth/refresh-session/internal");
        await Assert.That(innerHandler.CapturedRequest).IsNotNull();
        await Assert.That(innerHandler.CapturedRequest!.Headers.Authorization).IsNotNull();
        await Assert.That(innerHandler.CapturedRequest.Headers.Authorization!.Scheme).IsEqualTo("Bearer");
        await Assert.That(innerHandler.CapturedRequest.Headers.Authorization.Parameter).IsEqualTo(freshRefreshedToken);
        circuitTokenService.Received(1).SetToken(freshRefreshedToken);
    }

    [Test]
    public async Task SendAsync_WithExpiredHttpContextToken_AndSelfRefreshStoresDifferentSession_UsesUserFallbackToken()
    {
        var userId = Guid.NewGuid().ToString();
        var expiredContextToken = CreateUnsignedJwt(userId, DateTime.UtcNow.AddMinutes(-5));
        var freshRefreshedToken = CreateUnsignedJwt(userId, DateTime.UtcNow.AddMinutes(30));
        var httpContext = CreateHttpContextWithSession(userId, sessionId: "old-session", expiredContextToken, expiredContextToken);
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("localhost:5001");
        httpContext.Request.Headers.Cookie = ".AspNetCore.Cookies=test-cookie";
        var httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };

        var refreshedContext = CreateHttpContextWithSession(userId, "new-session", token: null);
        var refreshHandler = new BffSelfRefreshHandler(() => _tokenStore.Store(CircuitKey(refreshedContext), SessionKey(refreshedContext), freshRefreshedToken));
        var selfClientFactory = new TestHttpClientFactory(refreshHandler);
        var innerHandler = new CapturingHandler();
        var circuitTokenService = Substitute.For<ICircuitAccessTokenService>();
        var circuitUserContext = Substitute.For<ICircuitUserContext>();
        var handler = new AccessTokenForwardingHandler(
            httpContextAccessor, circuitTokenService, circuitUserContext,
            _tokenStore, NullLogger<AccessTokenForwardingHandler>.Instance,
            selfClientFactory)
        {
            InnerHandler = innerHandler
        };

        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/api/event");
        _ = await invoker.SendAsync(request, CancellationToken.None);

        await Assert.That(refreshHandler.Called).IsTrue();
        await Assert.That(innerHandler.CapturedRequest).IsNotNull();
        await Assert.That(innerHandler.CapturedRequest!.Headers.Authorization).IsNotNull();
        await Assert.That(innerHandler.CapturedRequest.Headers.Authorization!.Scheme).IsEqualTo("Bearer");
        await Assert.That(innerHandler.CapturedRequest.Headers.Authorization.Parameter).IsEqualTo(freshRefreshedToken);
        circuitTokenService.Received(1).SetToken(freshRefreshedToken);
    }

    [Test]
    public async Task SendAsync_WithExpiredHttpContextToken_AndSelfRefreshTokenSubjectDiffersFromPrincipal_UsesRefreshedToken()
    {
        var principalUserId = Guid.NewGuid().ToString();
        var tokenSubject = $"keycloak-{Guid.NewGuid():N}";
        var expiredContextToken = CreateUnsignedJwt(tokenSubject, DateTime.UtcNow.AddMinutes(-5));
        var freshRefreshedToken = CreateUnsignedJwt(tokenSubject, DateTime.UtcNow.AddMinutes(30));
        var httpContext = CreateHttpContext(principalUserId, expiredContextToken, expiredContextToken);
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("localhost:5001");
        httpContext.Request.Headers.Cookie = ".AspNetCore.Cookies=test-cookie";
        var httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };

        var refreshContext = CreateHttpContext(principalUserId, token: null);
        var refreshTokenService = new CircuitAccessTokenService(
            _tokenStore,
            new HttpContextAccessor { HttpContext = refreshContext },
            NullLogger<CircuitAccessTokenService>.Instance);
        var refreshHandler = new BffSelfRefreshHandler(() => refreshTokenService.SetToken(freshRefreshedToken));
        var selfClientFactory = new TestHttpClientFactory(refreshHandler);
        var innerHandler = new CapturingHandler();
        var circuitTokenService = Substitute.For<ICircuitAccessTokenService>();
        var circuitUserContext = Substitute.For<ICircuitUserContext>();
        var handler = new AccessTokenForwardingHandler(
            httpContextAccessor, circuitTokenService, circuitUserContext,
            _tokenStore, NullLogger<AccessTokenForwardingHandler>.Instance,
            selfClientFactory)
        {
            InnerHandler = innerHandler
        };

        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/api/event");
        _ = await invoker.SendAsync(request, CancellationToken.None);

        await Assert.That(refreshHandler.Called).IsTrue();
        await Assert.That(innerHandler.CapturedRequest).IsNotNull();
        await Assert.That(innerHandler.CapturedRequest!.Headers.Authorization).IsNotNull();
        await Assert.That(innerHandler.CapturedRequest.Headers.Authorization!.Scheme).IsEqualTo("Bearer");
        await Assert.That(innerHandler.CapturedRequest.Headers.Authorization.Parameter).IsEqualTo(freshRefreshedToken);
        circuitTokenService.Received(1).SetToken(freshRefreshedToken);
    }

    [Test]
    public async Task SendAsync_WithinCircuitActivityScope_UsesCircuitUserContextTokenStore()
    {
        var userId = Guid.NewGuid().ToString();
        var token = CreateUnsignedJwt(userId);
        var trustedContext = CreateHttpContext(userId, token: null);
        var httpContextAccessor = new HttpContextAccessor { HttpContext = trustedContext };

        var tokenStoreService = new CircuitAccessTokenService(
            _tokenStore,
            httpContextAccessor,
            NullLogger<CircuitAccessTokenService>.Instance);
        tokenStoreService.SetToken(token);

        var circuitUserContext = new CircuitUserContext();
        circuitUserContext.SetUserId(CircuitKey(trustedContext));
        httpContextAccessor.HttpContext = null;

        var innerHandler = new CapturingHandler();

        using (circuitUserContext.BeginActivityScope())
        {
            var handler = new AccessTokenForwardingHandler(
                httpContextAccessor,
                Substitute.For<ICircuitAccessTokenService>(),
                new CircuitUserContext(),
                _tokenStore,
                NullLogger<AccessTokenForwardingHandler>.Instance)
            {
                InnerHandler = innerHandler
            };

            using var invoker = new HttpMessageInvoker(handler);
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/protected");
            _ = await invoker.SendAsync(request, CancellationToken.None);
        }

        await Assert.That(innerHandler.CapturedRequest).IsNotNull();
        await Assert.That(innerHandler.CapturedRequest!.Headers.Authorization).IsNotNull();
        await Assert.That(innerHandler.CapturedRequest.Headers.Authorization!.Scheme).IsEqualTo("Bearer");
        await Assert.That(innerHandler.CapturedRequest.Headers.Authorization.Parameter).IsEqualTo(token);
    }

    private static string CircuitKey(HttpContext context)
    {
        context.User.TryGetCircuitSubject(out var identity);
        return identity.PartitionKey;
    }

    private static string? SessionKey(HttpContext context) =>
        context.User.TryGetSessionId(out var identity) ? identity.PartitionKey : null;

    private static string CreateUnsignedJwt(string userId, DateTime? expires = null)
    {
        var jwt = new JwtSecurityToken(
            claims:
            [
                new Claim("sub", userId)
            ],
            expires: expires ?? DateTime.UtcNow.AddMinutes(30));

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private static HttpContext CreateHttpContext(string userId, string? token, params string?[] additionalTokens)
    {
        return CreateHttpContextCore(userId, sessionId: null, token, additionalTokens);
    }

    private static HttpContext CreateHttpContextWithSession(string userId, string sessionId, string? token, params string?[] additionalTokens)
    {
        return CreateHttpContextCore(userId, sessionId, token, additionalTokens);
    }

    private static HttpContext CreateHttpContextCore(string userId, string? sessionId, string? token, params string?[] additionalTokens)
    {
        var authService = Substitute.For<IAuthenticationService>();

        var claims = new List<Claim>
        {
            new("sub", userId),
            new(ClaimTypes.NameIdentifier, userId)
        };

        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            claims.Add(new Claim("sid", sessionId));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Cookies"));

        var tokens = new[] { token }.Concat(additionalTokens ?? Array.Empty<string?>());
        var results = tokens.Select(t =>
        {
            var authProperties = new AuthenticationProperties();
            if (!string.IsNullOrWhiteSpace(t))
            {
                authProperties.StoreTokens(
                [
                    new AuthenticationToken { Name = "access_token", Value = t }
                ]);
            }

            return AuthenticateResult.Success(new AuthenticationTicket(principal, authProperties, TestAuthHandler.SchemeName));
        }).ToArray();

        authService.AuthenticateAsync(Arg.Any<HttpContext>(), Arg.Any<string?>())
            .Returns(results[0], results.Skip(1).ToArray());

        var services = new ServiceCollection();
        services.AddSingleton(authService);

        var httpContext = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider()
        };
        httpContext.User = principal;
        return httpContext;
    }

    private sealed class TestHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class BffSelfRefreshHandler(Action onRefresh) : HttpMessageHandler
    {
        public bool Called { get; private set; }
        public HttpRequestMessage? CapturedRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Called = true;
            CapturedRequest = request;
            onRefresh();
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                RequestMessage = request
            });
        }
    }
}
