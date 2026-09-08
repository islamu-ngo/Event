namespace Explore.Blazor.Client.Tests.Services;

public class TenantNavigationServiceTests
{
    private readonly ITenantClient _apiClient;
    private readonly ILogger<TenantNavigationService> _logger;

    public TenantNavigationServiceTests()
    {
        _apiClient = Substitute.For<ITenantClient>();
        _logger = Substitute.For<ILogger<TenantNavigationService>>();
    }

    [Test]
    public async Task GetNavigationLinksAsync_ReturnsLinks_WhenApiSucceeds()
    {
        // Arrange
        var links = new List<TenantNavigationLinkDto> { new(), new() };
        _apiClient.GetTenantNavigationLinksAsync(
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(links);
        var service = CreateService();

        // Act
        var result = await service.GetNavigationLinksAsync();

        // Assert
        await Assert.That(result.Count).IsEqualTo(2);
        await _apiClient.Received(1).GetTenantNavigationLinksAsync(
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetNavigationLinksAsync_ReturnsEmpty_WhenApiReturnsFailureStatus()
    {
        // Arrange
        _apiClient.GetTenantNavigationLinksAsync(
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<ICollection<TenantNavigationLinkDto>>>(_ => throw new ApiException(
                "Server error",
                500,
                string.Empty,
                new Dictionary<string, IEnumerable<string>>(),
                null));
        var service = CreateService();

        // Act
        var result = await service.GetNavigationLinksAsync();

        // Assert
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task CreateNavigationLinkAsync_ReturnsFailureResponse_WhenApiReturnsBadRequest()
    {
        // Arrange
        _apiClient.CreateTenantNavigationLinkAsync(
                Arg.Any<CreateTenantNavigationLinkDto>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<BaseCommandResponseOfGuid>>(_ => throw new ApiException(
                "Bad Request",
                400,
                string.Empty,
                new Dictionary<string, IEnumerable<string>>(),
                null));
        var service = CreateService();
        var dto = new CreateTenantNavigationLinkDto();

        // Act
        var result = await service.CreateNavigationLinkAsync(dto);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Success).IsFalse();
        await Assert.That(result.Message).Contains("Error:");
        await _apiClient.Received(1).CreateTenantNavigationLinkAsync(
            dto,
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UpdateNavigationLinkAsync_SendsPutToIdEndpoint_AndReturnsSuccessBody()
    {
        // Arrange
        var expected = new BaseCommandResponseOfboolean
        {
            Success = true,
            Message = "updated",
            Id = true
        };
        _apiClient.UpdateTenantNavigationLinkAsync(
                Arg.Any<Guid>(),
                Arg.Any<UpdateTenantNavigationLinkDto>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(expected);
        var service = CreateService();
        var linkId = Guid.NewGuid();
        var dto = new UpdateTenantNavigationLinkDto();

        // Act
        var result = await service.UpdateNavigationLinkAsync(linkId, dto);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Success).IsTrue();
        await _apiClient.Received(1).UpdateTenantNavigationLinkAsync(
            linkId,
            dto,
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DeleteNavigationLinkAsync_ReturnsFailureResponse_WhenHttpThrows()
    {
        // Arrange
        _apiClient.DeleteTenantNavigationLinkAsync(
                Arg.Any<Guid>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<BaseCommandResponseOfboolean>>(_ => throw new HttpRequestException("network failed"));
        var service = CreateService();
        var linkId = Guid.NewGuid();

        // Act
        var result = await service.DeleteNavigationLinkAsync(linkId);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Success).IsFalse();
        await Assert.That(result.Message).Contains("Error:");
        await Assert.That(result.Errors).Contains("network failed");
    }

    [Test]
    public async Task ReorderNavigationLinksAsync_SendsPutToReorderEndpoint_AndReturnsSuccessBody()
    {
        // Arrange
        var expected = new BaseCommandResponseOfboolean
        {
            Success = true,
            Message = "reordered",
            Id = true
        };
        _apiClient.ReorderTenantNavigationLinksAsync(
                Arg.Any<IEnumerable<UpdateTenantNavigationLinkOrderDto>>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(expected);
        var service = CreateService();
        var orders = new List<UpdateTenantNavigationLinkOrderDto> { new() };

        // Act
        var result = await service.ReorderNavigationLinksAsync(orders);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Success).IsTrue();
        await _apiClient.Received(1).ReorderTenantNavigationLinksAsync(
            orders,
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    private TenantNavigationService CreateService() => new(_apiClient, _logger);
}
