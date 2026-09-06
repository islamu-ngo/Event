// ABOUTME: Guards malformed ATProto handoff input before any backend lookup occurs.
// ABOUTME: Leaves persisted handoff and protected-cookie assertions to real API/PostgreSQL endpoint tests.

using System.Net;
using Explore.Blazor.IntegrationTests.Fixtures;

namespace Explore.Blazor.IntegrationTests.Endpoints;

public sealed class AtprotoTenantHandoffTests
{
    [Test]
    public async Task MalformedHandoffNeverReflectsCodeOrCredentialMaterial()
    {
        await using var factory = new BlazorBffWebApplicationFactory();
        using var client = factory.CreateClient(new()
        {
            AllowAutoRedirect = false, BaseAddress = new("https://tenant.example.com"), HandleCookies = false
        });
        const string supplied = "access_token=browser-secret";
        using var response = await client.GetAsync($"/auth/atproto/handoff?code={Uri.EscapeDataString(supplied)}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(response.Headers.Location?.OriginalString).IsEqualTo("/login?provider=atproto&challengeError=1");
        await Assert.That(response.Headers.Location!.OriginalString).DoesNotContain(supplied);
        await Assert.That(response.Headers.Location.OriginalString).DoesNotContain("browser-secret");
        if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
            await Assert.That(cookies).DoesNotContain(value => value.StartsWith(".AspNetCore.Cookies=", StringComparison.Ordinal));
    }
}
