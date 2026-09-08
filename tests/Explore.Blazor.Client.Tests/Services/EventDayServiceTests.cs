namespace Explore.Blazor.Client.Tests.Services;

public class EventDayServiceTests
{
    private readonly IEventDayClient _apiClient;
    private readonly ILogger<EventDayService> _logger;
    private readonly EventDayService _service;

    public EventDayServiceTests()
    {
        _apiClient = Substitute.For<IEventDayClient>();
        _logger = Substitute.For<ILogger<EventDayService>>();
        _service = new EventDayService(_apiClient, _logger);
    }

    // ========== GetDaysByEventAsync ==========

    [Test]
    public async Task GetDaysByEventAsync_ReturnsDays_WhenApiSucceeds()
    {
        var eventId = Guid.NewGuid();
        var halResponse = CreateHalCollectionResponse(new List<EventDayListDto>
        {
            new() { Id = Guid.NewGuid(), LocalDate = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), Label = "Day 1" },
            new() { Id = Guid.NewGuid(), LocalDate = new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero), Label = "Day 2" }
        });

        _apiClient.GetEventDaysByEventAsync(eventId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(halResponse);

        var result = await _service.GetDaysByEventAsync(eventId);

        await Assert.That(result.Count).IsEqualTo(2);
    }

    [Test]
    public async Task GetDaysByEventAsync_UsesManagedRoute_WhenRequested()
    {
        var eventId = Guid.NewGuid();
        _apiClient.GetManagedEventDaysByEventAsync(
                eventId,
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateHalCollectionResponse([]));

        await _service.GetDaysByEventAsync(eventId, includeManaged: true);

        await _apiClient.Received(1).GetManagedEventDaysByEventAsync(
            eventId,
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
        await _apiClient.DidNotReceive().GetEventDaysByEventAsync(
            Arg.Any<Guid>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetDaysByEventAsync_WhenCancelled_PropagatesCancellation()
    {
        var eventId = Guid.NewGuid();
        using var source = new CancellationTokenSource();
        source.Cancel();
        _apiClient.GetManagedEventDaysByEventAsync(eventId, null, null, source.Token)
            .Returns<Task<HalCollectionResourceOfEventDayListDto>>(_ => throw new OperationCanceledException(source.Token));

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await _service.GetDaysByEventAsync(eventId, includeManaged: true, source.Token));
    }

    [Test]
    public async Task GetDaysByEventAsync_ReturnsEmptyList_WhenApiThrows()
    {
        _apiClient.GetEventDaysByEventAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Network error"));

        var result = await _service.GetDaysByEventAsync(Guid.NewGuid());

        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task GetDaysByEventAsync_ReturnsEmptyList_WhenApiReturnsNull()
    {
        _apiClient.GetEventDaysByEventAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((HalCollectionResourceOfEventDayListDto?)null);

        var result = await _service.GetDaysByEventAsync(Guid.NewGuid());

        await Assert.That(result).IsEmpty();
    }

    // ========== GetDayByIdAsync ==========

    [Test]
    public async Task GetDayByIdAsync_ReturnsDay_WhenApiSucceeds()
    {
        var dayId = Guid.NewGuid();
        var dto = new EventDayDto
        {
            Id = dayId,
            LocalDate = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            Label = "Day 1",
            IsPublished = true
        };
        var halResponse = CreateHalResourceResponse(dto);

        _apiClient.GetEventDayByIdAsync(dayId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(halResponse);

        var result = await _service.GetDayByIdAsync(dayId);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Label).IsEqualTo("Day 1");
    }

    [Test]
    public async Task GetDayByIdAsync_ReturnsNull_WhenNotFound()
    {
        _apiClient.GetEventDayByIdAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException("Not found", 404));

        var result = await _service.GetDayByIdAsync(Guid.NewGuid());

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetDayByIdAsync_ReturnsNull_WhenApiThrows()
    {
        _apiClient.GetEventDayByIdAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Server error"));

        var result = await _service.GetDayByIdAsync(Guid.NewGuid());

        await Assert.That(result).IsNull();
    }

    // ========== CreateDayAsync ==========

    [Test]
    public async Task CreateDayAsync_ReturnsResponse_WhenApiSucceeds()
    {
        var newId = Guid.NewGuid();
        var dto = new CreateEventDayDto { EventId = Guid.NewGuid(), Label = "Day 1" };
        var response = new BaseCommandResponseOfGuid { Success = true, Id = newId };

        _apiClient.CreateEventDayAsync(dto, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(response);

        var result = await _service.CreateDayAsync(dto);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Success).IsTrue();
        await Assert.That(result.Id).IsEqualTo(newId);
    }

    [Test]
    public async Task CreateDayAsync_ReturnsFailureResponse_WhenApiThrows()
    {
        var dto = new CreateEventDayDto { EventId = Guid.NewGuid(), Label = "Day 1" };

        _apiClient.CreateEventDayAsync(dto, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException("Validation failed", 400));

        var result = await _service.CreateDayAsync(dto);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Success).IsFalse();
    }

    // ========== UpdateDayAsync ==========

    [Test]
    public async Task UpdateDayAsync_ReturnsResponse_WhenApiSucceeds()
    {
        var dayId = Guid.NewGuid();
        var stamp = Guid.NewGuid();
        var dto = new UpdateEventDayDto
        {
            Label = new UpdateEventDayLabelDto
            {
                Value = new OptionalUpdateOfstring { HasValue = true, Value = "Updated Day" }
            }
        };
        var response = new BaseCommandResponseOfGuid { Success = true, Id = dayId };

        _apiClient.UpdateEventDayAsync(dayId, dto, $"\"{stamp:D}\"", Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(response);

        var result = await _service.UpdateDayAsync(dayId, stamp, dto);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Success).IsTrue();
    }

    [Test]
    public async Task UpdateDayAsync_ReturnsFailureResponse_WhenApiThrows()
    {
        var dayId = Guid.NewGuid();
        var stamp = Guid.NewGuid();
        var dto = new UpdateEventDayDto
        {
            Label = new UpdateEventDayLabelDto
            {
                Value = new OptionalUpdateOfstring { HasValue = true, Value = "Updated Day" }
            }
        };

        _apiClient.UpdateEventDayAsync(dayId, dto, $"\"{stamp:D}\"", Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException("Conflict", 409));

        var result = await _service.UpdateDayAsync(dayId, stamp, dto);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Success).IsFalse();
    }

    // ========== DeleteDayAsync ==========

    [Test]
    public async Task DeleteDayAsync_ReturnsTrue_WhenApiSucceeds()
    {
        var dayId = Guid.NewGuid();

        _apiClient.DeleteEventDayAsync(dayId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var result = await _service.DeleteDayAsync(dayId);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task DeleteDayAsync_ReturnsFalse_WhenApiThrows()
    {
        _apiClient.DeleteEventDayAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Delete failed"));

        var result = await _service.DeleteDayAsync(Guid.NewGuid());

        await Assert.That(result).IsFalse();
    }

    // ========== Helpers ==========

    private static HalCollectionResourceOfEventDayListDto CreateHalCollectionResponse(
        IList<EventDayListDto> items)
    {
        return new HalCollectionResourceOfEventDayListDto
        {
            _embedded = new HalCollectionEmbeddedOfEventDayListDto
            {
                Items = items.Select(ToHalResource).ToList()
            }
        };
    }

    private static HalResourceOfEventDayListDto ToHalResource(EventDayListDto item)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(item);
        return System.Text.Json.JsonSerializer.Deserialize<HalResourceOfEventDayListDto>(json)
               ?? new HalResourceOfEventDayListDto();
    }

    private static HalResourceOfEventDayDto CreateHalResourceResponse(EventDayDto dto)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(dto);
        return System.Text.Json.JsonSerializer.Deserialize<HalResourceOfEventDayDto>(json)
               ?? new HalResourceOfEventDayDto();
    }

    private static ApiException CreateApiException(string message, int statusCode, string response = "")
    {
        return new ApiException(
            message,
            statusCode,
            response,
            new Dictionary<string, IEnumerable<string>>(),
            new InvalidOperationException(message));
    }
}
