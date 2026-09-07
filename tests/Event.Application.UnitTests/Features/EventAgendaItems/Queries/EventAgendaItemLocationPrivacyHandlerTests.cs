using System.Text.Json;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventAgendaItem;
using Explore.Application.Features.EventAgendaItems.Handlers.Queries;
using Explore.Application.Features.EventAgendaItems.Requests.Queries;
using Explore.Domain;
using NSubstitute;

namespace Event.Application.UnitTests.Features.EventAgendaItems.Queries;

[Category("EventLocationPrivacy")]
public sealed class EventAgendaItemLocationPrivacyHandlerTests
{
    [Test]
    public async Task PublicDetail_RedactsPhysicalLocation()
    {
        var repository = Substitute.For<IEventAgendaItemRepository>();
        var mapper = Substitute.For<IMapper>();
        var entity = CreateEntity();
        var dto = CreateDetailDto();
        repository.GetPublicByIdAsync(entity.Id, Arg.Any<CancellationToken>()).Returns(entity);
        mapper.Map<EventAgendaItemDto>(entity).Returns(dto);
        var handler = new GetEventAgendaItemDetailRequestHandler(
            repository,
            mapper,
            Substitute.For<IEventLocationDisclosureService>());

        var result = await handler.Handle(
            new GetEventAgendaItemDetailRequest(entity.Id),
            CancellationToken.None);

        await Assert.That(result!.LocationId).IsNull();
        await Assert.That(result.RoomId).IsNull();
    }

    [Test]
    public async Task PublicByEvent_OmitsPhysicalLocationFields()
    {
        var repository = Substitute.For<IEventAgendaItemRepository>();
        var mapper = Substitute.For<IMapper>();
        var entity = CreateEntity();
        var dto = new EventAgendaItemListDto { Id = entity.Id, EventId = entity.EventId, Title = entity.Title };
        repository.GetPublicByEventAsync(entity.EventId, Arg.Any<CancellationToken>()).Returns([entity]);
        mapper.Map<List<EventAgendaItemListDto>>(Arg.Any<List<EventAgendaItem>>()).Returns([dto]);
        var handler = new GetEventAgendaItemsByEventRequestHandler(
            repository,
            mapper,
            Substitute.For<IEventLocationDisclosureService>());

        var result = await handler.Handle(
            new GetEventAgendaItemsByEventRequest(entity.EventId),
            CancellationToken.None);
        string json = JsonSerializer.Serialize(result);

        await Assert.That(json.Contains("locationId", StringComparison.OrdinalIgnoreCase)).IsFalse();
        await Assert.That(json.Contains("locationName", StringComparison.OrdinalIgnoreCase)).IsFalse();
        await Assert.That(json.Contains("roomId", StringComparison.OrdinalIgnoreCase)).IsFalse();
        await Assert.That(json.Contains("roomName", StringComparison.OrdinalIgnoreCase)).IsFalse();
    }

    [Test]
    public async Task ManagedDetail_RetainsPhysicalLocation()
    {
        var repository = Substitute.For<IEventAgendaItemRepository>();
        var mapper = Substitute.For<IMapper>();
        var entity = CreateEntity();
        var dto = CreateDetailDto();
        repository.GetById(entity.Id).Returns(entity);
        mapper.Map<EventAgendaItemDto>(entity).Returns(dto);
        var handler = new GetManagedEventAgendaItemDetailRequestHandler(repository, mapper);

        var result = await handler.Handle(
            new GetManagedEventAgendaItemDetailRequest { EventId = entity.EventId, Id = entity.Id },
            CancellationToken.None);

        await Assert.That(result!.LocationId).IsEqualTo(dto.LocationId);
        await Assert.That(result.RoomId).IsEqualTo(dto.RoomId);
    }

    private static EventAgendaItem CreateEntity() => new()
    {
        Id = Guid.NewGuid(),
        EventId = Guid.NewGuid(),
        Event = null!,
        Title = "Agenda",
        LocationId = Guid.NewGuid(),
        RoomId = Guid.NewGuid(),
        Tenant = null!
    };

    private static EventAgendaItemDto CreateDetailDto() => new()
    {
        Id = Guid.NewGuid(),
        EventId = Guid.NewGuid(),
        Title = "Agenda",
        LocationId = Guid.NewGuid(),
        RoomId = Guid.NewGuid()
    };
}
