// Verifies HAL conversion, pagination usage, and error handling contracts for read and write operations.

using Explore.Blazor.Client.Constants;
using Explore.Blazor.Client.Helpers;

namespace Explore.Blazor.Client.Tests.Services;

/// <summary>
/// Unit tests for CategoryService.
/// </summary>
/// <remarks>
/// These tests verify:
/// - HAL collection/resource conversion for categories
/// - Read methods returning empty/null on API failures
/// - Create/Update methods returning failure BaseCommandResponseOfGuid on API failures
/// - Alias behavior for GetAllCategoriesAsync
/// </remarks>
public class CategoryServiceTests
{
    private readonly ICategoryClient _apiClient;
    private readonly ILogger<CategoryService> _logger;
    private readonly CategoryService _service;

    public CategoryServiceTests()
    {
        _apiClient = Substitute.For<ICategoryClient>();
        _logger = Substitute.For<ILogger<CategoryService>>();
        _service = new CategoryService(_apiClient, Substitute.For<ICategoryTypeClient>(), _logger);
    }

    // ========== GetCategoriesAsync ==========

    #region GetCategoriesAsync Tests

    [Test]
    public async Task GetCategoriesAsync_ReturnsCategories_WhenApiSucceeds()
    {
        // Arrange
        var categories = ComponentDataBuilder.CategoryListDto.Generate(3);
        var halResponse = CreateCategoryCollectionResponse(categories);

        _apiClient.GetCategoriesAsync(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(halResponse);

        // Act
        var result = await _service.GetCategoriesAsync();

        // Assert
        await Assert.That(result.Count).IsEqualTo(3);
        await Assert.That(result.First().FullName).IsEqualTo(categories.First().FullName);
    }

    [Test]
    public async Task GetCategoriesAsync_ReturnsEmptyList_WhenApiReturnsNull()
    {
        // Arrange
        _apiClient.GetCategoriesAsync(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((HalCollectionResourceOfCategoryListDto?)null);

        // Act
        var result = await _service.GetCategoriesAsync();

        // Assert
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task GetCategoriesAsync_ReturnsEmptyList_WhenApiThrows()
    {
        // Arrange
        _apiClient.GetCategoriesAsync(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException("Server Error", 500, null, null, null));

        // Act
        var result = await _service.GetCategoriesAsync();

        // Assert
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task GetCategoriesAsync_CallsApiWithCorrectPagination()
    {
        // Arrange
        var halResponse = CreateCategoryCollectionResponse(new List<CategoryListDto>());
        _apiClient.GetCategoriesAsync(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(halResponse);

        // Act
        await _service.GetCategoriesAsync();

        // Assert
        await _apiClient.Received(1).GetCategoriesAsync(
            ApiConstants.FirstPage,
            ApiConstants.DefaultPageSize,
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    #endregion

    // ========== GetAllCategoriesAsync ==========

    #region GetAllCategoriesAsync Tests

    [Test]
    public async Task GetAllCategoriesAsync_ReturnsCategories_WhenApiSucceeds()
    {
        // Arrange
        var categories = ComponentDataBuilder.CategoryListDto.Generate(2);
        var halResponse = CreateCategoryCollectionResponse(categories);

        _apiClient.GetCategoriesAsync(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(halResponse);

        // Act
        var result = await _service.GetAllCategoriesAsync();

        // Assert
        await Assert.That(result.Count).IsEqualTo(2);
    }

    #endregion

    // ========== GetCategoryByIdAsync ==========

    #region GetCategoryByIdAsync Tests

    [Test]
    public async Task GetCategoryByIdAsync_ReturnsNull_WhenApiReturnsNull()
    {
        // Arrange
        var categoryId = Guid.NewGuid();
        _apiClient.GetCategoryByIdAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((HalResourceOfCategoryDto?)null);
        _apiClient.GetCategoryByIdAsync(Arg.Any<Guid>())
            .Returns((HalResourceOfCategoryDto?)null);

        // Act
        var result = await _service.GetCategoryByIdAsync(categoryId);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetCategoryByIdAsync_ReturnsNull_WhenNotFound()
    {
        // Arrange
        var categoryId = Guid.NewGuid();
        _apiClient.GetCategoryByIdAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException("Not Found", 404, null, null, null));
        _apiClient.GetCategoryByIdAsync(Arg.Any<Guid>())
            .ThrowsAsync(new ApiException("Not Found", 404, null, null, null));

        // Act
        var result = await _service.GetCategoryByIdAsync(categoryId);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetCategoryByIdAsync_ReturnsNull_WhenApiThrows()
    {
        // Arrange
        var categoryId = Guid.NewGuid();
        _apiClient.GetCategoryByIdAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException("Server Error", 500, null, null, null));
        _apiClient.GetCategoryByIdAsync(Arg.Any<Guid>())
            .ThrowsAsync(new ApiException("Server Error", 500, null, null, null));

        // Act
        var result = await _service.GetCategoryByIdAsync(categoryId);

        // Assert
        await Assert.That(result).IsNull();
    }

    #endregion

    // ========== CreateCategoryAsync ==========

    #region CreateCategoryAsync Tests

    [Test]
    public async Task CreateCategoryAsync_ReturnsResponse_WhenApiSucceeds()
    {
        // Arrange
        var dto = new CreateCategoryDto
        {
            FullName = "New Category",
            MasterCode = "NEWCAT"
        };
        var expectedResponse = ComponentDataBuilder.SuccessResponse();

        _apiClient.CreateCategoryAsync(Arg.Any<CreateCategoryDto>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(expectedResponse);

        // Act
        var result = await _service.CreateCategoryAsync(dto);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Success).IsTrue();
    }

    [Test]
    public async Task CreateCategoryAsync_ReturnsFailureResponse_WhenApiThrows()
    {
        // Arrange
        var dto = new CreateCategoryDto
        {
            FullName = "New Category",
            MasterCode = "NEWCAT"
        };

        _apiClient.CreateCategoryAsync(Arg.Any<CreateCategoryDto>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException("Bad Request", 400, "validation error", null, null));

        // Act
        var result = await _service.CreateCategoryAsync(dto);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Success).IsFalse();
        await Assert.That(result.Message).Contains("API error");
    }

    #endregion

    // ========== UpdateCategoryAsync ==========

    #region UpdateCategoryAsync Tests

    [Test]
    public async Task UpdateCategoryAsync_ReturnsResponse_WhenApiSucceeds()
    {
        // Arrange
        var categoryId = Guid.NewGuid();
        var concurrencyStamp = Guid.NewGuid();
        var dto = new UpdateCategoryDto
        {
            FullName = new UpdateCategoryFullNameDto { Value = "Updated Category" },
            MasterCode = new UpdateCategoryMasterCodeDto { Value = "UPDCAT" }
        };
        var expectedResponse = ComponentDataBuilder.SuccessResponse(categoryId);

        _apiClient.UpdateCategoryAsync(Arg.Any<Guid>(), Arg.Any<UpdateCategoryDto>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(expectedResponse);

        // Act
        var result = await _service.UpdateCategoryAsync(categoryId, concurrencyStamp, dto);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Success).IsTrue();
        await Assert.That(result.Id).IsEqualTo(categoryId);
    }

    [Test]
    public async Task UpdateCategoryAsync_ReturnsFailureResponse_WhenApiThrows()
    {
        // Arrange
        var categoryId = Guid.NewGuid();
        var concurrencyStamp = Guid.NewGuid();
        var dto = new UpdateCategoryDto
        {
            FullName = new UpdateCategoryFullNameDto { Value = "Updated Category" },
            MasterCode = new UpdateCategoryMasterCodeDto { Value = "UPDCAT" }
        };

        _apiClient.UpdateCategoryAsync(Arg.Any<Guid>(), Arg.Any<UpdateCategoryDto>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException("Bad Request", 400, "validation error", null, null));

        // Act
        var result = await _service.UpdateCategoryAsync(categoryId, concurrencyStamp, dto);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Success).IsFalse();
        await Assert.That(result.Message).Contains("API error");
    }

    #endregion

    // ========== DeleteCategoryAsync ==========

    #region DeleteCategoryAsync Tests

    [Test]
    public async Task DeleteCategoryAsync_ReturnsTrue_WhenApiSucceeds()
    {
        // Arrange
        var categoryId = Guid.NewGuid();
        _apiClient.DeleteCategoryAsync(categoryId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.DeleteCategoryAsync(categoryId);

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task DeleteCategoryAsync_ReturnsFalse_WhenApiThrows()
    {
        // Arrange
        var categoryId = Guid.NewGuid();
        _apiClient.DeleteCategoryAsync(categoryId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException("Forbidden", 403, null, null, null));

        // Act
        var result = await _service.DeleteCategoryAsync(categoryId);

        // Assert
        await Assert.That(result).IsFalse();
    }

    #endregion

    // ========== HAL Response Helpers ==========

    #region HAL Response Helpers

    private static HalCollectionResourceOfCategoryListDto CreateCategoryCollectionResponse(
        IList<CategoryListDto> items)
    {
        return new HalCollectionResourceOfCategoryListDto
        {
            _embedded = new HalCollectionEmbeddedOfCategoryListDto
            {
                Items = items.Select(ToHalResource).ToList()
            }
        };
    }

    private static HalResourceOfCategoryListDto ToHalResource(CategoryListDto item)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(item);
        return System.Text.Json.JsonSerializer.Deserialize<HalResourceOfCategoryListDto>(json)
               ?? new HalResourceOfCategoryListDto();
    }

    #endregion
}
