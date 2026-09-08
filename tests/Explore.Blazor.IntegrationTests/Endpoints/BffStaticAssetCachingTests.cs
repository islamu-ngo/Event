using System.Net;
using Explore.Blazor.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Explore.Blazor.IntegrationTests.Endpoints;

public sealed class BffStaticAssetCachingTests : IAsyncDisposable
{
    private readonly BlazorBffWebApplicationFactory _factory = new();
    private readonly HttpClient _client;

    public BffStaticAssetCachingTests()
    {
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });
    }

    [Test]
    public async Task StaticAsset_Get_DoesNotIssueXsrfCookieOrDisableStorage()
    {
        using var response = await _client.GetAsync("/favicon.ico");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.TryGetValues("Set-Cookie", out var setCookies)).IsFalse();
        await Assert.That((response.Headers.CacheControl?.NoStore ?? false)).IsFalse();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }
}
