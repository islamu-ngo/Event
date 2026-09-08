using System.Security.Claims;
using Explore.Blazor.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Explore.Blazor.IntegrationTests.Handlers;

public class BffCookieForwardingHandlerTests
{
    [Test]
    public async Task SendAsync_WithinCircuitActivityScope_ForwardsCookieHeader()
    {
        var circuitCookieStore = new BffAuthCookieStore();
        circuitCookieStore.SetCookieHeader("AuthCookie=test-value; XSRF-TOKEN=xsrf-value");

        var innerHandler = new CapturingHandler();

        using (circuitCookieStore.BeginActivityScope())
        {
            var handler = new BffCookieForwardingHandler(
                new BffAuthCookieStore(),
                NullLogger<BffCookieForwardingHandler>.Instance)
            {
                InnerHandler = innerHandler
            };

            using var invoker = new HttpMessageInvoker(handler);
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://bff.example.com/bff/auth/refresh-session");
            _ = await invoker.SendAsync(request, CancellationToken.None);
        }

        await Assert.That(innerHandler.CapturedRequest).IsNotNull();
        await Assert.That(innerHandler.CapturedRequest!.Headers.TryGetValues("Cookie", out var cookieValues)).IsTrue();
        await Assert.That(cookieValues).Contains("AuthCookie=test-value; XSRF-TOKEN=xsrf-value");
        await Assert.That(innerHandler.CapturedRequest.Headers.TryGetValues("X-CSRF-TOKEN", out var headerValues)).IsTrue();
        await Assert.That(headerValues).Contains("xsrf-value");
    }

    [Test]
    public async Task SendAsync_DuringInitialHttpRequest_ForwardsCurrentRequestCookieWhenCircuitStoreIsEmpty()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Cookie = "AuthCookie=request-value; XSRF-TOKEN=request-xsrf";
        var innerHandler = new CapturingHandler();
        var handler = new BffCookieForwardingHandler(
            new BffAuthCookieStore(),
            new HttpContextAccessor { HttpContext = httpContext },
            NullLogger<BffCookieForwardingHandler>.Instance)
        {
            InnerHandler = innerHandler
        };

        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://bff.example.com/api/TenantOnboarding/status");
        _ = await invoker.SendAsync(request, CancellationToken.None);

        await Assert.That(innerHandler.CapturedRequest).IsNotNull();
        await Assert.That(innerHandler.CapturedRequest!.Headers.TryGetValues("Cookie", out var cookieValues)).IsTrue();
        await Assert.That(cookieValues).Contains("AuthCookie=request-value; XSRF-TOKEN=request-xsrf");
        await Assert.That(innerHandler.CapturedRequest.Headers.TryGetValues("X-CSRF-TOKEN", out var headerValues)).IsTrue();
        await Assert.That(headerValues).Contains("request-xsrf");
    }

    [Test]
    public async Task SendAsync_WhenNoCookieIsAvailable_DoesNotForwardCookieOrThrow()
    {
        var innerHandler = new CapturingHandler();
        var handler = new BffCookieForwardingHandler(
            new BffAuthCookieStore(),
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
            NullLogger<BffCookieForwardingHandler>.Instance)
        {
            InnerHandler = innerHandler
        };

        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://bff.example.com/api/TenantOnboarding/status");
        _ = await invoker.SendAsync(request, CancellationToken.None);

        await Assert.That(innerHandler.CapturedRequest).IsNotNull();
        await Assert.That(innerHandler.CapturedRequest!.Headers.Contains("Cookie")).IsFalse();
        await Assert.That(innerHandler.CapturedRequest.Headers.Contains("X-CSRF-TOKEN")).IsFalse();
    }

    [Test]
    public async Task SendAsync_WhenCapturedCircuitCookieLacksXsrf_MergesCurrentRequestXsrfCookie()
    {
        var circuitCookieStore = new BffAuthCookieStore();
        circuitCookieStore.SetCookieHeader("AuthCookie=circuit-value");

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Cookie = "XSRF-TOKEN=current-xsrf";
        var innerHandler = new CapturingHandler();
        var handler = new BffCookieForwardingHandler(
            circuitCookieStore,
            new HttpContextAccessor { HttpContext = httpContext },
            NullLogger<BffCookieForwardingHandler>.Instance)
        {
            InnerHandler = innerHandler
        };

        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://bff.example.com/bff/storage/upload-session");
        _ = await invoker.SendAsync(request, CancellationToken.None);

        await Assert.That(innerHandler.CapturedRequest).IsNotNull();
        await Assert.That(innerHandler.CapturedRequest!.Headers.TryGetValues("Cookie", out var cookieValues)).IsTrue();
        await Assert.That(cookieValues).Contains("AuthCookie=circuit-value; XSRF-TOKEN=current-xsrf");
        await Assert.That(innerHandler.CapturedRequest.Headers.TryGetValues("X-CSRF-TOKEN", out var headerValues)).IsTrue();
        await Assert.That(headerValues).Contains("current-xsrf");
    }

    [Test]
    public async Task SendAsync_WhenCapturedCircuitCookieHasStaleAntiforgeryTokens_UsesCurrentRequestAntiforgeryPair()
    {
        var circuitCookieStore = new BffAuthCookieStore();
        circuitCookieStore.SetCookieHeader("AuthCookie=circuit-value; .AspNetCore.Antiforgery.test=stale-cookie; XSRF-TOKEN=stale-xsrf");

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Cookie = ".AspNetCore.Antiforgery.test=current-cookie; XSRF-TOKEN=current-xsrf";
        var innerHandler = new CapturingHandler();
        var handler = new BffCookieForwardingHandler(
            circuitCookieStore,
            new HttpContextAccessor { HttpContext = httpContext },
            NullLogger<BffCookieForwardingHandler>.Instance)
        {
            InnerHandler = innerHandler
        };

        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://bff.example.com/bff/storage/upload-session");
        _ = await invoker.SendAsync(request, CancellationToken.None);

        await Assert.That(innerHandler.CapturedRequest).IsNotNull();
        await Assert.That(innerHandler.CapturedRequest!.Headers.TryGetValues("Cookie", out var cookieValues)).IsTrue();
        await Assert.That(cookieValues).Contains("AuthCookie=circuit-value; .AspNetCore.Antiforgery.test=current-cookie; XSRF-TOKEN=current-xsrf");
        await Assert.That(innerHandler.CapturedRequest.Headers.TryGetValues("X-CSRF-TOKEN", out var headerValues)).IsTrue();
        await Assert.That(headerValues).Contains("current-xsrf");
    }

    [Test]
    public async Task SendAsync_WhenMutatingBffSelfCall_AddsBoundSelfCallToken()
    {
        using var provider = CreateSelfCallServices();
        var selfCallTokenService = provider.GetRequiredService<IBffSelfCallTokenService>();
        var actorUserId = Guid.NewGuid().ToString("D");

        var httpContext = new DefaultHttpContext
        {
            RequestServices = provider,
            User = CreateUser(actorUserId)
        };
        httpContext.Request.Host = new HostString("bff.example.com");
        var innerHandler = new CapturingHandler();
        var handler = new BffCookieForwardingHandler(
            new BffAuthCookieStore(),
            new HttpContextAccessor { HttpContext = httpContext },
            NullLogger<BffCookieForwardingHandler>.Instance,
            selfCallTokenService)
        {
            InnerHandler = innerHandler
        };

        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://bff.example.com/bff/support-access/sessions");
        _ = await invoker.SendAsync(request, CancellationToken.None);

        await Assert.That(innerHandler.CapturedRequest).IsNotNull();
        await Assert.That(innerHandler.CapturedRequest!.Headers.TryGetValues(BffSelfCallHeaders.Token, out var tokenValues)).IsTrue();
        var token = tokenValues!.Single();

        var validationContext = new DefaultHttpContext
        {
            RequestServices = provider,
            User = CreateUser(actorUserId)
        };
        validationContext.Request.Method = HttpMethods.Post;
        validationContext.Request.Host = new HostString("bff.example.com");
        validationContext.Request.Path = "/bff/support-access/sessions";
        validationContext.Request.Headers[BffSelfCallHeaders.Token] = token;

        await Assert.That(selfCallTokenService.Validate(validationContext)).IsTrue();
    }

    [Test]
    public async Task SendAsync_WhenMutatingNonBffRequest_DoesNotAddSelfCallToken()
    {
        using var provider = CreateSelfCallServices();
        var selfCallTokenService = provider.GetRequiredService<IBffSelfCallTokenService>();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = provider,
            User = CreateUser(Guid.NewGuid().ToString("D"))
        };
        httpContext.Request.Host = new HostString("bff.example.com");
        var innerHandler = new CapturingHandler();
        var handler = new BffCookieForwardingHandler(
            new BffAuthCookieStore(),
            new HttpContextAccessor { HttpContext = httpContext },
            NullLogger<BffCookieForwardingHandler>.Instance,
            selfCallTokenService)
        {
            InnerHandler = innerHandler
        };

        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/api/support-access/sessions");
        _ = await invoker.SendAsync(request, CancellationToken.None);

        await Assert.That(innerHandler.CapturedRequest).IsNotNull();
        await Assert.That(innerHandler.CapturedRequest!.Headers.Contains(BffSelfCallHeaders.Token)).IsFalse();
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? CapturedRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CapturedRequest = request;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }

    private static ServiceProvider CreateSelfCallServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddSingleton<IBffSelfCallTokenService, BffSelfCallTokenService>();
        return services.BuildServiceProvider();
    }

    private static ClaimsPrincipal CreateUser(string userId) => new(new ClaimsIdentity(
        [
            new Claim("sub", userId),
            new Claim(ClaimTypes.NameIdentifier, userId)
        ],
        "Cookies"));
}
