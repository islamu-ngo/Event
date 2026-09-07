using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.DTOs.Organization;
using Explore.Application.Responses;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
[ClassDataSource<ApiTestFixture>(Shared = SharedType.PerAssembly)]
public class OrganizationControllerTests
{
    private readonly ApiTestFixture _fixture;
    private const string BaseUrl = "/api/organization";

    public OrganizationControllerTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    #region GET Endpoints

    [Test]
    public async Task GetAll_ShouldReturnOk_WithPaginatedResult()
    {
        // Act
        var response = await _fixture.Client.GetAsync(BaseUrl);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        await Assert.That(content).Contains("items");
        await Assert.That(content).Contains("totalCount");
    }

    [Test]
    [Arguments(1, 10)]
    [Arguments(1, 20)]
    [Arguments(2, 5)]
    public async Task GetAll_WithPaginationParams_ShouldReturnPaginatedResult(int pageNumber, int pageSize)
    {
        // Act
        var response = await _fixture.Client.GetAsync($"{BaseUrl}?pageNumber={pageNumber}&pageSize={pageSize}");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task GetById_WithValidId_ShouldReturnNotFound()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        var response = await _fixture.Client.GetAsync($"{BaseUrl}/{id}");

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

    [Test]
    public async Task GetMyOrganizations_WithoutAuth_ShouldReturnUnauthorized()
    {
        // Act
        var response = await _fixture.Client.GetAsync($"{BaseUrl}/my");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region POST Endpoints

    [Test]
    public async Task Create_WithoutAuth_ShouldReturnUnauthorized()
    {
        // Arrange
        var createDto = new CreateOrganizationDto
        {
            FullName = "Test Organization",
            Email = "test@example.com",
            Country = "Belgium",
            City = "Brussels",
            Address = "123 Test Street",
            Postcode = 1000
        };

        // Act
        var response = await _fixture.Client.PostAsJsonAsync(BaseUrl, createDto);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Update Endpoints

    [Test]
    public async Task UpdatePatch_WithoutAuth_ShouldReturnUnauthorized()
    {
        // Arrange
        var id = Guid.NewGuid();
        var updateDto = new UpdateOrganizationDto
        {
            FullName = new UpdateOrganizationFullNameDto { Value = "Updated Organization" }
        };

        // Act
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"{BaseUrl}/{id}")
        {
            Content = JsonContent.Create(updateDto)
        };
        request.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{Guid.NewGuid():D}\""));
        var response = await _fixture.Client.SendAsync(request);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task UpdatePut_WithoutAuth_ShouldReturnMethodNotAllowed()
    {
        // Arrange
        var id = Guid.NewGuid();
        var updateDto = new UpdateOrganizationDto
        {
            FullName = new UpdateOrganizationFullNameDto { Value = "Updated Organization" }
        };

        // Act
        var response = await _fixture.Client.PutAsJsonAsync($"{BaseUrl}/{id}", updateDto);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.MethodNotAllowed);
    }

    [Test]
    public async Task UpdateStatusType_WithoutAuth_ShouldReturnUnauthorized()
    {
        // Arrange
        var id = Guid.NewGuid();
        var updateDto = new UpdateOrganizationApprovalStatusDto
        {
            ApprovalStatusId = 1
        };

        // Act
        var response = await _fixture.Client.PutAsJsonAsync($"{BaseUrl}/{id}/approval-status", updateDto);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    #endregion
}
