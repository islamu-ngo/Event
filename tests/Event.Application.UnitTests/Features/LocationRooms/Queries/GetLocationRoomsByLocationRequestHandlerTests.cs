using Explore.Application.Features.LocationRooms.Handlers.Queries;
using Explore.Application.Features.LocationRooms.Requests.Queries;

namespace Event.Application.UnitTests.Features.LocationRooms.Queries;

public class GetLocationRoomsByLocationRequestHandlerTests
{
    [Test]
    public async Task Handle_WithExistingRooms_ReturnsMappedList()
    {
        var first = RoomQueryStore.Room(Guid.Parse("01900000-0000-7000-8000-000000000091"), "Room A", 1);
        var second = RoomQueryStore.Room(Guid.Parse("01900000-0000-7000-8000-000000000092"), "Room B", 2);
        var unrelated = RoomQueryStore.Room(Guid.Parse("01900000-0000-7000-8000-000000000096"), "Other venue");
        unrelated.LocationId = Guid.Parse("01900000-0000-7000-8000-000000000097");
        var handler = new GetLocationRoomsByLocationRequestHandler(new RoomQueryStore(second, unrelated, first));
        var result = await handler.Handle(new GetLocationRoomsByLocationRequest
        {
            LocationId = RoomQueryStore.ParentId
        }, CancellationToken.None);

        await Assert.That(result.Count).IsEqualTo(2);
        await Assert.That(result[0].Name).IsEqualTo("Room A");
        await Assert.That(result[1].Name).IsEqualTo("Room B");
        await Assert.That(result[0].Id).IsEqualTo(Guid.Parse("01900000-0000-7000-8000-000000000091"));
        await Assert.That(result[1].Id).IsEqualTo(Guid.Parse("01900000-0000-7000-8000-000000000092"));
        first.Name = "Changed";
        await Assert.That(result[0].Name).IsEqualTo("Room A");
    }

    [Test]
    public async Task Handle_WithNoRooms_ReturnsEmptyList()
    {
        var handler = new GetLocationRoomsByLocationRequestHandler(new RoomQueryStore());
        var result = await handler.Handle(new GetLocationRoomsByLocationRequest
        {
            LocationId = RoomQueryStore.ParentId
        }, CancellationToken.None);
        await Assert.That(result).IsEmpty();
    }
}
