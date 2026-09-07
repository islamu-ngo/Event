// Verifies successful pass-through and exception propagation from the API client.

namespace Explore.Blazor.Client.Tests.Services;

/// <summary>
/// Unit tests for EventTypeService.
/// </summary>
public class EventTypeServiceTests
{
    private readonly IEventTypeClient _apiClient;
    private readonly EventTypeService _service;

    public EventTypeServiceTests()
    {
        _apiClient = Substitute.For<IEventTypeClient>();
        _service = new EventTypeService(_apiClient);
    }

    // ========== GetEventTypesAsync ==========

    #region GetEventTypesAsync Tests

    [Test]
    public async Task GetEventTypesAsync_ReturnsData_WhenApiSucceeds()
    {
        // Arrange
        var items = new List<EventTypeListDto> { new() };
        _apiClient.GetEventTypesAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(items);

        // Act
        var result = await _service.GetEventTypesAsync();

        // Assert
        await Assert.That(result.Count).IsEqualTo(1);
    }

    [Test]
    public async Task GetEventTypesAsync_Throws_WhenApiThrows()
    {
        // Arrange
        _apiClient.GetEventTypesAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException("Server Error", 500, null, null, null));

        // Act & Assert
        await Assert.That(() => _service.GetEventTypesAsync()).ThrowsExactly<ApiException>();
    }

    #endregion
}
