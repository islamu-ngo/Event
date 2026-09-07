using System.Net;
using System.Net.Http.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.DTOs.Tenant;
using Explore.Application.Responses;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("SingleTenantAuthenticatedApiFixture")]
[ClassDataSource<SingleTenantAuthenticatedApiTestFixture>(Shared = SharedType.PerAssembly)]
public class TenantNavigationCacheTests
{
    private readonly SingleTenantAuthenticatedApiTestFixture _fixture;

    public TenantNavigationCacheTests(SingleTenantAuthenticatedApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Test]
    public async Task GetNavigation_ThenCreate_ThenGet_ShouldReturnNewLink()
    {
        var userId = Guid.NewGuid();

        // Step 1: GET navigation — should return 200 OK (possibly empty list)
        var firstGet = await _fixture.Client.GetAsync("/api/tenant/navigation");
        await Assert.That(firstGet.StatusCode).IsEqualTo(HttpStatusCode.OK);

        // Step 2: POST new navigation link (authenticated)
        var createDto = new CreateTenantNavigationLinkDto
        {
            Label = "Cache Test Link",
            Url = "https://cache-test.example.com",
            OpenInNewTab = true
        };

        using var createRequest = _fixture.CreateAuthenticatedRequest(HttpMethod.Post, "/api/tenant/navigation", userId);
        createRequest.Content = JsonContent.Create(createDto);
        var createResponse = await _fixture.Client.SendAsync(createRequest);

        await Assert.That(createResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var createBody = await createResponse.Content.ReadFromJsonAsync<BaseCommandResponse<Guid>>();
        await Assert.That(createBody).IsNotNull();
        await Assert.That(createBody!.IsSuccess).IsTrue();

        // Step 3: GET navigation again — cache should be invalidated, new link visible
        var secondGet = await _fixture.Client.GetAsync("/api/tenant/navigation");
        await Assert.That(secondGet.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var links = await secondGet.Content.ReadFromJsonAsync<List<TenantNavigationLinkDto>>();
        await Assert.That(links).IsNotNull();

        var createdLink = links!.FirstOrDefault(l => l.Label == "Cache Test Link");
        await Assert.That(createdLink).IsNotNull();
        await Assert.That(createdLink!.Url).IsEqualTo("https://cache-test.example.com");
    }

    [Test]
    public async Task GetNavigation_WithoutAuth_ShouldReturn200()
    {
        // GET navigation is AllowAnonymous
        var response = await _fixture.Client.GetAsync("/api/tenant/navigation");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task CreateNavigation_WithoutAuth_ShouldReturnUnauthorized()
    {
        // POST navigation requires Authorize
        var createDto = new CreateTenantNavigationLinkDto
        {
            Label = "Unauth Link",
            Url = "https://unauth.example.com",
            OpenInNewTab = false
        };

        var response = await _fixture.Client.PostAsJsonAsync("/api/tenant/navigation", createDto);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task CreateNavigation_WithInvalidUrl_ShouldReturnBadRequest()
    {
        var userId = Guid.NewGuid();

        var createDto = new CreateTenantNavigationLinkDto
        {
            Label = "Bad URL Link",
            Url = "javascript:alert(1)",
            OpenInNewTab = false
        };

        using var request = _fixture.CreateAuthenticatedRequest(HttpMethod.Post, "/api/tenant/navigation", userId);
        request.Content = JsonContent.Create(createDto);
        var response = await _fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }
}
