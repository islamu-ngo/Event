using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.OrganizerPaymentConnections;
using Explore.Application.Hateoas;

namespace Explore.API.Hateoas.Policies;

public sealed class OrganizerPaymentConnectionLinkPolicy : ILinkPolicy<EventOrganizerPaymentConnectionManagementDto>
{
    public IEnumerable<LinkDefinition> GetLinks(EventOrganizerPaymentConnectionManagementDto dto, ClaimsPrincipal? user)
    {
        yield return LinkDefinition.Self(RouteNames.GetEventOrganizerPaymentConnection, new { eventId = dto.EventId });
        yield return PaidCommerce(new LinkDefinition(
            LinkRelations.StartOnboarding,
            RouteNames.StartEventOrganizerPaymentOnboarding,
            new { eventId = dto.EventId },
            HttpMethods.Post,
            "Start organizer payment onboarding",
            RequiresAuth: true), dto);
    }

    private static LinkDefinition PaidCommerce(LinkDefinition link, EventOrganizerPaymentConnectionManagementDto dto) =>
        link.RequirePermission(
            AuthorizationActions.Events.ManagePaidEventCommerce,
            ResourceKinds.Event,
            dto.EventId.ToString("D"),
            new AuthorizationScope(TenantId: dto.TenantId.ToString("D")),
            BuildEventFacts(dto));

    private static IAuthorizationFacts BuildEventFacts(EventOrganizerPaymentConnectionManagementDto dto) =>
        new EventAuthorizationFacts(
            dto.TenantId,
            dto.EventId,
            dto.ActorId,
            dto.ActorUserId,
            dto.ActorOrganizationId,
            dto.ActorGroupId,
            dto.OrganizerActorId,
            dto.OrganizerUserId,
            dto.OrganizerOrganizationId,
            dto.OrganizerGroupId,
            ProvenanceType: null,
            SubmittedByUserId: null);
}

public sealed class OrganizerPaymentConnectionCollectionLinkPolicy : ICollectionLinkPolicy<EventOrganizerPaymentConnectionManagementDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(EventOrganizerPaymentConnectionManagementDto dto, ClaimsPrincipal? user) => [];
    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}
