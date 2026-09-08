namespace Event.Api.IntegrationTests.Features.Hateoas;

using Explore.API.Hateoas.Policies;
using Explore.Application.Authorization;
using Explore.Application.DTOs.EventAgendaItem;
using Explore.Application.Hateoas;

public sealed class EventAgendaItemLinkPolicyTests
{
    [Test]
    public async Task CollectionCreateLink_ShouldResolveAgendaItemResourceKind()
    {
        var link = new EventAgendaItemCollectionLinkPolicy()
            .GetCollectionLinks(user: null)
            .Single(definition => definition.Rel == "create");

        await Assert.That(link.PermissionResourceKind).IsEqualTo(ResourceKinds.EventAgendaItem);
        await Assert.That(link.PermissionAction).IsEqualTo(AuthorizationActions.Create);
    }

    [Test]
    public async Task DetailEditLink_ShouldUsePatchAndAgendaItemUpdatePermission()
    {
        var agendaItem = new EventAgendaItemDto
        {
            Id = Guid.CreateVersion7(),
            EventId = Guid.CreateVersion7(),
            Title = "Agenda item"
        };

        var link = new EventAgendaItemDetailLinkPolicy()
            .GetLinks(agendaItem, user: null)
            .Single(definition => definition.Rel == LinkRelations.Edit);

        await Assert.That(link.Method).IsEqualTo("PATCH");
        await Assert.That(link.PermissionResourceKind).IsEqualTo(ResourceKinds.EventAgendaItem);
        await Assert.That(link.PermissionAction).IsEqualTo(AuthorizationActions.Update);
    }
}
