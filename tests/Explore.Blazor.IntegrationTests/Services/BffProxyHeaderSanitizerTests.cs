using Event.Web.BffHosting.Security;

namespace Explore.Blazor.IntegrationTests.Services;

public class BffProxyHeaderSanitizerTests
{
    [Test]
    public async Task RemoveBrowserControlledHeaders_StripsBrowserCredentialAndTenantHeaders()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/api/events");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "browser-token");
        request.Headers.Add("Proxy-Authorization", "Bearer proxy-token");
        request.Headers.Add("Cookie", "bff=session; setup-secret=secret");
        request.Headers.Add("X-Setup-Secret", "browser-secret");
        request.Headers.Add(EventBffHeaderNames.ApiKey, "browser-api-key");
        request.Headers.Add("X-Control-Plane-Key", "browser-control-plane-key");
        request.Headers.Add("Access-Token", "access");
        request.Headers.Add("Refresh-Token", "refresh");
        request.Headers.Add("Identity-Token", "identity");
        request.Headers.Add("Id-Token", "id");
        request.Headers.Add("X-Access-Token", "x-access");
        request.Headers.Add("X-Refresh-Token", "x-refresh");
        request.Headers.Add("X-Identity-Token", "x-identity");
        request.Headers.Add("X-Id-Token", "x-id");
        request.Headers.Add("X-Auth-Token", "x-auth");
        request.Headers.Add(EventBffHeaderNames.AtprotoBootstrapAssertion, "bootstrap-token");
        request.Headers.Add(EventBffHeaderNames.AtprotoSessionBridgeAssertion, "session-bridge-token");
        request.Headers.Add(EventBffHeaderNames.TenantId, Guid.NewGuid().ToString());
        request.Headers.Add(EventBffHeaderNames.TenantSlug, "attacker-tenant");
        request.Headers.Add(EventBffHeaderNames.SupportAccessSessionId, Guid.NewGuid().ToString());
        request.Headers.Add(EventBffHeaderNames.SupportAccessTargetTenantId, Guid.NewGuid().ToString());
        request.Headers.Add(EventBffHeaderNames.SupportAccessMode, "Write");
        request.Headers.Add("X-Support-Access-Future-Scope", "tenant-admin");

        BffProxyHeaderSanitizer.RemoveBrowserControlledHeaders(request);

        await Assert.That(request.Headers.Authorization).IsNull();
        await Assert.That(request.Headers.Contains("Proxy-Authorization")).IsFalse();
        await Assert.That(request.Headers.Contains("Cookie")).IsFalse();
        await Assert.That(request.Headers.Contains("X-Setup-Secret")).IsFalse();
        await Assert.That(request.Headers.Contains(EventBffHeaderNames.ApiKey)).IsFalse();
        await Assert.That(request.Headers.Contains("X-Control-Plane-Key")).IsFalse();
        await Assert.That(request.Headers.Contains("Access-Token")).IsFalse();
        await Assert.That(request.Headers.Contains("Refresh-Token")).IsFalse();
        await Assert.That(request.Headers.Contains("Identity-Token")).IsFalse();
        await Assert.That(request.Headers.Contains("Id-Token")).IsFalse();
        await Assert.That(request.Headers.Contains("X-Access-Token")).IsFalse();
        await Assert.That(request.Headers.Contains("X-Refresh-Token")).IsFalse();
        await Assert.That(request.Headers.Contains("X-Identity-Token")).IsFalse();
        await Assert.That(request.Headers.Contains("X-Id-Token")).IsFalse();
        await Assert.That(request.Headers.Contains("X-Auth-Token")).IsFalse();
        await Assert.That(request.Headers.Contains(EventBffHeaderNames.AtprotoBootstrapAssertion)).IsFalse();
        await Assert.That(request.Headers.Contains(EventBffHeaderNames.AtprotoSessionBridgeAssertion)).IsFalse();
        await Assert.That(request.Headers.Contains(EventBffHeaderNames.TenantId)).IsFalse();
        await Assert.That(request.Headers.Contains(EventBffHeaderNames.TenantSlug)).IsFalse();
        await Assert.That(request.Headers.Contains(EventBffHeaderNames.SupportAccessSessionId)).IsFalse();
        await Assert.That(request.Headers.Contains(EventBffHeaderNames.SupportAccessTargetTenantId)).IsFalse();
        await Assert.That(request.Headers.Contains(EventBffHeaderNames.SupportAccessMode)).IsFalse();
        await Assert.That(request.Headers.Contains("X-Support-Access-Future-Scope")).IsFalse();
    }

    [Test]
    public async Task RemoveBrowserControlledHeaders_PreservesNonCredentialRequestMetadata()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/api/events");
        request.Headers.Accept.ParseAdd("application/hal+json");
        request.Headers.Add("X-Correlation-ID", "correlation-123");
        request.Headers.Add("X-Request-ID", "request-123");
        request.Headers.Add("Accept-Language", "en-US");

        BffProxyHeaderSanitizer.RemoveBrowserControlledHeaders(request);

        await Assert.That(request.Headers.Accept.Single().MediaType).IsEqualTo("application/hal+json");
        await Assert.That(request.Headers.GetValues("X-Correlation-ID").Single()).IsEqualTo("correlation-123");
        await Assert.That(request.Headers.GetValues("X-Request-ID").Single()).IsEqualTo("request-123");
        await Assert.That(request.Headers.GetValues("Accept-Language").Single()).IsEqualTo("en-US");
    }

    [Test]
    public async Task RemoveBrowserControlledHeaders_StripsUnsafeCorrelationMetadata()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/api/events");
        _ = request.Headers.TryAddWithoutValidation("X-Correlation-ID", new string('a', 129));
        _ = request.Headers.TryAddWithoutValidation("X-Request-ID", "request\ninjection");

        BffProxyHeaderSanitizer.RemoveBrowserControlledHeaders(request);

        await Assert.That(request.Headers.Contains("X-Correlation-ID")).IsFalse();
        await Assert.That(request.Headers.Contains("X-Request-ID")).IsFalse();
    }
}
