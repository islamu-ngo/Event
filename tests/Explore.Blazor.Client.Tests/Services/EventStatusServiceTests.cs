// Verifies successful pass-through and exception propagation from the API client.

namespace Explore.Blazor.Client.Tests.Services;

/// <summary>
/// Unit tests for EventStatusService.
/// </summary>
public class EventStatusServiceTests
{
    private readonly IEventStatusClient _apiClient;
    private readonly EventStatusService _service;

    public EventStatusServiceTests()
    {
        _apiClient = Substitute.For<IEventStatusClient>();
        _service = new EventStatusService(_apiClient);
    }

    // ========== GetEventStatusesAsync ==========

    #region GetEventStatusesAsync Tests

    [Test]
    public async Task GetEventStatusesAsync_ReturnsData_WhenApiSucceeds()
    {
        // Arrange
        var items = new List<EventStatusListDto> { new() };
        _apiClient.GetEventStatusesAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(items);

        // Act
        var result = await _service.GetEventStatusesAsync();

        // Assert
        await Assert.That(result.Count).IsEqualTo(1);
    }

    [Test]
    public async Task GetEventStatusesAsync_Throws_WhenApiThrows()
    {
        // Arrange
        _apiClient.GetEventStatusesAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException("Server Error", 500, null, null, null));

        // Act & Assert
        await Assert.That(() => _service.GetEventStatusesAsync()).ThrowsExactly<ApiException>();
    }

    #endregion

    // ========== GetEventStatusByIdAsync ==========

    #region GetEventStatusByIdAsync Tests

    [Test]
    public async Task GetEventStatusByIdAsync_ReturnsItem_WhenApiSucceeds()
    {
        // Arrange
        const int id = 1;
        var item = new EventStatusDto();

        _apiClient.GetEventStatusByIdAsync(id, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(item);
        _apiClient.GetEventStatusByIdAsync(id).Returns(item);

        // Act
        var result = await _service.GetEventStatusByIdAsync(id);

        // Assert
        await Assert.That(result).IsNotNull();
    }

    [Test]
    public async Task GetEventStatusByIdAsync_Throws_WhenApiThrows()
    {
        // Arrange
        const int id = 1;

        _apiClient.GetEventStatusByIdAsync(id, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException("Server Error", 500, null, null, null));
        _apiClient.GetEventStatusByIdAsync(id)
            .ThrowsAsync(new ApiException("Server Error", 500, null, null, null));

        // Act & Assert
        await Assert.That(() => _service.GetEventStatusByIdAsync(id)).ThrowsExactly<ApiException>();
    }

    #endregion
}
