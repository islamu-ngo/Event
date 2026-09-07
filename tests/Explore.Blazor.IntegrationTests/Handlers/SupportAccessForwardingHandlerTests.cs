using System.Security.Claims;
using Event.Web.BffHosting.Security;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;

namespace Explore.Blazor.IntegrationTests.Handlers;

public class SupportAccessForwardingHandlerTests
{
    private const string UserId = "support-admin-1";
    private const string OidcSessionId = "oidc-session-1";
    private const string FutureSupportHeaderName = "X-Support-Access-Future-Scope";

    [Test]
    public async Task SendAsync_ApiRequestWithActiveOwnedSession_StripsBrowserHeadersAndAddsTrustedSessionId()
    {
        var session = CreateActiveSession();
        var httpContextAccessor = CreateHttpContextAccessor(CreateUser());
        var store = CreateStore(httpContextAccessor);
        var storeResult = await store.StoreAsync(httpContextAccessor.HttpContext!.User, session);
        await Assert.That(storeResult.Success).IsTrue();

        var innerHandler = new CapturingHandler();
        using var handler = new SupportAccessForwardingHandler(store)
        {
            InnerHandler = innerHandler
        };

        using var invoker = new HttpMessageInvoker(handler, disposeHandler: false);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/api/events");
        AddBrowserSuppliedSupportHeaders(request);
        using var response = await invoker.SendAsync(request, CancellationToken.None);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(innerHandler.CapturedRequest).IsNotNull();
        await Assert.That(innerHandler.CapturedRequest!.Headers.GetValues(EventBffHeaderNames.SupportAccessSessionId).Single())
            .IsEqualTo(session.Id!.Value.ToString("D"));
        await Assert.That(innerHandler.CapturedRequest.Headers.Contains(EventBffHeaderNames.SupportAccessTargetTenantId)).IsFalse();
        await Assert.That(innerHandler.CapturedRequest.Headers.Contains(EventBffHeaderNames.SupportAccessMode)).IsFalse();
        await Assert.That(innerHandler.CapturedRequest.Headers.Contains(FutureSupportHeaderName)).IsFalse();
    }

    [Test]
    public async Task SendAsync_ApiRequestWithoutActiveSession_StripsBrowserHeadersAndDoesNotForwardSupportContext()
    {
        var store = CreateStore(CreateHttpContextAccessor(CreateUser()));
        var innerHandler = new CapturingHandler();
        using var handler = new SupportAccessForwardingHandler(store)
        {
            InnerHandler = innerHandler
        };

        using var invoker = new HttpMessageInvoker(handler, disposeHandler: false);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/api/events");
        AddBrowserSuppliedSupportHeaders(request);
        _ = await invoker.SendAsync(request, CancellationToken.None);

        await Assert.That(innerHandler.CapturedRequest).IsNotNull();
        await AssertNoSupportAccessHeaders(innerHandler.CapturedRequest!);
    }

    [Test]
    public async Task SendAsync_ApiRequestWithSessionOwnedByDifferentUser_DoesNotForwardSupportContext()
    {
        var httpContextAccessor = CreateHttpContextAccessor(CreateUser());
        var store = CreateStore(httpContextAccessor);
        var storeResult = await store.StoreAsync(httpContextAccessor.HttpContext!.User, CreateActiveSession());
        await Assert.That(storeResult.Success).IsTrue();

        httpContextAccessor.HttpContext = new DefaultHttpContext
        {
            User = CreateUser("other-support-admin", "other-oidc-session")
        };

        var innerHandler = new CapturingHandler();
        using var handler = new SupportAccessForwardingHandler(store)
        {
            InnerHandler = innerHandler
        };

        using var invoker = new HttpMessageInvoker(handler, disposeHandler: false);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/api/events");
        AddBrowserSuppliedSupportHeaders(request);
        _ = await invoker.SendAsync(request, CancellationToken.None);

        await Assert.That(innerHandler.CapturedRequest).IsNotNull();
        await AssertNoSupportAccessHeaders(innerHandler.CapturedRequest!);
    }

    [Test]
    public async Task SendAsync_NonApiRequestWithActiveSession_StripsBrowserHeadersAndDoesNotForwardSupportContext()
    {
        var session = CreateActiveSession();
        var httpContextAccessor = CreateHttpContextAccessor(CreateUser());
        var store = CreateStore(httpContextAccessor);
        var storeResult = await store.StoreAsync(httpContextAccessor.HttpContext!.User, session);
        await Assert.That(storeResult.Success).IsTrue();

        var innerHandler = new CapturingHandler();
        using var handler = new SupportAccessForwardingHandler(store)
        {
            InnerHandler = innerHandler
        };

        using var invoker = new HttpMessageInvoker(handler, disposeHandler: false);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/health");
        AddBrowserSuppliedSupportHeaders(request);
        _ = await invoker.SendAsync(request, CancellationToken.None);

        await Assert.That(innerHandler.CapturedRequest).IsNotNull();
        await AssertNoSupportAccessHeaders(innerHandler.CapturedRequest!);
    }

    private static BffSupportAccessSessionStore CreateStore(IHttpContextAccessor httpContextAccessor)
    {
        var cache = new TestDistributedCache();
        return new BffSupportAccessSessionStore(cache, httpContextAccessor, new CircuitUserContext());
    }

    private static HttpContextAccessor CreateHttpContextAccessor(ClaimsPrincipal user)
    {
        return new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = user
            }
        };
    }

    private static ClaimsPrincipal CreateUser(
        string userId = UserId,
        string sessionId = OidcSessionId)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("sub", userId),
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim("sid", sessionId)
            ],
            "Cookies"));
    }

    private static SupportAccessSessionDto CreateActiveSession() => new()
    {
        Id = Guid.NewGuid(),
        TargetTenantId = Guid.NewGuid(),
        ModeId = 1,
        AllowsWrites = false,
        StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
        ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(15),
        IsActive = true
    };

    private static void AddBrowserSuppliedSupportHeaders(HttpRequestMessage request)
    {
        request.Headers.Add(EventBffHeaderNames.SupportAccessSessionId, Guid.NewGuid().ToString("D"));
        request.Headers.Add(EventBffHeaderNames.SupportAccessTargetTenantId, Guid.NewGuid().ToString("D"));
        request.Headers.Add(EventBffHeaderNames.SupportAccessMode, "Write");
        request.Headers.Add(FutureSupportHeaderName, "tenant-admin");
    }

    private static async Task AssertNoSupportAccessHeaders(HttpRequestMessage request)
    {
        foreach (var header in request.Headers)
        {
            await Assert.That(EventBffHeaderNames.IsSupportAccessHeader(header.Key)).IsFalse();
        }
    }

    private sealed class TestDistributedCache : IDistributedCache
    {
        private readonly Dictionary<string, byte[]> _entries = [];

        public byte[]? Get(string key)
        {
            return _entries.GetValueOrDefault(key);
        }

        public Task<byte[]?> GetAsync(
            string key,
            CancellationToken token = default)
        {
            return Task.FromResult(Get(key));
        }

        public void Set(
            string key,
            byte[] value,
            DistributedCacheEntryOptions options)
        {
            _entries[key] = value;
        }

        public Task SetAsync(
            string key,
            byte[] value,
            DistributedCacheEntryOptions options,
            CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }

        public void Refresh(string key)
        {
        }

        public Task RefreshAsync(
            string key,
            CancellationToken token = default)
        {
            Refresh(key);
            return Task.CompletedTask;
        }

        public void Remove(string key)
        {
            _ = _entries.Remove(key);
        }

        public Task RemoveAsync(
            string key,
            CancellationToken token = default)
        {
            Remove(key);
            return Task.CompletedTask;
        }
    }
}
