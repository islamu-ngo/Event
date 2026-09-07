// Verifies successful pass-through and exception propagation from the API client.

namespace Explore.Blazor.Client.Tests.Services;

/// <summary>
/// Unit tests for MadhabService.
/// </summary>
public class MadhabServiceTests
{
    private readonly IMadhabClient _apiClient;
    private readonly MadhabService _service;

    public MadhabServiceTests()
    {
        _apiClient = Substitute.For<IMadhabClient>();
        _service = new MadhabService(_apiClient);
    }

    // ========== GetMadhabsAsync ==========

    #region GetMadhabsAsync Tests

    [Test]
    public async Task GetMadhabsAsync_ReturnsData_WhenApiSucceeds()
    {
        // Arrange
        var items = new List<MadhabListDto> { new() };
        _apiClient.GetMadhabsAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(items);

        // Act
        var result = await _service.GetMadhabsAsync();

        // Assert
        await Assert.That(result.Count).IsEqualTo(1);
    }

    [Test]
    public async Task GetMadhabsAsync_Throws_WhenApiThrows()
    {
        // Arrange
        _apiClient.GetMadhabsAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException("Server Error", 500, null, null, null));

        // Act & Assert
        await Assert.That(() => _service.GetMadhabsAsync()).ThrowsExactly<ApiException>();
    }

    #endregion

    // ========== GetMadhabByIdAsync ==========

    #region GetMadhabByIdAsync Tests

    [Test]
    public async Task GetMadhabByIdAsync_ReturnsItem_WhenApiSucceeds()
    {
        // Arrange
        const int id = 1;
        var item = new MadhabDto();

        _apiClient.GetMadhabByIdAsync(id, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(item);
        _apiClient.GetMadhabByIdAsync(id).Returns(item);

        // Act
        var result = await _service.GetMadhabByIdAsync(id);

        // Assert
        await Assert.That(result).IsNotNull();
    }

    [Test]
    public async Task GetMadhabByIdAsync_Throws_WhenApiThrows()
    {
        // Arrange
        const int id = 1;

        _apiClient.GetMadhabByIdAsync(id, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException("Server Error", 500, null, null, null));
        _apiClient.GetMadhabByIdAsync(id)
            .ThrowsAsync(new ApiException("Server Error", 500, null, null, null));

        // Act & Assert
        await Assert.That(() => _service.GetMadhabByIdAsync(id)).ThrowsExactly<ApiException>();
    }

    #endregion
}
