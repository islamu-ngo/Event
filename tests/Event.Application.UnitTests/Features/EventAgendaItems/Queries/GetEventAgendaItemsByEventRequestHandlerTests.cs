using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.EventAgendaItems.Handlers.Queries;
using Explore.Application.Features.EventAgendaItems.Requests.Queries;
using Explore.Domain;
using NSubstitute;

namespace Event.Application.UnitTests.Features.EventAgendaItems.Queries;

[Category("EventAgendaMapping")]
public class GetEventAgendaItemsByEventRequestHandlerTests
{
    [Test]
    public async Task Handle_WithExistingAgendaItems_ReturnsMappedList()
    {
        var eventId = Guid.Parse("01900000-0000-7000-8000-000000000001");
        var items = new List<EventAgendaItem>
        {
            new() { Id = Guid.Parse("01900000-0000-7000-8000-000000000002"), Title = "Item 1", EventId = eventId, Event = null!, Tenant = null! },
            new() { Id = Guid.Parse("01900000-0000-7000-8000-000000000003"), Title = "Item 2", EventId = eventId, Event = null!, Tenant = null! },
            new() { Id = Guid.Parse("01900000-0000-7000-8000-000000000004"), Title = "Item 3", EventId = eventId, Event = null!, Tenant = null! }
        };
        var repository = Substitute.For<IEventAgendaItemRepository>();
        repository.GetPublicByEventAsync(eventId, Arg.Any<CancellationToken>()).Returns(items);
        var handler = new GetEventAgendaItemsByEventRequestHandler(repository, Substitute.For<IEventLocationDisclosureService>());
        var result = await handler.QueryAsync(new GetEventAgendaItemsByEventRequest(eventId), CancellationToken.None);
        items[0].Title = "Changed";
        items.Clear();
        await Assert.That(result.Count).IsEqualTo(3);
        await Assert.That(result[0].Title).IsEqualTo("Item 1");
        await Assert.That(result[1].Title).IsEqualTo("Item 2");
        await Assert.That(result[2].Title).IsEqualTo("Item 3");
    }

    [Test]
    public async Task Handle_WithNoAgendaItems_ReturnsEmptyList()
    {
        var eventId = Guid.Parse("01900000-0000-7000-8000-000000000001");
        var repository = Substitute.For<IEventAgendaItemRepository>();
        repository.GetPublicByEventAsync(eventId, Arg.Any<CancellationToken>()).Returns(new List<EventAgendaItem>());
        var handler = new GetEventAgendaItemsByEventRequestHandler(repository, Substitute.For<IEventLocationDisclosureService>());
        var result = await handler.QueryAsync(new GetEventAgendaItemsByEventRequest(eventId), CancellationToken.None);
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Count).IsEqualTo(0);
    }
}
