using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.Location;
using Explore.Application.Hateoas;

namespace Explore.API.Hateoas.Policies;

public sealed class EventLocationManagementLinkPolicy(IHttpContextAccessor httpContextAccessor)
    : ILinkPolicy<EventLocationManagementDto>
{
    public IEnumerable<LinkDefinition> GetLinks(
        EventLocationManagementDto dto,
        ClaimsPrincipal? user)
    {
        if (dto.EventLocationId == Guid.Empty
            || !TryGetEventId(out Guid eventId)
            || !TryGetUpdateAuthorization(dto, eventId, out AuthorizationRequest authorization))
        {
            yield break;
        }

        yield return new LinkDefinition(
            LinkRelations.Edit,
            RouteNames.UpdateEventLocationDisclosure,
            new { eventId, eventLocationId = dto.EventLocationId },
            HttpMethods.Patch,
            "Update location disclosure",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Update,
                ResourceKinds.Event,
                eventId.ToString("D"),
                authorization.Scope,
                authorization.Facts);

        if (dto.NeedsPrivacyReview)
        {
            yield return new LinkDefinition(
                LinkRelations.RemediateLocation,
                RouteNames.ConfirmEventLocationRemediation,
                new { eventId, eventLocationId = dto.EventLocationId },
                HttpMethods.Post,
                "Confirm location privacy remediation",
                RequiresAuth: true)
                .RequirePermission(AuthorizationActions.Update,
                    ResourceKinds.Event,
                    eventId.ToString("D"),
                    authorization.Scope,
                    authorization.Facts);
        }
    }

    private bool TryGetEventId(out Guid eventId)
    {
        object? routeValue = httpContextAccessor.HttpContext?.Request.RouteValues["eventId"];
        if (routeValue is Guid guid && guid != Guid.Empty)
        {
            eventId = guid;
            return true;
        }

        eventId = Guid.Empty;
        return routeValue is string value
            && Guid.TryParse(value, out eventId)
            && eventId != Guid.Empty;
    }

    private static bool TryGetUpdateAuthorization(
        EventLocationManagementDto dto,
        Guid eventId,
        out AuthorizationRequest authorization)
    {
        authorization = dto.UpdateAuthorization!;

        // The disclosure DTO carries the exact request the handler already authorized. Re-emitting it as a
        // link is only safe when it still describes this event: same capability, same resource id, and
        // trusted event facts whose tenant matches the scope the check will be evaluated under.
        if (authorization is null
            || authorization.ResourceKind != ResourceKinds.Event
            || authorization.Action != AuthorizationActions.Update
            || !Guid.TryParse(authorization.ResourceId, out Guid authorizationEventId)
            || authorizationEventId != eventId
            || authorization.Facts is not EventAuthorizationFacts facts
            || facts.EventId != eventId
            || facts.TenantId == Guid.Empty
            || facts.ActorId == Guid.Empty
            || !Guid.TryParse(authorization.Scope?.TenantId, out Guid scopeTenantId)
            || scopeTenantId != facts.TenantId)
        {
            authorization = null!;
            return false;
        }

        return true;
    }
}

public sealed class EventLocationManagementCollectionLinkPolicy(
    ILinkPolicy<EventLocationManagementDto> detailPolicy)
    : ICollectionLinkPolicy<EventLocationManagementDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(
        EventLocationManagementDto dto,
        ClaimsPrincipal? user) => detailPolicy.GetLinks(dto, user);

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}
