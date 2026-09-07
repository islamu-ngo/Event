using System.Net;
using System.Net.Http.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.DTOs.Category;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
[ClassDataSource<ApiTestFixture>(Shared = SharedType.PerAssembly)]
public class CategoryControllerTests
{
    private readonly ApiTestFixture _fixture;
    private const string BaseUrl = "/api/category";

    public CategoryControllerTests(ApiTestFixture fixture)
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
    public async Task GetById_WithRandomId_ShouldReturnNotFound()
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

    #endregion

    #region POST Endpoints

    [Test]
    public async Task Create_WithoutAuth_ShouldReturnUnauthorized()
    {
        // Arrange
        var createDto = new CreateCategoryDto
        {
            MasterCode = "TEST_CAT",
            FullName = "Test Category"
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
        // Arrange
        var id = Guid.NewGuid();
        var updateDto = new UpdateCategoryDto
        {
            FullName = new UpdateCategoryFullNameDto
            {
                Value = "Updated Category"
            }
        };

        // Act
        var response = await _fixture.Client.PatchAsJsonAsync($"{BaseUrl}/{id}", updateDto);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task UpdatePut_WhenUsingOldRoute_ShouldReturnMethodNotAllowed()
    {
        var id = Guid.NewGuid();
        var updateDto = new UpdateCategoryDto
        {
            FullName = new UpdateCategoryFullNameDto
            {
                Value = "Updated Category"
            }
        };

        var response = await _fixture.Client.PutAsJsonAsync($"{BaseUrl}/{id}", updateDto);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.MethodNotAllowed);
    }

    [Test]
    public async Task UpdatePatch_WhenAuthenticatedWithoutIfMatch_ShouldReturnBadRequest()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();
        var categoryId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var updateDto = new UpdateCategoryDto
        {
            FullName = new UpdateCategoryFullNameDto
            {
                Value = "Updated Category"
            }
        };
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"{BaseUrl}/{categoryId}")
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
