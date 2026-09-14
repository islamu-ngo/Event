using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.EventAgendaItems.Handlers.Queries;
using Explore.Application.Features.EventAgendaItems.Requests.Queries;
using Explore.Domain;
using NSubstitute;

namespace Event.Application.UnitTests.Features.EventAgendaItems.Queries;

[Category("EventLocationPrivacy")]
[Category("EventAgendaMapping")]
public sealed class EventAgendaItemLocationPrivacyHandlerTests
{
    [Test]
    public async Task PublicDetail_RedactsPhysicalLocation()
    {
        var repository = Substitute.For<IEventAgendaItemRepository>();
        var entity = CreateEntity();
        repository.GetPublicByIdAsync(entity.Id, Arg.Any<CancellationToken>()).Returns(entity);
        var handler = new GetEventAgendaItemDetailRequestHandler(repository, Substitute.For<IEventLocationDisclosureService>());
        var result = await handler.QueryAsync(new GetEventAgendaItemDetailRequest(entity.Id), CancellationToken.None);
        await Assert.That(result!.LocationId).IsNull();
        await Assert.That(result.RoomId).IsNull();
    }

    [Test]
    public async Task PublicByEvent_OmitsPhysicalLocationFields()
    {
        var repository = Substitute.For<IEventAgendaItemRepository>();
        var entity = CreateEntity();
        repository.GetPublicByEventAsync(entity.EventId, Arg.Any<CancellationToken>()).Returns([entity]);
        var handler = new GetEventAgendaItemsByEventRequestHandler(repository, Substitute.For<IEventLocationDisclosureService>());
        var result = await handler.QueryAsync(new GetEventAgendaItemsByEventRequest(entity.EventId), CancellationToken.None);
        string json = JsonSerializer.Serialize(result);
        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(json.Contains("locationId", StringComparison.OrdinalIgnoreCase)).IsFalse();
        await Assert.That(json.Contains("locationName", StringComparison.OrdinalIgnoreCase)).IsFalse();
        await Assert.That(json.Contains("roomId", StringComparison.OrdinalIgnoreCase)).IsFalse();
        await Assert.That(json.Contains("roomName", StringComparison.OrdinalIgnoreCase)).IsFalse();
    }

    [Test]
    public async Task ManagedDetail_RetainsPhysicalLocation()
    {
        var repository = Substitute.For<IEventAgendaItemRepository>();
        var entity = CreateEntity();
        repository.GetById(entity.Id).Returns(entity);
        var handler = new GetManagedEventAgendaItemDetailRequestHandler(repository);
        var result = await handler.QueryAsync(new GetManagedEventAgendaItemDetailRequest { EventId = entity.EventId, Id = entity.Id }, CancellationToken.None);
        await Assert.That(result!.LocationId).IsEqualTo(Guid.Parse("01900000-0000-7000-8000-000000000003"));
        await Assert.That(result.RoomId).IsEqualTo(Guid.Parse("01900000-0000-7000-8000-000000000004"));
    }

    private static EventAgendaItem CreateEntity() => new()
    {
        Id = Guid.Parse("01900000-0000-7000-8000-000000000001"),
        EventId = Guid.Parse("01900000-0000-7000-8000-000000000002"),
        Event = null!, Title = "Agenda", Tenant = null!,
        LocationId = Guid.Parse("01900000-0000-7000-8000-000000000003"),
        RoomId = Guid.Parse("01900000-0000-7000-8000-000000000004")
    };
}
