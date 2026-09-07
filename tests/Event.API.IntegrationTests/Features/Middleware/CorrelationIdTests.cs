using System.Net;
using Event.Api.IntegrationTests.Fixtures;

namespace Event.Api.IntegrationTests.Features.Middleware;

[NotInParallel("ApiTestFixture")]
[ClassDataSource<ApiTestFixture>(Shared = SharedType.PerAssembly)]
public class CorrelationIdTests
{
    private readonly ApiTestFixture _fixture;

    public CorrelationIdTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Test]
    public async Task Request_WithoutCorrelationId_ShouldGenerateAndReturnOne()
    {
        var response = await _fixture.Client.GetAsync("/api/category");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.Contains("X-Correlation-ID")).IsTrue();

        var correlationId = response.Headers.GetValues("X-Correlation-ID").First();
        await Assert.That(correlationId).IsNotNull();
        await Assert.That(correlationId).IsNotEmpty();
    }

    [Test]
    public async Task Request_WithCorrelationId_ShouldEchoItBack()
    {
        var expectedId = Guid.NewGuid().ToString();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/category");
        request.Headers.Add("X-Correlation-ID", expectedId);

        var response = await _fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.Contains("X-Correlation-ID")).IsTrue();

        var returnedId = response.Headers.GetValues("X-Correlation-ID").First();
        await Assert.That(returnedId).IsEqualTo(expectedId);
    }

    [Test]
    public async Task Request_WithXRequestId_ShouldUseAsCorrelationId()
    {
        var expectedId = Guid.NewGuid().ToString();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/category");
        request.Headers.Add("X-Request-ID", expectedId);

        var response = await _fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.Contains("X-Correlation-ID")).IsTrue();

        var returnedId = response.Headers.GetValues("X-Correlation-ID").First();
        await Assert.That(returnedId).IsEqualTo(expectedId);
    }

    [Test]
    public async Task Request_WithUnsafeCorrelationId_ShouldGenerateSafeId()
    {
        var unsafeId = new string('a', 129);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/category");
        request.Headers.TryAddWithoutValidation("X-Correlation-ID", unsafeId);

        var response = await _fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var returnedId = response.Headers.GetValues("X-Correlation-ID").First();
        await Assert.That(returnedId).IsNotEqualTo(unsafeId);
        await Assert.That(returnedId).IsNotNull();
        await Assert.That(returnedId).IsNotEmpty();
    }

    [Test]
    public async Task Request_WithUnsafeCorrelationIdAndSafeRequestId_ShouldUseRequestId()
    {
        var expectedId = Guid.NewGuid().ToString("N");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/category");
        request.Headers.TryAddWithoutValidation("X-Correlation-ID", new string('a', 129));
        request.Headers.Add("X-Request-ID", expectedId);

        var response = await _fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var returnedId = response.Headers.GetValues("X-Correlation-ID").First();
        await Assert.That(returnedId).IsEqualTo(expectedId);
    }

    [Test]
    public async Task MultipleRequests_WithoutCorrelationId_ShouldGenerateUniqueIds()
    {
        var response1 = await _fixture.Client.GetAsync("/api/category");
        var response2 = await _fixture.Client.GetAsync("/api/tag");

        var id1 = response1.Headers.GetValues("X-Correlation-ID").First();
        var id2 = response2.Headers.GetValues("X-Correlation-ID").First();

        await Assert.That(id1).IsNotEqualTo(id2);
    }
}
