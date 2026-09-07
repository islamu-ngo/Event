// Verifies successful pass-through and exception propagation from the API client.

namespace Explore.Blazor.Client.Tests.Services;

/// <summary>
/// Unit tests for AudienceGenderService.
/// </summary>
public class AudienceGenderServiceTests
{
    private readonly IAudienceGenderClient _apiClient;
    private readonly AudienceGenderService _service;

    public AudienceGenderServiceTests()
    {
        _apiClient = Substitute.For<IAudienceGenderClient>();
        _service = new AudienceGenderService(_apiClient);
    }

    // ========== GetAudienceGendersAsync ==========

    #region GetAudienceGendersAsync Tests

    [Test]
    public async Task GetAudienceGendersAsync_ReturnsData_WhenApiSucceeds()
    {
        // Arrange
        var items = new List<AudienceGenderListDto> { new() };
        _apiClient.GetAudienceGenderOptionsAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(items);

        // Act
        var result = await _service.GetAudienceGendersAsync();

        // Assert
        await Assert.That(result.Count).IsEqualTo(1);
    }

    [Test]
    public async Task GetAudienceGendersAsync_Throws_WhenApiThrows()
    {
        // Arrange
        _apiClient.GetAudienceGenderOptionsAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException("Server Error", 500, null, null, null));

        // Act & Assert
        await Assert.That(() => _service.GetAudienceGendersAsync()).ThrowsExactly<ApiException>();
    }

    #endregion

    // ========== GetAudienceGenderByIdAsync ==========

    #region GetAudienceGenderByIdAsync Tests

    [Test]
    public async Task GetAudienceGenderByIdAsync_ReturnsItem_WhenApiSucceeds()
    {
        // Arrange
        const int id = 1;
        var item = new AudienceGenderDto();

        _apiClient.GetAudienceGenderOptionByIdAsync(id, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(item);
        _apiClient.GetAudienceGenderOptionByIdAsync(id).Returns(item);

        // Act
        var result = await _service.GetAudienceGenderByIdAsync(id);

        // Assert
        await Assert.That(result).IsNotNull();
    }

    [Test]
    public async Task GetAudienceGenderByIdAsync_Throws_WhenApiThrows()
    {
        // Arrange
        const int id = 1;

        _apiClient.GetAudienceGenderOptionByIdAsync(id, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException("Server Error", 500, null, null, null));
        _apiClient.GetAudienceGenderOptionByIdAsync(id)
            .ThrowsAsync(new ApiException("Server Error", 500, null, null, null));

        // Act & Assert
        await Assert.That(() => _service.GetAudienceGenderByIdAsync(id)).ThrowsExactly<ApiException>();
    }

    #endregion
}
