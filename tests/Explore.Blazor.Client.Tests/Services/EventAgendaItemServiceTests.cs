using System.Globalization;

namespace Explore.Blazor.Client.Tests.Services;

public class EventAgendaItemServiceTests
{
    private readonly IEventAgendaItemClient _apiClient;
    private readonly ILogger<EventAgendaItemService> _logger;
    private readonly EventAgendaItemService _service;

    public EventAgendaItemServiceTests()
    {
        _apiClient = Substitute.For<IEventAgendaItemClient>();
        _logger = Substitute.For<ILogger<EventAgendaItemService>>();
        _service = new EventAgendaItemService(_apiClient, _logger);
    }

    // ========== GetAgendaItemsByEventAsync ==========

    [Test]
    public async Task GetAgendaItemsByEventAsync_ReturnsItems_WhenApiSucceeds()
    {
        var eventId = Guid.NewGuid();
        var eventDate = new DateTimeOffset(2026, 5, 7, 0, 0, 0, TimeSpan.Zero);
        var halResponse = CreateHalCollectionResponse(new List<EventAgendaItemListDto>
        {
            CreateAgendaItemListDto("Keynote", 1, eventDate, new TimeSpan(9, 0, 0), new TimeSpan(10, 0, 0)),
            CreateAgendaItemListDto("Workshop", 2, eventDate, new TimeSpan(10, 30, 0), new TimeSpan(12, 0, 0))
        });

        _apiClient.GetEventAgendaItemsByEventAsync(eventId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(halResponse);

        var result = await _service.GetAgendaItemsByEventAsync(eventId);

        await Assert.That(result.Count).IsEqualTo(2);
    }

    [Test]
    public async Task GetAgendaItemsByEventAsync_ReturnsEmptyList_WhenApiThrows()
    {
        _apiClient.GetEventAgendaItemsByEventAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Network error"));

        var result = await _service.GetAgendaItemsByEventAsync(Guid.NewGuid());

        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task GetAgendaItemsByEventAsync_ReturnsEmptyList_WhenApiReturnsNull()
    {
        _apiClient.GetEventAgendaItemsByEventAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((HalCollectionResourceOfEventAgendaItemListDto?)null);

        var result = await _service.GetAgendaItemsByEventAsync(Guid.NewGuid());

        await Assert.That(result).IsEmpty();
    }

    // ========== GetAgendaItemByIdAsync ==========

    [Test]
    public async Task GetAgendaItemByIdAsync_ReturnsItem_WhenApiSucceeds()
    {
        var itemId = Guid.NewGuid();
        var dto = new EventAgendaItemDto
        {
            Id = itemId,
            Title = "Keynote Speech",
            StartTime = DateTimeOffset.Parse("2026-05-07T09:00:00+00:00", CultureInfo.InvariantCulture),
            EndTime = DateTimeOffset.Parse("2026-05-07T10:00:00+00:00", CultureInfo.InvariantCulture),
            LocalStartDate = new DateTimeOffset(2026, 5, 7, 0, 0, 0, TimeSpan.Zero),
            LocalEndDate = new DateTimeOffset(2026, 5, 7, 0, 0, 0, TimeSpan.Zero),
            LocalStartTime = new TimeSpan(9, 0, 0),
            LocalEndTime = new TimeSpan(10, 0, 0)
        };
        var halResponse = CreateHalResourceResponse(dto);

        _apiClient.GetEventAgendaItemByIdAsync(itemId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(halResponse);

        var result = await _service.GetAgendaItemByIdAsync(itemId);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Title).IsEqualTo("Keynote Speech");
    }

    [Test]
    public async Task GetAgendaItemByIdAsync_ReturnsNull_WhenNotFound()
    {
        _apiClient.GetEventAgendaItemByIdAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException("Not found", 404));

        var result = await _service.GetAgendaItemByIdAsync(Guid.NewGuid());

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetAgendaItemByIdAsync_ReturnsNull_WhenApiThrows()
    {
        _apiClient.GetEventAgendaItemByIdAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Server error"));

        var result = await _service.GetAgendaItemByIdAsync(Guid.NewGuid());

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetManagedAgendaItemByIdAsync_PreservesRoomForEdit()
    {
        var eventId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var roomId = Guid.NewGuid();
        var dto = new EventAgendaItemDto
        {
            Id = itemId,
            EventId = eventId,
            Title = "Keynote Speech",
            RoomId = roomId,
            StartTime = DateTimeOffset.Parse("2026-05-07T09:00:00+00:00", CultureInfo.InvariantCulture),
            EndTime = DateTimeOffset.Parse("2026-05-07T10:00:00+00:00", CultureInfo.InvariantCulture)
        };
        _apiClient.GetManagedEventAgendaItemByIdAsync(
                eventId,
                itemId,
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateHalResourceResponse(dto));

        var result = await _service.GetManagedAgendaItemByIdAsync(eventId, itemId);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.RoomId).IsEqualTo(roomId);
        await _apiClient.DidNotReceive().GetEventAgendaItemByIdAsync(
            Arg.Any<Guid>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    // ========== CreateAgendaItemAsync ==========

    [Test]
    public async Task CreateAgendaItemAsync_ReturnsResponse_WhenApiSucceeds()
    {
        var newId = Guid.NewGuid();
        var dto = new CreateEventAgendaItemDto { EventId = Guid.NewGuid(), Title = "Workshop" };
        var response = new BaseCommandResponseOfGuid { Success = true, Id = newId };

        _apiClient.CreateEventAgendaItemAsync(dto, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(response);

        var result = await _service.CreateAgendaItemAsync(dto);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Success).IsTrue();
        await Assert.That(result.Id).IsEqualTo(newId);
    }

    [Test]
    public async Task CreateAgendaItemAsync_ReturnsFailureResponse_WhenApiThrows()
    {
        var dto = new CreateEventAgendaItemDto { EventId = Guid.NewGuid(), Title = "Workshop" };

        _apiClient.CreateEventAgendaItemAsync(dto, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException("Validation failed", 400));

        var result = await _service.CreateAgendaItemAsync(dto);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Success).IsFalse();
    }

    // ========== UpdateAgendaItemAsync ==========

    [Test]
    public async Task UpdateAgendaItemAsync_ReturnsResponse_WhenApiSucceeds()
    {
        var itemId = Guid.NewGuid();
        var expectedConcurrencyStamp = Guid.NewGuid();
        var dto = new UpdateEventAgendaItemDto
        {
            Title = new UpdateEventAgendaItemTitleDto { Value = "Updated Workshop" }
        };
        var response = new BaseCommandResponseOfGuid { Success = true, Id = itemId };

        _apiClient.UpdateEventAgendaItemAsync(
                itemId,
                dto,
                $"\"{expectedConcurrencyStamp:D}\"",
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(response);

        var result = await _service.UpdateAgendaItemAsync(itemId, expectedConcurrencyStamp, dto);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Success).IsTrue();
    }

    [Test]
    public async Task UpdateAgendaItemAsync_ReturnsFailureResponse_WhenApiThrows()
    {
        var itemId = Guid.NewGuid();
        var expectedConcurrencyStamp = Guid.NewGuid();
        var dto = new UpdateEventAgendaItemDto
        {
            Title = new UpdateEventAgendaItemTitleDto { Value = "Updated Workshop" }
        };

        _apiClient.UpdateEventAgendaItemAsync(
                itemId,
                dto,
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException("Conflict", 409));

        var result = await _service.UpdateAgendaItemAsync(itemId, expectedConcurrencyStamp, dto);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Success).IsFalse();
    }

    // ========== DeleteAgendaItemAsync ==========

    [Test]
    public async Task DeleteAgendaItemAsync_ReturnsTrue_WhenApiSucceeds()
    {
        var itemId = Guid.NewGuid();

        _apiClient.DeleteEventAgendaItemAsync(itemId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var result = await _service.DeleteAgendaItemAsync(itemId);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task DeleteAgendaItemAsync_ReturnsFalse_WhenApiThrows()
    {
        _apiClient.DeleteEventAgendaItemAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Delete failed"));

        var result = await _service.DeleteAgendaItemAsync(Guid.NewGuid());

        await Assert.That(result).IsFalse();
    }

    // ========== Helpers ==========

    private static HalCollectionResourceOfEventAgendaItemListDto CreateHalCollectionResponse(
        IList<EventAgendaItemListDto> items)
    {
        return new HalCollectionResourceOfEventAgendaItemListDto
        {
            _embedded = new HalCollectionEmbeddedOfEventAgendaItemListDto
            {
                Items = items.Select(ToHalResource).ToList()
            }
        };
    }

    private static EventAgendaItemListDto CreateAgendaItemListDto(
        string title,
        int sortOrder,
        DateTimeOffset localDate,
        TimeSpan localStartTime,
        TimeSpan localEndTime)
    {
        return new EventAgendaItemListDto
        {
            Id = Guid.NewGuid(),
            EventId = Guid.NewGuid(),
            Title = title,
            SortOrder = sortOrder,
            LocalStartDate = localDate,
            LocalStartTime = localStartTime,
            LocalEndTime = localEndTime,
            StartTime = new DateTimeOffset(localDate.Date + localStartTime, TimeSpan.Zero),
            EndTime = new DateTimeOffset(localDate.Date + localEndTime, TimeSpan.Zero)
        };
    }

    private static HalResourceOfEventAgendaItemListDto ToHalResource(EventAgendaItemListDto item)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(item);
        return System.Text.Json.JsonSerializer.Deserialize<HalResourceOfEventAgendaItemListDto>(json)
               ?? new HalResourceOfEventAgendaItemListDto();
    }

    private static HalResourceOfEventAgendaItemDto CreateHalResourceResponse(EventAgendaItemDto dto)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(dto);
        return System.Text.Json.JsonSerializer.Deserialize<HalResourceOfEventAgendaItemDto>(json)
               ?? new HalResourceOfEventAgendaItemDto();
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
