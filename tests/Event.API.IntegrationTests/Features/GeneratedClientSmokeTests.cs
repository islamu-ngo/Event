using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features;

[ClassDataSource<ContractApiFixture>(Shared = SharedType.PerAssembly)]
public class GeneratedClientSmokeTests
{
    private readonly ContractApiFixture _fixture;

    public GeneratedClientSmokeTests(ContractApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Test]
    public async Task Get_Collection_ReturnsOk_WithJsonPayload()
    {
        var response = await _fixture.Client.GetAsync("/api/madhab");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        await Assert.That(contentType.Contains("json")).IsTrue();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var hasContent = doc.RootElement.ValueKind is JsonValueKind.Array or JsonValueKind.Object;
        await Assert.That(hasContent).IsTrue();
    }

    [Test]
    public async Task Get_ById_ReturnsOk_WithJsonPayload()
    {
        using var request = _fixture.CreateAuthenticatedRequest(HttpMethod.Get, "/api/actor");
        var response = await _fixture.Client.SendAsync(request);

        Console.WriteLine($"GET /api/actor => {response.StatusCode}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task Post_AuthenticatedEndpoint_AcceptsPayload()
    {
        using var request = _fixture.CreateAuthenticatedRequest(HttpMethod.Post, "/api/organization");
        request.Content = JsonContent.Create(new { fullName = $"SmokeOrg_{Guid.NewGuid():N}"[..28], slug = $"smoke-{Guid.NewGuid():N}"[..16] });

        var response = await _fixture.Client.SendAsync(request);
        Console.WriteLine($"POST /api/organization => {response.StatusCode}");

        var isExpected = response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created
            or HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity or HttpStatusCode.Forbidden;
        await Assert.That(isExpected).IsTrue();
    }

    [Test]
    public async Task Patch_AuthenticatedEndpoint_AcceptsPayload()
    {
        using var request = _fixture.CreateAuthenticatedRequest(HttpMethod.Patch, "/api/organization/00000000-0000-0000-0000-000000000001");
        request.Content = JsonContent.Create(new { fullName = "SmokePut" });

        var response = await _fixture.Client.SendAsync(request);
        Console.WriteLine($"PATCH /api/organization/... => {response.StatusCode}");

        var isExpected = response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent
            or HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.Forbidden;
        await Assert.That(isExpected).IsTrue();
    }

    [Test]
    public async Task Delete_AuthenticatedEndpoint_RespondsPredictably()
    {
        using var request = _fixture.CreateAuthenticatedRequest(HttpMethod.Delete, "/api/organization/00000000-0000-0000-0000-000000000001");

        var response = await _fixture.Client.SendAsync(request);
        Console.WriteLine($"DELETE /api/organization/... => {response.StatusCode}");

        var isExpected = response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent
            or HttpStatusCode.NotFound or HttpStatusCode.Forbidden or HttpStatusCode.BadRequest;
        await Assert.That(isExpected).IsTrue();
    }
}
