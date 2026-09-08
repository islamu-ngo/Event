using System.Net;
using Event.Api.IntegrationTests.Fixtures;

namespace Event.Api.IntegrationTests.Features.Middleware;

[NotInParallel("ApiTestFixture")]
[ClassDataSource<ApiTestFixture>(Shared = SharedType.PerAssembly)]
public class SecurityHeadersTests
{
    private readonly ApiTestFixture _fixture;

    public SecurityHeadersTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Test]
    public async Task GetRequest_ShouldInclude_XContentTypeOptions()
    {
        var response = await _fixture.Client.GetAsync("/api/category");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.Contains("X-Content-Type-Options")).IsTrue();

        var value = response.Headers.GetValues("X-Content-Type-Options").First();
        await Assert.That(value).IsEqualTo("nosniff");
    }

    [Test]
    public async Task GetRequest_ShouldInclude_XFrameOptions()
    {
        var response = await _fixture.Client.GetAsync("/api/category");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.Contains("X-Frame-Options")).IsTrue();

        var value = response.Headers.GetValues("X-Frame-Options").First();
        await Assert.That(value).IsEqualTo("DENY");
    }

    [Test]
    public async Task GetRequest_ShouldInclude_ReferrerPolicy()
    {
        var response = await _fixture.Client.GetAsync("/api/category");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.Contains("Referrer-Policy")).IsTrue();

        var value = response.Headers.GetValues("Referrer-Policy").First();
        await Assert.That(value).IsEqualTo("strict-origin-when-cross-origin");
    }

    [Test]
    public async Task GetRequest_ShouldInclude_PermissionsPolicy()
    {
        var response = await _fixture.Client.GetAsync("/api/category");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.Contains("Permissions-Policy")).IsTrue();
    }

    [Test]
    public async Task GetRequest_ShouldInclude_ContentSecurityPolicy()
    {
        var response = await _fixture.Client.GetAsync("/api/category");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.Contains("Content-Security-Policy")).IsTrue();
    }

    [Test]
    public async Task GetRequest_ShouldNotInclude_NoCacheHeaders()
    {
        var response = await _fixture.Client.GetAsync("/api/category");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        // GET requests should NOT have no-store cache directive from security middleware
        var hasPragma = response.Headers.Contains("Pragma");
        if (hasPragma)
        {
            var pragma = response.Headers.GetValues("Pragma").First();
            await Assert.That(pragma).IsNotEqualTo("no-cache");
        }
    }

    [Test]
    [Arguments("/api/category")]
    [Arguments("/api/tag")]
    [Arguments("/api/organization")]
    public async Task AllGetEndpoints_ShouldInclude_SecurityHeaders(string endpoint)
    {
        var response = await _fixture.Client.GetAsync(endpoint);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.Contains("X-Content-Type-Options")).IsTrue();
        await Assert.That(response.Headers.Contains("X-Frame-Options")).IsTrue();
        await Assert.That(response.Headers.Contains("Referrer-Policy")).IsTrue();
    }
}
