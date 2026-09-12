using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.EventAgendaItems.Handlers.Queries;
using Explore.Application.Features.EventAgendaItems.Requests.Queries;
using Explore.Domain;
using NSubstitute;

namespace Event.Application.UnitTests.Features.EventAgendaItems.Queries;

[Category("EventAgendaMapping")]
public class GetEventAgendaItemDetailRequestHandlerTests
{
    [Test]
    public async Task Handle_WithExistingAgendaItem_ReturnsDto()
    {
        var id = Guid.Parse("01900000-0000-7000-8000-000000000001");
        var repository = Substitute.For<IEventAgendaItemRepository>();
        var item = new EventAgendaItem { Id = id, Title = "Opening Ceremony", Event = null!, Tenant = null! };
        repository.GetPublicByIdAsync(id, Arg.Any<CancellationToken>()).Returns(item);
        var handler = new GetEventAgendaItemDetailRequestHandler(repository, Substitute.For<IEventLocationDisclosureService>());
        var result = await handler.Handle(new GetEventAgendaItemDetailRequest(id), CancellationToken.None);
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Id).IsEqualTo(id);
        await Assert.That(result.Title).IsEqualTo("Opening Ceremony");
        await Assert.That(result.EventTitle).IsNull();
    }

    [Test]
    public async Task Handle_WithNonExistentAgendaItem_ReturnsNull()
    {
        var repository = Substitute.For<IEventAgendaItemRepository>();
        var handler = new GetEventAgendaItemDetailRequestHandler(repository, Substitute.For<IEventLocationDisclosureService>());
        var result = await handler.Handle(new GetEventAgendaItemDetailRequest(Guid.Parse("01900000-0000-7000-8000-000000000001")), CancellationToken.None);
        await Assert.That(result).IsNull();
    }
}
