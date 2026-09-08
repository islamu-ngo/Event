using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.EventTicketing;
using Explore.Application.Hateoas;

namespace Explore.API.Hateoas.Policies;

public sealed class PaidEventPublicationPreflightLinkPolicy : ILinkPolicy<PaidEventPublicationPreflightDto>
{
    public IEnumerable<LinkDefinition> GetLinks(PaidEventPublicationPreflightDto dto, ClaimsPrincipal? user)
    {
        yield return LinkDefinition.Self(RouteNames.GetPaidEventPublicationPreflight, new { eventId = dto.EventId });
        if (!dto.IsReady)
        {
            yield break;
        }

        LinkDefinition publish = new(
            LinkRelations.Publish,
            RouteNames.PublishEventTicketCatalog,
            new { eventId = dto.EventId },
            HttpMethods.Post,
            "Publish ticket catalog",
            RequiresAuth: true);
        yield return dto.IsPaidCatalog
            ? publish.RequirePermission(AuthorizationActions.Events.ManagePaidEventCommerce, ResourceKinds.Event, dto.EventId.ToString("D"), BuildScope(dto), BuildEventFacts(dto))
            : publish.RequirePermission(AuthorizationActions.Events.ManageTickets, ResourceKinds.Event, dto.EventId.ToString("D"), BuildScope(dto), BuildEventFacts(dto));
    }

    private static IAuthorizationFacts BuildEventFacts(PaidEventPublicationPreflightDto dto) =>
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

    private static AuthorizationScope BuildScope(PaidEventPublicationPreflightDto dto) => new(TenantId: dto.TenantId.ToString("D"));
}

public sealed class PaidEventPublicationPreflightCollectionLinkPolicy : ICollectionLinkPolicy<PaidEventPublicationPreflightDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(PaidEventPublicationPreflightDto dto, ClaimsPrincipal? user) => [];
    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}
