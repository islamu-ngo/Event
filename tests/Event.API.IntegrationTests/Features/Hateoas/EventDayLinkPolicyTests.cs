namespace Event.Api.IntegrationTests.Features.Hateoas;

using Explore.API.Hateoas.Policies;
using Explore.Application.Authorization;
using Explore.Application.DTOs.EventDay;
using Explore.Application.Hateoas;

public sealed class EventDayLinkPolicyTests
{
    [Test]
    public async Task CollectionCreateLink_ShouldResolveEventDayResourceKind()
    {
        var link = new EventDayCollectionLinkPolicy()
            .GetCollectionLinks(user: null)
            .Single(definition => definition.Rel == LinkRelations.Create);

        await Assert.That(link.PermissionResourceKind).IsEqualTo(ResourceKinds.EventDay);
        await Assert.That(link.PermissionAction).IsEqualTo(AuthorizationActions.Create);
    }

    [Test]
    public async Task DetailEditLink_ShouldUsePatchAndEventDayUpdatePermission()
    {
        var eventDay = new EventDayDto
        {
            Id = Guid.CreateVersion7(),
            EventId = Guid.CreateVersion7(),
            Label = "Day one"
        };

        var link = new EventDayDetailLinkPolicy()
            .GetLinks(eventDay, user: null)
            .Single(definition => definition.Rel == LinkRelations.Edit);

        await Assert.That(link.Method).IsEqualTo("PATCH");
        await Assert.That(link.PermissionResourceKind).IsEqualTo(ResourceKinds.EventDay);
        await Assert.That(link.PermissionAction).IsEqualTo(AuthorizationActions.Update);
    }
}
