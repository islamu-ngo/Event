// Verifies HAL conversion, fallback contracts on API failures, and write-operation response behavior.

using Explore.Blazor.Client.Constants;
using Explore.Blazor.Client.Helpers;

namespace Explore.Blazor.Client.Tests.Services;

/// <summary>
/// Unit tests for TagService.
/// </summary>
public class TagServiceTests
{
    private readonly ITagClient _apiClient;
    private readonly Microsoft.Extensions.Logging.ILogger<TagService> _logger;
    private readonly TagService _service;

    public TagServiceTests()
    {
        _apiClient = Substitute.For<ITagClient>();
        _logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<TagService>>();
        _service = new TagService(_apiClient, Substitute.For<ITagTypeClient>(), _logger);
    }

    // ========== GetTagsAsync ==========

    #region GetTagsAsync Tests

    [Test]
    public async Task GetTagsAsync_ReturnsTags_WhenApiSucceeds()
    {
        // Arrange
        var tags = new List<TagListDto> { new(), new(), new() };
        var halResponse = CreateTagCollectionResponse(tags);

        _apiClient.GetTagsAsync(ApiConstants.FirstPage, ApiConstants.DefaultPageSize, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(halResponse);

        // Act
        var result = await _service.GetTagsAsync();

        // Assert
        await Assert.That(result.Count).IsEqualTo(3);
    }

    [Test]
    public async Task GetTagsAsync_ReturnsEmptyList_WhenApiReturnsNull()
    {
        // Arrange
        _apiClient.GetTagsAsync(ApiConstants.FirstPage, ApiConstants.DefaultPageSize, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((HalCollectionResourceOfTagListDto?)null);

        // Act
        var result = await _service.GetTagsAsync();

        // Assert
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task GetTagsAsync_ReturnsEmptyList_WhenApiThrows()
    {
        // Arrange
        _apiClient.GetTagsAsync(ApiConstants.FirstPage, ApiConstants.DefaultPageSize, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException("Server Error", 500, null, null, null));

        // Act
        var result = await _service.GetTagsAsync();

        // Assert
        await Assert.That(result).IsEmpty();
    }

    #endregion

    // ========== GetAllTagsAsync ==========

    #region GetAllTagsAsync Tests

    [Test]
    public async Task GetAllTagsAsync_ReturnsTags_WhenApiSucceeds()
    {
        // Arrange
        var tags = new List<TagListDto> { new(), new() };
        var halResponse = CreateTagCollectionResponse(tags);

        _apiClient.GetTagsAsync(ApiConstants.FirstPage, ApiConstants.DefaultPageSize, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(halResponse);

        // Act
        var result = await _service.GetAllTagsAsync();

        // Assert
        await Assert.That(result.Count).IsEqualTo(2);
    }

    #endregion

    // ========== GetTagByIdAsync ==========

    #region GetTagByIdAsync Tests

    [Test]
    public async Task GetTagByIdAsync_ReturnsTag_WhenApiSucceeds()
    {
        // Arrange
        var tagId = Guid.NewGuid();
        var halResponse = new HalResourceOfTagDto
        {
            Id = tagId,
            FullName = "Education",
            MasterCode = "EDU"
        };

        _apiClient.GetTagByIdAsync(tagId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(halResponse);
        _apiClient.GetTagByIdAsync(tagId).Returns(halResponse);

        // Act
        var result = await _service.GetTagByIdAsync(tagId);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Id).IsEqualTo(tagId);
    }

    [Test]
    public async Task GetTagByIdAsync_ReturnsNull_WhenNotFound()
    {
        // Arrange
        var tagId = Guid.NewGuid();

        _apiClient.GetTagByIdAsync(tagId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException("Not Found", 404, null, null, null));
        _apiClient.GetTagByIdAsync(tagId)
            .ThrowsAsync(new ApiException("Not Found", 404, null, null, null));

        // Act
        var result = await _service.GetTagByIdAsync(tagId);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetTagByIdAsync_ReturnsNull_WhenApiThrows()
    {
        // Arrange
        var tagId = Guid.NewGuid();

        _apiClient.GetTagByIdAsync(tagId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException("Server Error", 500, null, null, null));
        _apiClient.GetTagByIdAsync(tagId)
            .ThrowsAsync(new ApiException("Server Error", 500, null, null, null));

        // Act
        var result = await _service.GetTagByIdAsync(tagId);

        // Assert
        await Assert.That(result).IsNull();
    }

    #endregion

    // ========== CreateTagAsync ==========

    #region CreateTagAsync Tests

    [Test]
    public async Task CreateTagAsync_ReturnsResponse_WhenApiSucceeds()
    {
        // Arrange
        var dto = new CreateTagDto();
        var expectedResponse = ComponentDataBuilder.SuccessResponse();

        _apiClient.CreateTagAsync(Arg.Any<CreateTagDto>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(expectedResponse);
        _apiClient.CreateTagAsync(Arg.Any<CreateTagDto>())
            .Returns(expectedResponse);

        // Act
        var result = await _service.CreateTagAsync(dto);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Success).IsTrue();
    }

    [Test]
    public async Task CreateTagAsync_ReturnsFailureResponse_WhenApiThrows()
    {
        // Arrange
        var dto = new CreateTagDto();

        _apiClient.CreateTagAsync(Arg.Any<CreateTagDto>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException("Bad Request", 400, "validation error", null, null));
        _apiClient.CreateTagAsync(Arg.Any<CreateTagDto>())
            .ThrowsAsync(new ApiException("Bad Request", 400, "validation error", null, null));

        // Act
        var result = await _service.CreateTagAsync(dto);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Success).IsFalse();
        await Assert.That(result.Message).Contains("API error");
    }

    #endregion

    // ========== UpdateTagAsync ==========

    #region UpdateTagAsync Tests

    [Test]
    public async Task UpdateTagAsync_ReturnsResponse_WhenApiSucceeds()
    {
        // Arrange
        var tagId = Guid.NewGuid();
        var dto = new UpdateTagDto { FullName = new() { Value = "Updated" } };
        var expectedResponse = ComponentDataBuilder.SuccessResponse(tagId);

        _apiClient.UpdateTagAsync(Arg.Any<Guid>(), Arg.Any<UpdateTagDto>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(expectedResponse);
        _apiClient.UpdateTagAsync(Arg.Any<Guid>(), Arg.Any<UpdateTagDto>())
            .Returns(expectedResponse);

        // Act
        var result = await _service.UpdateTagAsync(tagId, dto);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Success).IsTrue();
    }

    [Test]
    public async Task UpdateTagAsync_ReturnsFailureResponse_WhenApiThrows()
    {
        // Arrange
        var tagId = Guid.NewGuid();
        var dto = new UpdateTagDto { FullName = new() { Value = "Updated" } };

        _apiClient.UpdateTagAsync(Arg.Any<Guid>(), Arg.Any<UpdateTagDto>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException("Bad Request", 400, "validation error", null, null));
        _apiClient.UpdateTagAsync(Arg.Any<Guid>(), Arg.Any<UpdateTagDto>())
            .ThrowsAsync(new ApiException("Bad Request", 400, "validation error", null, null));

        // Act
        var result = await _service.UpdateTagAsync(tagId, dto);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Success).IsFalse();
        await Assert.That(result.Message).Contains("API error");
    }

    #endregion

    // ========== DeleteTagAsync ==========

    #region DeleteTagAsync Tests

    [Test]
    public async Task DeleteTagAsync_ReturnsTrue_WhenApiSucceeds()
    {
        // Arrange
        var tagId = Guid.NewGuid();

        _apiClient.DeleteTagAsync(tagId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _apiClient.DeleteTagAsync(tagId).Returns(Task.CompletedTask);

        // Act
        var result = await _service.DeleteTagAsync(tagId);

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task DeleteTagAsync_ReturnsFalse_WhenApiThrows()
    {
        // Arrange
        var tagId = Guid.NewGuid();

        _apiClient.DeleteTagAsync(tagId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException("Forbidden", 403, null, null, null));
        _apiClient.DeleteTagAsync(tagId)
            .ThrowsAsync(new ApiException("Forbidden", 403, null, null, null));

        // Act
        var result = await _service.DeleteTagAsync(tagId);

        // Assert
        await Assert.That(result).IsFalse();
    }

    #endregion

    // ========== HAL Response Helpers ==========

    #region HAL Response Helpers

    private static HalCollectionResourceOfTagListDto CreateTagCollectionResponse(IList<TagListDto> items)
    {
        return new HalCollectionResourceOfTagListDto
        {
            _embedded = new HalCollectionEmbeddedOfTagListDto
            {
                Items = items.Select(ToHalResource).ToList()
            }
        };
    }

    private static HalResourceOfTagListDto ToHalResource(TagListDto item)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(item);
        return System.Text.Json.JsonSerializer.Deserialize<HalResourceOfTagListDto>(json)
               ?? new HalResourceOfTagListDto();
    }

    #endregion
}
