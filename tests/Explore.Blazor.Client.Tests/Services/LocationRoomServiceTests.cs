namespace Explore.Blazor.Client.Tests.Services;

public class LocationRoomServiceTests
{
    private readonly ILocationRoomClient _apiClient;
    private readonly ILogger<LocationRoomService> _logger;
    private readonly LocationRoomService _service;

    public LocationRoomServiceTests()
    {
        _apiClient = Substitute.For<ILocationRoomClient>();
        _logger = Substitute.For<ILogger<LocationRoomService>>();
        _service = new LocationRoomService(_apiClient, _logger);
    }

    // ========== GetRoomsByLocationAsync ==========

    [Test]
    public async Task GetRoomsByLocationAsync_ReturnsRooms_WhenApiSucceeds()
    {
        var locationId = Guid.NewGuid();
        var halResponse = CreateHalCollectionResponse(new List<LocationRoomListDto>
        {
            new() { Id = Guid.NewGuid(), Name = "Main Hall", Capacity = 500 },
            new() { Id = Guid.NewGuid(), Name = "Room A", Capacity = 50 }
        });

        _apiClient.GetLocationRoomsByLocationAsync(locationId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(halResponse);

        var result = await _service.GetRoomsByLocationAsync(locationId);

        await Assert.That(result.Count).IsEqualTo(2);
    }

    [Test]
    public async Task GetRoomsByLocationAsync_ReturnsEmptyList_WhenApiThrows()
    {
        _apiClient.GetLocationRoomsByLocationAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Network error"));

        var result = await _service.GetRoomsByLocationAsync(Guid.NewGuid());

        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task GetRoomsByLocationAsync_ReturnsEmptyList_WhenApiReturnsNull()
    {
        _apiClient.GetLocationRoomsByLocationAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((HalCollectionResourceOfLocationRoomListDto?)null);

        var result = await _service.GetRoomsByLocationAsync(Guid.NewGuid());

        await Assert.That(result).IsEmpty();
    }

    // ========== GetRoomByIdAsync ==========

    [Test]
    public async Task GetRoomByIdAsync_ReturnsRoom_WhenApiSucceeds()
    {
        var roomId = Guid.NewGuid();
        var dto = new LocationRoomDto
        {
            Id = roomId,
            Name = "Main Hall",
            Capacity = 500
        };
        var halResponse = CreateHalResourceResponse(dto);

        _apiClient.GetLocationRoomByIdAsync(roomId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(halResponse);

        var result = await _service.GetRoomByIdAsync(roomId);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Name).IsEqualTo("Main Hall");
    }

    [Test]
    public async Task GetRoomByIdAsync_ReturnsNull_WhenNotFound()
    {
        _apiClient.GetLocationRoomByIdAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException("Not found", 404));

        var result = await _service.GetRoomByIdAsync(Guid.NewGuid());

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetRoomByIdAsync_ReturnsNull_WhenApiThrows()
    {
        _apiClient.GetLocationRoomByIdAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Server error"));

        var result = await _service.GetRoomByIdAsync(Guid.NewGuid());

        await Assert.That(result).IsNull();
    }

    // ========== CreateRoomAsync ==========

    [Test]
    public async Task CreateRoomAsync_ReturnsResponse_WhenApiSucceeds()
    {
        var newId = Guid.NewGuid();
        var dto = new CreateLocationRoomDto { LocationId = Guid.NewGuid(), Name = "Room B" };
        var response = new BaseCommandResponseOfGuid { Success = true, Id = newId };

        _apiClient.CreateLocationRoomAsync(dto, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(response);

        var result = await _service.CreateRoomAsync(dto);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Success).IsTrue();
        await Assert.That(result.Id).IsEqualTo(newId);
    }

    [Test]
    public async Task CreateRoomAsync_ReturnsFailureResponse_WhenApiThrows()
    {
        var dto = new CreateLocationRoomDto { LocationId = Guid.NewGuid(), Name = "Room B" };

        _apiClient.CreateLocationRoomAsync(dto, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException("Validation failed", 400));

        var result = await _service.CreateRoomAsync(dto);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Success).IsFalse();
    }

    // ========== UpdateRoomAsync ==========

    [Test]
    public async Task UpdateRoomAsync_ReturnsResponse_WhenApiSucceeds()
    {
        var roomId = Guid.NewGuid();
        var concurrencyStamp = Guid.NewGuid();
        var dto = new UpdateLocationRoomDto { Name = new UpdateLocationRoomNameDto { Value = "Updated Hall" } };
        var response = new BaseCommandResponseOfGuid { Success = true, Id = roomId };

        _apiClient.UpdateLocationRoomAsync(roomId, dto, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(response);

        var result = await _service.UpdateRoomAsync(roomId, concurrencyStamp, dto);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Success).IsTrue();
        await _apiClient.Received(1).UpdateLocationRoomAsync(
            roomId,
            dto,
            $"\"{concurrencyStamp:D}\"",
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UpdateRoomAsync_ReturnsFailureResponse_WhenApiThrows()
    {
        var roomId = Guid.NewGuid();
        var concurrencyStamp = Guid.NewGuid();
        var dto = new UpdateLocationRoomDto { Name = new UpdateLocationRoomNameDto { Value = "Updated Hall" } };

        _apiClient.UpdateLocationRoomAsync(roomId, dto, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException("Conflict", 409));

        var result = await _service.UpdateRoomAsync(roomId, concurrencyStamp, dto);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Success).IsFalse();
    }

    // ========== DeleteRoomAsync ==========

    [Test]
    public async Task DeleteRoomAsync_ReturnsTrue_WhenApiSucceeds()
    {
        var roomId = Guid.NewGuid();

        _apiClient.DeleteLocationRoomAsync(roomId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var result = await _service.DeleteRoomAsync(roomId);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task DeleteRoomAsync_ReturnsFalse_WhenApiThrows()
    {
        _apiClient.DeleteLocationRoomAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Delete failed"));

        var result = await _service.DeleteRoomAsync(Guid.NewGuid());

        await Assert.That(result).IsFalse();
    }

    // ========== Helpers ==========

    private static HalCollectionResourceOfLocationRoomListDto CreateHalCollectionResponse(
        IList<LocationRoomListDto> items)
    {
        return new HalCollectionResourceOfLocationRoomListDto
        {
            _embedded = new HalCollectionEmbeddedOfLocationRoomListDto
            {
                Items = items.Select(ToHalResource).ToList()
            }
        };
    }

    private static HalResourceOfLocationRoomListDto ToHalResource(LocationRoomListDto item)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(item);
        return System.Text.Json.JsonSerializer.Deserialize<HalResourceOfLocationRoomListDto>(json)
               ?? new HalResourceOfLocationRoomListDto();
    }

    private static HalResourceOfLocationRoomDto CreateHalResourceResponse(LocationRoomDto dto)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(dto);
        return System.Text.Json.JsonSerializer.Deserialize<HalResourceOfLocationRoomDto>(json)
               ?? new HalResourceOfLocationRoomDto();
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
