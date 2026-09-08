// Verifies successful pass-through and exception propagation from the API client.

namespace Explore.Blazor.Client.Tests.Services;

/// <summary>
/// Unit tests for LanguageService.
/// </summary>
public class LanguageServiceTests
{
    private readonly ILanguageClient _apiClient;
    private readonly LanguageService _service;

    public LanguageServiceTests()
    {
        _apiClient = Substitute.For<ILanguageClient>();
        _service = new LanguageService(_apiClient);
    }

    // ========== GetLanguagesAsync ==========

    #region GetLanguagesAsync Tests

    [Test]
    public async Task GetLanguagesAsync_ReturnsData_WhenApiSucceeds()
    {
        // Arrange
        var items = new List<LanguageListDto> { new() };
        _apiClient.GetLanguagesAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(items);

        // Act
        var result = await _service.GetLanguagesAsync();

        // Assert
        await Assert.That(result.Count).IsEqualTo(1);
    }

    [Test]
    public async Task GetLanguagesAsync_Throws_WhenApiThrows()
    {
        // Arrange
        _apiClient.GetLanguagesAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException("Server Error", 500, null, null, null));

        // Act & Assert
        await Assert.That(() => _service.GetLanguagesAsync()).ThrowsExactly<ApiException>();
    }

    #endregion

    // ========== GetLanguageByIdAsync ==========

    #region GetLanguageByIdAsync Tests

    [Test]
    public async Task GetLanguageByIdAsync_ReturnsItem_WhenApiSucceeds()
    {
        // Arrange
        const int id = 1;
        var item = new LanguageDto();

        _apiClient.GetLanguageByIdAsync(id, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(item);
        _apiClient.GetLanguageByIdAsync(id).Returns(item);

        // Act
        var result = await _service.GetLanguageByIdAsync(id);

        // Assert
        await Assert.That(result).IsNotNull();
    }

    [Test]
    public async Task GetLanguageByIdAsync_Throws_WhenApiThrows()
    {
        // Arrange
        const int id = 1;

        _apiClient.GetLanguageByIdAsync(id, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException("Server Error", 500, null, null, null));
        _apiClient.GetLanguageByIdAsync(id)
            .ThrowsAsync(new ApiException("Server Error", 500, null, null, null));

        // Act & Assert
        await Assert.That(() => _service.GetLanguageByIdAsync(id)).ThrowsExactly<ApiException>();
    }

    #endregion
}
