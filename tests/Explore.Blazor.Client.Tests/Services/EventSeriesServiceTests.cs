namespace Explore.Blazor.Client.Tests.Services;

public class EventSeriesServiceTests
{
    private readonly IEventSeriesClient _apiClient;
    private readonly ILogger<EventSeriesService> _logger;
    private readonly EventSeriesService _service;

    public EventSeriesServiceTests()
    {
        _apiClient = Substitute.For<IEventSeriesClient>();
        _logger = Substitute.For<ILogger<EventSeriesService>>();
        _service = new EventSeriesService(_apiClient, _logger);
    }

    [Test]
    public async Task UpdateSeriesAsync_ReturnsResponse_WhenApiSucceeds()
    {
        var seriesId = Guid.NewGuid();
        var stamp = Guid.NewGuid();
        var dto = new UpdateEventSeriesDto
        {
            Description = new UpdateEventSeriesDescriptionDto
            {
                Value = new OptionalUpdateOfstring { HasValue = true, Value = "Updated series" }
            }
        };
        var response = new BaseCommandResponseOfGuid { Success = true, Id = seriesId };

        _apiClient.UpdateEventSeriesAsync(seriesId, dto, $"\"{stamp:D}\"", Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(response);

        var result = await _service.UpdateSeriesAsync(seriesId, stamp, dto);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Success).IsTrue();
    }

    [Test]
    public async Task UpdateSeriesAsync_ReturnsFailureResponse_WhenApiThrows()
    {
        var seriesId = Guid.NewGuid();
        var stamp = Guid.NewGuid();
        var dto = new UpdateEventSeriesDto
        {
            Description = new UpdateEventSeriesDescriptionDto
            {
                Value = new OptionalUpdateOfstring { HasValue = true, Value = "Updated series" }
            }
        };

        _apiClient.UpdateEventSeriesAsync(seriesId, dto, $"\"{stamp:D}\"", Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Conflict"));

        var result = await _service.UpdateSeriesAsync(seriesId, stamp, dto);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Success).IsFalse();
    }
}
