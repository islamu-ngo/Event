using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventSessionGroup;
using Explore.Application.Features.EventSessionGroups.Handlers.Queries;
using Explore.Application.Features.EventSessionGroups.Requests.Queries;
using Explore.Domain;
using NSubstitute;

namespace Event.Application.UnitTests.Features.EventSessionGroups.Queries;

[Category("EventLocationPrivacy")]
public sealed class EventSessionGroupLocationPrivacyHandlerTests
{
    [Test]
    public async Task PublicByEvent_RedactsPhysicalLocation()
    {
        var repository = Substitute.For<IEventSessionGroupRepository>();
        var mapper = Substitute.For<IMapper>();
        var eventId = Guid.NewGuid();
        var entity = CreateEntity(eventId);
        var dto = CreateListDto(eventId);
        dto = dto with { Id = entity.Id };
        repository.GetPublicByEventAsync(eventId, Arg.Any<CancellationToken>()).Returns([entity]);
        mapper.Map<List<EventSessionGroupListDto>>(Arg.Any<List<EventSessionGroup>>()).Returns([dto]);
        var handler = new GetEventSessionGroupsByEventRequestHandler(
            repository,
            mapper,
            Substitute.For<IEventLocationDisclosureService>());

        var result = await handler.Handle(
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
        var mapper = Substitute.For<IMapper>();
        var eventId = Guid.NewGuid();
        var entity = CreateEntity(eventId);
        var dto = CreateDetailDto(eventId);
        repository.GetPublicWithDetailsAsync(entity.Id, Arg.Any<CancellationToken>()).Returns(entity);
        mapper.Map<EventSessionGroupDto>(entity).Returns(dto);
        var handler = new GetEventSessionGroupDetailRequestHandler(
            repository,
            mapper,
            Substitute.For<IEventLocationDisclosureService>());

        var result = await handler.Handle(
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
        var mapper = Substitute.For<IMapper>();
        var eventId = Guid.NewGuid();
        var entity = CreateEntity(eventId);
        var dto = CreateListDto(eventId);
        repository.GetActiveByEventAsync(eventId, Arg.Any<CancellationToken>()).Returns([entity]);
        mapper.Map<List<EventSessionGroupListDto>>(Arg.Any<List<EventSessionGroup>>()).Returns([dto]);
        var handler = new GetManagedEventSessionGroupsByEventRequestHandler(repository, mapper);

        var result = await handler.Handle(
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
        var mapper = Substitute.For<IMapper>();
        var eventId = Guid.NewGuid();
        var entity = CreateEntity(eventId);
        var dto = CreateDetailDto(eventId);
        repository.GetWithDetailsAsync(entity.Id, Arg.Any<CancellationToken>()).Returns(entity);
        mapper.Map<EventSessionGroupDto>(entity).Returns(dto);
        var handler = new GetManagedEventSessionGroupDetailRequestHandler(repository, mapper);

        var result = await handler.Handle(
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
            Id = Guid.NewGuid(),
            FullName = "Private venue",
            Country = "Belgium",
            City = "Brussels",
            Tenant = null!
        };
        var room = new LocationRoom
        {
            Id = Guid.NewGuid(),
            LocationId = location.Id,
            Location = location,
            Name = "Private room",
            Tenant = null!
        };

        return new EventSessionGroup
        {
            Id = Guid.NewGuid(),
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

    private static EventSessionGroupListDto CreateListDto(Guid eventId) => new()
    {
        Id = Guid.NewGuid(),
        EventId = eventId,
        Name = "Group",
        LocationId = Guid.NewGuid(),
        LocationName = "Private venue",
        RoomId = Guid.NewGuid(),
        RoomName = "Private room"
    };

    private static EventSessionGroupDto CreateDetailDto(Guid eventId) => new()
    {
        Id = Guid.NewGuid(),
        EventId = eventId,
        Name = "Group",
        LocationId = Guid.NewGuid(),
        LocationName = "Private venue",
        RoomId = Guid.NewGuid(),
        RoomName = "Private room"
    };
}
