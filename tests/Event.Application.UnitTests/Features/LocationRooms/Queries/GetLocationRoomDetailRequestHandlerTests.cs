using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.LocationRooms.Handlers.Queries;
using Explore.Application.Features.LocationRooms.Requests.Queries;
using Explore.Domain;

namespace Event.Application.UnitTests.Features.LocationRooms.Queries;

public class GetLocationRoomDetailRequestHandlerTests
{
    [Test]
    public async Task QueryAsync_WithExistingRoom_ReturnsDto()
    {
        var roomId = Guid.Parse("01900000-0000-7000-8000-000000000091");
        var room = RoomQueryStore.Room(roomId, "Main Hall");
        var handler = new GetLocationRoomDetailRequestHandler(new RoomQueryStore(room));
        var result = await handler.QueryAsync(new GetLocationRoomDetailRequest { Id = roomId }, CancellationToken.None);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Id).IsEqualTo(roomId);
        await Assert.That(result.Name).IsEqualTo("Main Hall");
        await Assert.That(result.LocationId).IsEqualTo(RoomQueryStore.ParentId);
        await Assert.That(result.Capacity).IsEqualTo(120);
        room.Name = "Changed";
        await Assert.That(result.Name).IsEqualTo("Main Hall");
    }

    [Test]
    public async Task QueryAsync_WithNonExistentRoom_ReturnsNull()
    {
        var handler = new GetLocationRoomDetailRequestHandler(new RoomQueryStore());
        var result = await handler.QueryAsync(new GetLocationRoomDetailRequest
        {
            Id = Guid.Parse("01900000-0000-7000-8000-000000000091")
        }, CancellationToken.None);
        await Assert.That(result).IsNull();
    }
}

internal sealed class RoomQueryStore(params LocationRoom[] rooms) : ILocationRoomRepository
{
    internal static readonly Guid ParentId = Guid.Parse("01900000-0000-7000-8000-000000000093");

    internal static LocationRoom Room(Guid id, string name, int sortOrder = 0) => new()
    {
        Id = id, LocationId = ParentId, Name = name, Capacity = 120, SortOrder = sortOrder, Location = null!, Tenant = null!,
        TenantId = Guid.Parse("01900000-0000-7000-8000-000000000094"),
        ConcurrencyStamp = Guid.Parse("01900000-0000-7000-8000-000000000095")
    };

    public Task<LocationRoom?> GetById(Guid id) => Task.FromResult(rooms.SingleOrDefault(room => room.Id == id));
    public Task<IReadOnlyList<LocationRoom>> GetAll() => Task.FromResult<IReadOnlyList<LocationRoom>>(rooms);
    public Task<bool> Exists(Guid id) => Task.FromResult(rooms.Any(room => room.Id == id));

    public Task<List<LocationRoom>> GetByLocationAsync(Guid locationId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(rooms.Where(room => room.LocationId == locationId).OrderBy(room => room.SortOrder).ToList());
    }

    public Task<IReadOnlyList<LocationRoom>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<LocationRoom>>(rooms.Where(room => ids.Contains(room.Id)).ToArray());
    }

    public Task<(IReadOnlyList<LocationRoom> Items, int TotalCount)> GetAllPaged(int pageNumber, int pageSize) => throw new NotSupportedException();
    public Task<bool> HasScheduleReferencesAsync(Guid roomId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task MoveToLocationAsync(LocationRoom room, Location location, string name, Guid expectedConcurrencyStamp,
        CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<LocationRoom> Create(LocationRoom entity) => throw new NotSupportedException();
    public Task Update(LocationRoom entity) => throw new NotSupportedException();
    public Task Delete(LocationRoom entity) => throw new NotSupportedException();
}
