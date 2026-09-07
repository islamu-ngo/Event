// Verifies HAL conversion and resilient fallback contracts for all guarded error paths.

namespace Explore.Blazor.Client.Tests.Services;

/// <summary>
/// Unit tests for TagService.
/// </summary>
/// <remarks>
/// These tests verify:
/// - GetTags/GetAllTags return HAL items or empty on error
/// - GetTagById returns DTO on success and null for not-found/error
/// - Create/Update return failure response on API exceptions
/// - Delete returns true on success and false on API exceptions
/// - Neutralized methods return expected placeholders
/// </remarks>
public class TagServiceCrudErrorHandlingTests
{
    private readonly ITagClient _apiClient;
    private readonly Microsoft.Extensions.Logging.ILogger<TagService> _logger;
    private readonly TagService _service;

    public TagServiceCrudErrorHandlingTests()
    {
        _apiClient = Substitute.For<ITagClient>();
        _logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<TagService>>();
        _service = new TagService(_apiClient, Substitute.For<ITagTypeClient>(), _logger);
    }

    // ========== GetTagsAsync ==========

    #region GetTagsAsync Tests

    [Test]
    public async Task GetTagsAsync_ReturnsCollection_WhenApiSucceeds()
    {
        // Arrange
        var tags = new List<TagListDto>
        {
            new() { FullName = "Family", MasterCode = "FAMILY" },
            new() { FullName = "Charity", MasterCode = "CHARITY" }
        };

        var halResponse = new HalCollectionResourceOfTagListDto
        {
            _embedded = new HalCollectionEmbeddedOfTagListDto
            {
                Items = tags.Select(ToHalResource).ToList()
            }
        };

        _apiClient.GetTagsAsync(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(halResponse);
        _apiClient.GetTagsAsync(Arg.Any<int?>(), Arg.Any<int?>()).Returns(halResponse);

        // Act
        var result = await _service.GetTagsAsync();

        // Assert
        await Assert.That(result.Count).IsEqualTo(2);
    }

    [Test]
    public async Task GetTagsAsync_ReturnsEmptyList_WhenApiThrows()
    {
        // Arrange
        var ex = new ApiException("Server Error", 500, null, null, null);
        _apiClient.GetTagsAsync(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).ThrowsAsync(ex);
        _apiClient.GetTagsAsync(Arg.Any<int?>(), Arg.Any<int?>()).ThrowsAsync(ex);

        // Act
        var result = await _service.GetTagsAsync();

        // Assert
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task GetAllTagsAsync_ReturnsCollection_WhenApiSucceeds()
    {
        // Arrange
        var tags = new List<TagListDto> { new() { FullName = "Youth", MasterCode = "YOUTH" } };
        var halResponse = new HalCollectionResourceOfTagListDto
        {
            _embedded = new HalCollectionEmbeddedOfTagListDto { Items = tags.Select(ToHalResource).ToList() }
        };
        _apiClient.GetTagsAsync(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(halResponse);
        _apiClient.GetTagsAsync(Arg.Any<int?>(), Arg.Any<int?>()).Returns(halResponse);

        // Act
        var result = await _service.GetAllTagsAsync();

        // Assert
        await Assert.That(result.Count).IsEqualTo(1);
    }

    #endregion

    // ========== GetTagByIdAsync ==========

    #region GetTagByIdAsync Tests

    [Test]
    public async Task GetTagByIdAsync_ReturnsDto_WhenApiSucceeds()
    {
        // Arrange
        var id = Guid.NewGuid();
        var hal = new HalResourceOfTagDto { Id = id, FullName = "Education" };
        _apiClient.GetTagByIdAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(hal);
        _apiClient.GetTagByIdAsync(Arg.Any<Guid>()).Returns(hal);

        // Act
        var result = await _service.GetTagByIdAsync(id);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Id).IsEqualTo(id);
    }

    [Test]
    public async Task GetTagByIdAsync_ReturnsNull_WhenNotFound()
    {
        // Arrange
        var id = Guid.NewGuid();
        var ex = new ApiException("Not Found", 404, null, null, null);
        _apiClient.GetTagByIdAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).ThrowsAsync(ex);
        _apiClient.GetTagByIdAsync(Arg.Any<Guid>()).ThrowsAsync(ex);

        // Act
        var result = await _service.GetTagByIdAsync(id);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetTagByIdAsync_ReturnsNull_WhenApiThrows()
    {
        // Arrange
        var id = Guid.NewGuid();
        var ex = new ApiException("Server Error", 500, null, null, null);
        _apiClient.GetTagByIdAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).ThrowsAsync(ex);
        _apiClient.GetTagByIdAsync(Arg.Any<Guid>()).ThrowsAsync(ex);

        // Act
        var result = await _service.GetTagByIdAsync(id);

        // Assert
        await Assert.That(result).IsNull();
    }

    #endregion

    // ========== CreateTagAsync ==========

    #region CreateTagAsync Tests

    [Test]
    public async Task CreateTagAsync_ReturnsFailureResponse_WhenApiThrows()
    {
        // Arrange
        var dto = new CreateTagDto { FullName = "NewTag", MasterCode = "NEW" };
        var ex = new ApiException("Bad Request", 400, "validation error", null, null);
        _apiClient.CreateTagAsync(Arg.Any<CreateTagDto>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).ThrowsAsync(ex);
        _apiClient.CreateTagAsync(Arg.Any<CreateTagDto>()).ThrowsAsync(ex);

        // Act
        var result = await _service.CreateTagAsync(dto);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Success).IsFalse();
    }

    #endregion

    // ========== UpdateTagAsync ==========

    #region UpdateTagAsync Tests

    [Test]
    public async Task UpdateTagAsync_ReturnsFailureResponse_WhenApiThrows()
    {
        // Arrange
        var id = Guid.NewGuid();
        var dto = new UpdateTagDto
        {
            FullName = new() { Value = "Updated" },
            MasterCode = new() { Value = "UPD" }
        };
        var ex = new ApiException("Bad Request", 400, "validation error", null, null);
        _apiClient.UpdateTagAsync(Arg.Any<Guid>(), Arg.Any<UpdateTagDto>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).ThrowsAsync(ex);
        _apiClient.UpdateTagAsync(Arg.Any<Guid>(), Arg.Any<UpdateTagDto>()).ThrowsAsync(ex);

        // Act
        var result = await _service.UpdateTagAsync(id, dto);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Success).IsFalse();
    }

    #endregion

    // ========== DeleteTagAsync ==========

    #region DeleteTagAsync Tests

    [Test]
    public async Task DeleteTagAsync_ReturnsTrue_WhenApiSucceeds()
    {
        // Arrange
        var id = Guid.NewGuid();
        _apiClient.DeleteTagAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _apiClient.DeleteTagAsync(Arg.Any<Guid>()).Returns(Task.CompletedTask);

        // Act
        var result = await _service.DeleteTagAsync(id);

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task DeleteTagAsync_ReturnsFalse_WhenApiThrows()
    {
        // Arrange
        var id = Guid.NewGuid();
        var ex = new ApiException("Forbidden", 403, null, null, null);
        _apiClient.DeleteTagAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).ThrowsAsync(ex);
        _apiClient.DeleteTagAsync(Arg.Any<Guid>()).ThrowsAsync(ex);

        // Act
        var result = await _service.DeleteTagAsync(id);

        // Assert
        await Assert.That(result).IsFalse();
    }

    #endregion

    private static HalResourceOfTagListDto ToHalResource(TagListDto item)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(item);
        return System.Text.Json.JsonSerializer.Deserialize<HalResourceOfTagListDto>(json)
               ?? new HalResourceOfTagListDto();
    }

}
