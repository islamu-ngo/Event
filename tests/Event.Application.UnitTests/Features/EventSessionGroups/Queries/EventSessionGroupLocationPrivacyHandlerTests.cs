using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.EventSessionGroups.Handlers.Queries;
using Explore.Application.Features.EventSessionGroups.Requests.Queries;
using Explore.Domain;
using NSubstitute;

namespace Event.Application.UnitTests.Features.EventSessionGroups.Queries;

[Category("EventLocationPrivacy")]
[Category("EventSessionMapping")]
public sealed class EventSessionGroupLocationPrivacyHandlerTests
{
    [Test]
    public async Task PublicByEvent_RedactsPhysicalLocation()
    {
        var repository = Substitute.For<IEventSessionGroupRepository>();
        var eventId = Guid.Parse("01900000-0000-7000-8000-000000000001");
        var entity = CreateEntity(eventId);
        repository.GetPublicByEventAsync(eventId, Arg.Any<CancellationToken>()).Returns([entity]);
        var handler = new GetEventSessionGroupsByEventRequestHandler(
            repository,
            Substitute.For<IEventLocationDisclosureService>());

        var result = await handler.QueryAsync(
            new GetEventSessionGroupsByEventRequest { EventId = eventId },
            CancellationToken.None);

        await Assert.That(result.Single().LocationId).IsNull();
        await Assert.That(result.Single().LocationName).IsNull();
        await Assert.That(result.Single().RoomId).IsNull();
        await Assert.That(result.Single().RoomName).IsNull();
    }

    [Test]
    public async Task PublicDetail_RedactsPhysicalLocation()
    {
        var repository = Substitute.For<IEventSessionGroupRepository>();
        var eventId = Guid.Parse("01900000-0000-7000-8000-000000000001");
        var entity = CreateEntity(eventId);
        repository.GetPublicWithDetailsAsync(entity.Id, Arg.Any<CancellationToken>()).Returns(entity);
        var handler = new GetEventSessionGroupDetailRequestHandler(
            repository,
            Substitute.For<IEventLocationDisclosureService>());

        var result = await handler.QueryAsync(
            new GetEventSessionGroupDetailRequest { Id = entity.Id },
            CancellationToken.None);

        await Assert.That(result!.LocationId).IsNull();
        await Assert.That(result.LocationName).IsNull();
        await Assert.That(result.RoomId).IsNull();
        await Assert.That(result.RoomName).IsNull();
    }

    [Test]
    public async Task ManagedByEvent_RetainsPhysicalLocation()
    {
        var repository = Substitute.For<IEventSessionGroupRepository>();
        var eventId = Guid.Parse("01900000-0000-7000-8000-000000000001");
        var entity = CreateEntity(eventId);
        repository.GetActiveByEventAsync(eventId, Arg.Any<CancellationToken>()).Returns([entity]);
        var handler = new GetManagedEventSessionGroupsByEventRequestHandler(repository);

        var result = await handler.QueryAsync(
            new GetManagedEventSessionGroupsByEventRequest { EventId = eventId },
            CancellationToken.None);

        await Assert.That(result.Single().LocationId).IsEqualTo(entity.LocationId);
        await Assert.That(result.Single().LocationName).IsEqualTo(entity.Location!.FullName);
        await Assert.That(result.Single().RoomId).IsEqualTo(entity.RoomId);
        await Assert.That(result.Single().RoomName).IsEqualTo(entity.Room!.Name);
    }

    [Test]
    public async Task ManagedDetail_RetainsPhysicalLocation()
    {
        var repository = Substitute.For<IEventSessionGroupRepository>();
        var eventId = Guid.Parse("01900000-0000-7000-8000-000000000001");
        var entity = CreateEntity(eventId);
        repository.GetWithDetailsAsync(entity.Id, Arg.Any<CancellationToken>()).Returns(entity);
        var handler = new GetManagedEventSessionGroupDetailRequestHandler(repository);

        var result = await handler.QueryAsync(
            new GetManagedEventSessionGroupDetailRequest { EventId = eventId, Id = entity.Id },
            CancellationToken.None);

        await Assert.That(result!.LocationId).IsEqualTo(entity.LocationId);
        await Assert.That(result.LocationName).IsEqualTo(entity.Location!.FullName);
        await Assert.That(result.RoomId).IsEqualTo(entity.RoomId);
        await Assert.That(result.RoomName).IsEqualTo(entity.Room!.Name);
    }

    private static EventSessionGroup CreateEntity(Guid eventId)
    {
        var location = new Location
        {
            Id = Guid.Parse("01900000-0000-7000-8000-000000000002"),
            FullName = "Private venue",
            Country = "Belgium",
            City = "Brussels",
            Tenant = null!
        };
        var room = new LocationRoom
        {
            Id = Guid.Parse("01900000-0000-7000-8000-000000000003"),
            LocationId = location.Id,
            Location = location,
            Name = "Private room",
            Tenant = null!
        };

        return new EventSessionGroup
        {
            Id = Guid.Parse("01900000-0000-7000-8000-000000000004"),
            EventId = eventId,
            Event = null!,
            Name = "Group",
            LocationId = location.Id,
            Location = location,
            RoomId = room.Id,
            Room = room,
            Tenant = null!
        };
    }

}
