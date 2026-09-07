using System.Net;
using System.Net.Http.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.DTOs.Location;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features;

[ClassDataSource<ContractApiFixture>(Shared = SharedType.PerAssembly)]
public class LocationControllerTests
{
    private readonly ContractApiFixture _fixture;
    private const string BaseUrl = "/api/location";

    public LocationControllerTests(ContractApiFixture fixture)
    {
        _fixture = fixture;
    }

    #region GET Endpoints

    [Test]
    public async Task GetAll_ShouldReturnOk_WithPaginatedResult()
    {
        // Act
        using var request = _fixture.CreateAuthenticatedRequest(HttpMethod.Get, BaseUrl);
        var response = await _fixture.Client.SendAsync(request);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        await Assert.That(content).Contains("items");
    }

    [Test]
    [Arguments(1, 10)]
    [Arguments(1, 20)]
    [Arguments(2, 5)]
    public async Task GetAll_WithPaginationParams_ShouldReturnPaginatedResult(int pageNumber, int pageSize)
    {
        // Act
        using var request = _fixture.CreateAuthenticatedRequest(
            HttpMethod.Get,
            $"{BaseUrl}?pageNumber={pageNumber}&pageSize={pageSize}");
        var response = await _fixture.Client.SendAsync(request);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task GetById_WithRandomId_ShouldReturnNotFound()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        using var request = _fixture.CreateAuthenticatedRequest(HttpMethod.Get, $"{BaseUrl}/{id}");
        var response = await _fixture.Client.SendAsync(request);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task GetById_WithInvalidGuidFormat_ShouldReturnNotFound()
    {
        // Act - ASP.NET Core route constraints reject non-GUID strings with 404 (no route match)
        var response = await _fixture.Client.GetAsync($"{BaseUrl}/not-a-guid");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    #endregion

    #region POST Endpoints

    [Test]
    public async Task Create_WithoutAuth_ShouldReturnUnauthorized()
    {
        // Arrange
        var createDto = new CreateLocationDto
        {
            FullName = "Test Location",
            Address = "123 Test Street",
            Postcode = "1000",
            Country = "Belgium",
            City = "Brussels"
        };

        // Act
        var response = await _fixture.Client.PostAsJsonAsync(BaseUrl, createDto);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region PATCH Endpoints

    [Test]
    public async Task Update_WithoutAuth_ShouldReturnUnauthorized()
    {
        var id = Guid.NewGuid();
        var updateDto = new UpdateLocationDto
        {
            FullName = new UpdateLocationFullNameDto { Value = "Updated Location" }
        };

        var response = await _fixture.Client.PatchAsJsonAsync($"{BaseUrl}/{id}", updateDto);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task UpdatePut_WhenUsingOldRoute_ShouldReturnMethodNotAllowed()
    {
        var id = Guid.NewGuid();
        var updateDto = new UpdateLocationDto
        {
            FullName = new UpdateLocationFullNameDto { Value = "Updated Location" }
        };

        var response = await _fixture.Client.PutAsJsonAsync($"{BaseUrl}/{id}", updateDto);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.MethodNotAllowed);
    }

    [Test]
    public async Task UpdatePatch_WhenAuthenticatedWithoutIfMatch_ShouldReturnBadRequest()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();
        var locationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var updateDto = new UpdateLocationDto
        {
            FullName = new UpdateLocationFullNameDto { Value = "Updated Location" }
        };
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"{BaseUrl}/{locationId}")
        {
            Content = JsonContent.Create(updateDto)
        };
        request.Headers.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(userId));

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    #endregion

    #region DELETE Endpoints

    [Test]
    public async Task Delete_WithoutAuth_ShouldReturnUnauthorized()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        var response = await _fixture.Client.DeleteAsync($"{BaseUrl}/{id}");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    #endregion
}
