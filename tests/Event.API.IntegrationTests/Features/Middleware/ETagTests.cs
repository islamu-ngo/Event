using System.Net;
using Event.Api.IntegrationTests.Fixtures;

namespace Event.Api.IntegrationTests.Features.Middleware;

[NotInParallel("ApiTestFixture")]
[ClassDataSource<ApiTestFixture>(Shared = SharedType.PerAssembly)]
public class ETagTests
{
    private readonly ApiTestFixture _fixture;

    public ETagTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Test]
    public async Task GetRequest_ShouldInclude_ETagHeader()
    {
        var response = await _fixture.Client.GetAsync("/api/category");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.ETag).IsNotNull();
        await Assert.That(response.Headers.ETag!.Tag).IsNotEmpty();
    }

    [Test]
    public async Task GetRequest_ETag_ShouldBeWeakValidator()
    {
        var response = await _fixture.Client.GetAsync("/api/category");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.ETag).IsNotNull();
        await Assert.That(response.Headers.ETag!.IsWeak).IsTrue();
    }

    [Test]
    public async Task GetRequest_WithMatchingIfNoneMatch_ShouldReturn304()
    {
        // First request to get the ETag
        var firstResponse = await _fixture.Client.GetAsync("/api/category");
        await Assert.That(firstResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var etag = firstResponse.Headers.ETag;
        await Assert.That(etag).IsNotNull();

        // Second request with If-None-Match
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/category");
        request.Headers.IfNoneMatch.Add(etag!);

        var secondResponse = await _fixture.Client.SendAsync(request);
        await Assert.That(secondResponse.StatusCode).IsEqualTo(HttpStatusCode.NotModified);
    }

    [Test]
    public async Task GetRequest_WithNonMatchingIfNoneMatch_ShouldReturn200()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/category");
        request.Headers.TryAddWithoutValidation("If-None-Match", "W/\"nonexistent\"");

        var response = await _fixture.Client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.ETag).IsNotNull();
    }

    [Test]
    public async Task SameEndpoint_SameData_ShouldReturn_SameETag()
    {
        var response1 = await _fixture.Client.GetAsync("/api/category");
        var response2 = await _fixture.Client.GetAsync("/api/category");

        await Assert.That(response1.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response2.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var etag1 = response1.Headers.ETag?.Tag;
        var etag2 = response2.Headers.ETag?.Tag;

        await Assert.That(etag1).IsEqualTo(etag2);
    }

    [Test]
    public async Task DifferentEndpoints_ShouldInclude_ETagHeaders()
    {
        var response1 = await _fixture.Client.GetAsync("/api/category");
        var response2 = await _fixture.Client.GetAsync("/api/tag");

        await Assert.That(response1.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response2.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response1.Headers.ETag).IsNotNull();
        await Assert.That(response2.Headers.ETag).IsNotNull();

        var etag1 = response1.Headers.ETag?.Tag;
        var etag2 = response2.Headers.ETag?.Tag;

        await Assert.That(etag1).IsNotEmpty();
        await Assert.That(etag2).IsNotEmpty();
    }
}
