using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.Authorization;
using Explore.Application.DTOs.EventSessionGroup;
using Explore.Application.Hateoas;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features.Hateoas;

/// <summary>
/// HATEOAS contract tests for event program section links.
/// </summary>
public class EventSessionGroupHateoasTests
{
    [Test]
    public async Task CollectionItemLinks_ShouldExposeDeleteAffordanceWithEventScope()
    {
        var eventId = Guid.NewGuid();
        var sectionId = Guid.NewGuid();
        var dto = new EventSessionGroupListDto
        {
            Id = sectionId,
            EventId = eventId,
            Name = "Main stage",
            Slug = "main-stage",
            SortOrder = 10,
            IsPublished = true,
            TenantId = Guid.NewGuid()
        };

        var policy = new EventSessionGroupCollectionLinkPolicy();

        var links = policy.GetItemLinks(dto, user: null).ToList();

        var deleteLink = links.Single(link => link.Rel == LinkRelations.Delete);
        await Assert.That(deleteLink.RouteName).IsEqualTo(RouteNames.DeleteEventSessionGroup);
        await Assert.That(deleteLink.Method).IsEqualTo("DELETE");
        await Assert.That(deleteLink.Title).IsEqualTo("Delete program section");
        await Assert.That(deleteLink.RequiresAuth).IsTrue();
        await Assert.That(deleteLink.PermissionResourceKind).IsEqualTo(ResourceKinds.EventSessionGroup);
        await Assert.That(deleteLink.PermissionAction).IsEqualTo(AuthorizationActions.Delete);
        await Assert.That(GetRouteValue<Guid>(deleteLink.RouteValues, "id")).IsEqualTo(sectionId);
        await Assert.That(GetRouteValue<Guid>(deleteLink.RouteValues, "eventId")).IsEqualTo(eventId);
    }

    [Test]
    public async Task CollectionItemLinks_ShouldExposeAssignSessionAffordanceWithUpdatePermission()
    {
        var eventId = Guid.NewGuid();
        var sectionId = Guid.NewGuid();
        var dto = new EventSessionGroupListDto
        {
            Id = sectionId,
            EventId = eventId,
            Name = "Workshops",
            Slug = "workshops",
            SortOrder = 20,
            IsPublished = true,
            TenantId = Guid.NewGuid()
        };

        var policy = new EventSessionGroupCollectionLinkPolicy();

        var links = policy.GetItemLinks(dto, user: null).ToList();

        var assignLink = links.Single(link => link.Rel == LinkRelations.AssignSession);
        await Assert.That(assignLink.RouteName).IsEqualTo(RouteNames.AssignEventSessionToGroup);
        await Assert.That(assignLink.Method).IsEqualTo("POST");
        await Assert.That(assignLink.Title).IsEqualTo("Assign sessions to this program section");
        await Assert.That(assignLink.RequiresAuth).IsTrue();
        await Assert.That(assignLink.PermissionResourceKind).IsEqualTo(ResourceKinds.EventSessionGroup);
        await Assert.That(assignLink.PermissionAction).IsEqualTo(AuthorizationActions.Update);
        await Assert.That(GetRouteValue<Guid>(assignLink.RouteValues, "id")).IsEqualTo(sectionId);
    }

    private static T? GetRouteValue<T>(object? routeValues, string name)
    {
        if (routeValues is null)
        {
            return default;
        }

        var property = routeValues.GetType().GetProperty(name);
        if (property is null)
        {
            return default;
        }

        var value = property.GetValue(routeValues);
        return value is T typedValue ? typedValue : default;
    }
}
