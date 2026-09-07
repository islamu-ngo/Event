using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.EventRoleAssignment;
using Explore.Application.Hateoas;
using Explore.Domain.Enums;

namespace Explore.API.Hateoas.Policies;

/// <summary>
/// Team membership exposes no detail affordances; revocation is an item link on the collection.
/// The contract's empty default supplies that.
/// </summary>
public sealed class EventTeamMemberDetailLinkPolicy : ILinkPolicy<EventTeamMemberDto>;

public sealed class EventTeamMemberCollectionLinkPolicy : ICollectionLinkPolicy<EventTeamMemberDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(EventTeamMemberDto dto, ClaimsPrincipal? user)
    {
        if (dto.TenantId == Guid.Empty
            || dto.EventId == Guid.Empty
            || dto.AssignmentId == Guid.Empty
            || !dto.IsEffective
            || dto.RoleId == (int)RoleEnum.EventOwner)
        {
            yield break;
        }

        yield return ManageTeam(
            new LinkDefinition(
                LinkRelations.Revoke,
                RouteNames.RevokeEventRole,
                new { eventId = dto.EventId, assignmentId = dto.AssignmentId },
                HttpMethods.Delete,
                "Revoke event role",
                RequiresAuth: true),
            dto.TenantId,
            dto.EventId);
    }

    public IEnumerable<LinkDefinition> GetCollectionLinks(
        ClaimsPrincipal? user,
        ICollectionAuthorizationContext? authorizationContext)
    {
        if (authorizationContext is not EventTeamCollectionAuthorizationContext context
            || context.TenantId == Guid.Empty
            || context.EventId == Guid.Empty)
        {
            yield break;
        }

        yield return ManageTeam(
            new LinkDefinition(
                LinkRelations.AssignEventRole,
                RouteNames.AssignEventRole,
                new { eventId = context.EventId },
                HttpMethods.Post,
                "Assign event role",
                RequiresAuth: true),
            context.TenantId,
            context.EventId);
    }

    private static LinkDefinition ManageTeam(LinkDefinition link, Guid tenantId, Guid eventId) =>
        link.RequirePermission(
            AuthorizationActions.Events.ManageTeam,
            ResourceKinds.Event,
            eventId.ToString("D"),
            scope: new AuthorizationScope(TenantId: tenantId.ToString("D")));
}

public sealed record EventTeamCollectionAuthorizationContext(
    Guid TenantId,
    Guid EventId) : ICollectionAuthorizationContext
{
    public string AuthorizationResourceId => EventId.ToString("D");

    // The evaluator reloads the event to build full authority facts for manage-team candidates, so the
    // collection context deliberately publishes none of its own.
    public IAuthorizationFacts? AuthorizationFacts => null;
}
