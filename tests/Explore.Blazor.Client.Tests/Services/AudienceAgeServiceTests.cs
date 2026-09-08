// Verifies successful pass-through and exception propagation from the API client.

namespace Explore.Blazor.Client.Tests.Services;

/// <summary>
/// Unit tests for AudienceAgeService.
/// </summary>
public class AudienceAgeServiceTests
{
    private readonly IAudienceAgeClient _apiClient;
    private readonly AudienceAgeService _service;

    public AudienceAgeServiceTests()
    {
        _apiClient = Substitute.For<IAudienceAgeClient>();
        _service = new AudienceAgeService(_apiClient);
    }

    // ========== GetAudienceAgesAsync ==========

    #region GetAudienceAgesAsync Tests

    [Test]
    public async Task GetAudienceAgesAsync_ReturnsData_WhenApiSucceeds()
    {
        // Arrange
        var items = new List<AudienceAgeListDto> { new() };
        _apiClient.GetAudienceAgeOptionsAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(items);

        // Act
        var result = await _service.GetAudienceAgesAsync();

        // Assert
        await Assert.That(result.Count).IsEqualTo(1);
    }

    [Test]
    public async Task GetAudienceAgesAsync_Throws_WhenApiThrows()
    {
        // Arrange
        _apiClient.GetAudienceAgeOptionsAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException("Server Error", 500, null, null, null));

        // Act & Assert
        await Assert.That(() => _service.GetAudienceAgesAsync()).ThrowsExactly<ApiException>();
    }

    #endregion

    // ========== GetAudienceAgeByIdAsync ==========

    #region GetAudienceAgeByIdAsync Tests

    [Test]
    public async Task GetAudienceAgeByIdAsync_ReturnsItem_WhenApiSucceeds()
    {
        // Arrange
        const int id = 1;
        var item = new AudienceAgeDto();

        _apiClient.GetAudienceAgeOptionByIdAsync(id, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(item);
        _apiClient.GetAudienceAgeOptionByIdAsync(id).Returns(item);

        // Act
        var result = await _service.GetAudienceAgeByIdAsync(id);

        // Assert
        await Assert.That(result).IsNotNull();
    }

    [Test]
    public async Task GetAudienceAgeByIdAsync_Throws_WhenApiThrows()
    {
        // Arrange
        const int id = 1;

        _apiClient.GetAudienceAgeOptionByIdAsync(id, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException("Server Error", 500, null, null, null));
        _apiClient.GetAudienceAgeOptionByIdAsync(id)
            .ThrowsAsync(new ApiException("Server Error", 500, null, null, null));

        // Act & Assert
        await Assert.That(() => _service.GetAudienceAgeByIdAsync(id)).ThrowsExactly<ApiException>();
    }

    #endregion
}
