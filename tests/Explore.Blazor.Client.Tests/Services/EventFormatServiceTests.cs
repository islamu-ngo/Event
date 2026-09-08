// Verifies successful pass-through and exception propagation from the API client.

namespace Explore.Blazor.Client.Tests.Services;

/// <summary>
/// Unit tests for EventFormatService.
/// </summary>
public class EventFormatServiceTests
{
    private readonly IEventFormatClient _apiClient;
    private readonly EventFormatService _service;

    public EventFormatServiceTests()
    {
        _apiClient = Substitute.For<IEventFormatClient>();
        _service = new EventFormatService(_apiClient);
    }

    // ========== GetEventFormatsAsync ==========

    #region GetEventFormatsAsync Tests

    [Test]
    public async Task GetEventFormatsAsync_ReturnsData_WhenApiSucceeds()
    {
        // Arrange
        var items = new List<EventFormatListDto> { new() };
        _apiClient.GetEventFormatOptionsAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(items);

        // Act
        var result = await _service.GetEventFormatsAsync();

        // Assert
        await Assert.That(result.Count).IsEqualTo(1);
    }

    [Test]
    public async Task GetEventFormatsAsync_Throws_WhenApiThrows()
    {
        // Arrange
        _apiClient.GetEventFormatOptionsAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException("Server Error", 500, null, null, null));

        // Act & Assert
        await Assert.That(() => _service.GetEventFormatsAsync()).ThrowsExactly<ApiException>();
    }

    #endregion

    // ========== GetEventFormatByIdAsync ==========

    #region GetEventFormatByIdAsync Tests

    [Test]
    public async Task GetEventFormatByIdAsync_ReturnsItem_WhenApiSucceeds()
    {
        // Arrange
        const int id = 1;
        var item = new EventFormatDto();

        _apiClient.GetEventFormatOptionByIdAsync(id, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(item);
        _apiClient.GetEventFormatOptionByIdAsync(id).Returns(item);

        // Act
        var result = await _service.GetEventFormatByIdAsync(id);

        // Assert
        await Assert.That(result).IsNotNull();
    }

    [Test]
    public async Task GetEventFormatByIdAsync_Throws_WhenApiThrows()
    {
        // Arrange
        const int id = 1;

        _apiClient.GetEventFormatOptionByIdAsync(id, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException("Server Error", 500, null, null, null));
        _apiClient.GetEventFormatOptionByIdAsync(id)
            .ThrowsAsync(new ApiException("Server Error", 500, null, null, null));

        // Act & Assert
        await Assert.That(() => _service.GetEventFormatByIdAsync(id)).ThrowsExactly<ApiException>();
    }

    #endregion
}
