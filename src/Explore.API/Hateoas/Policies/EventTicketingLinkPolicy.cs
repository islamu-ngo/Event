using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.EventTicketing;
using Explore.Application.Hateoas;
using Explore.Domain.Enums;

namespace Explore.API.Hateoas.Policies;

public sealed class EventTicketCatalogManagementLinkPolicy : ILinkPolicy<EventTicketCatalogManagementDto>
{
    public IEnumerable<LinkDefinition> GetLinks(
        EventTicketCatalogManagementDto dto,
        ClaimsPrincipal? user)
    {
        yield return LinkDefinition.Self(
            RouteNames.GetEventTicketCatalogManagement,
            new { eventId = dto.EventId });

        yield return new LinkDefinition(
            LinkRelations.Event,
            RouteNames.GetEventManagementDetails,
            new { id = dto.EventId },
            HttpMethods.Get);

        if (dto.EventId == Guid.Empty)
        {
            yield break;
        }

        if (dto.CatalogId is null)
        {
            yield return PaidCommerce(
                new LinkDefinition(
                    LinkRelations.PaymentConnection,
                    RouteNames.GetEventOrganizerPaymentConnection,
                    new { eventId = dto.EventId },
                    HttpMethods.Get,
                    "View organizer payment connection",
                    RequiresAuth: true),
                dto);
            yield return Manage(
                new LinkDefinition(
                    LinkRelations.CreateDraft,
                    RouteNames.CreateEventTicketCatalogDraft,
                    new { eventId = dto.EventId },
                    HttpMethods.Post,
                    "Create ticket catalog draft",
                    RequiresAuth: true),
                dto);
            yield break;
        }

        if (dto.StatusId == (int)TicketCatalogStatusEnum.Draft)
        {
            yield return Manage(
                new LinkDefinition(
                    LinkRelations.Preflight,
                    RouteNames.GetPaidEventPublicationPreflight,
                    new { eventId = dto.EventId },
                    HttpMethods.Get,
                    "Check paid publication readiness",
                    RequiresAuth: true),
                dto);
            if (dto.PublicationPreflight?.IsPaidCatalog == true)
            {
                yield return PaidCommerce(
                    new LinkDefinition(
                        LinkRelations.CommercialDisclosures,
                        RouteNames.UpdateEventTicketCatalogCommercialDisclosures,
                        new { eventId = dto.EventId },
                        HttpMethods.Put,
                        "Update commercial disclosures",
                        RequiresAuth: true),
                    dto);
            }
            yield return PaidCommerce(
                new LinkDefinition(
                    LinkRelations.PaymentConnection,
                    RouteNames.GetEventOrganizerPaymentConnection,
                    new { eventId = dto.EventId },
                    HttpMethods.Get,
                    "View organizer payment connection",
                    RequiresAuth: true),
                dto);
            yield return PaidCommerce(
                new LinkDefinition(
                    LinkRelations.StartOnboarding,
                    RouteNames.StartEventOrganizerPaymentOnboarding,
                    new { eventId = dto.EventId },
                    HttpMethods.Post,
                    "Start organizer payment onboarding",
                    RequiresAuth: true),
                dto);
            yield return Manage(
                new LinkDefinition(
                    LinkRelations.CreateTicketType,
                    RouteNames.CreateEventTicketType,
                    new { eventId = dto.EventId },
                    HttpMethods.Post,
                    "Create ticket type",
                    RequiresAuth: true),
                dto);
            yield return Manage(
                new LinkDefinition(
                    LinkRelations.CreateCapacityPool,
                    RouteNames.CreateEventCapacityPool,
                    new { eventId = dto.EventId },
                    HttpMethods.Post,
                    "Create capacity pool",
                    RequiresAuth: true),
                dto);
            if (dto.PublicationPreflight?.IsPaidCatalog == true)
            {
                if (dto.PublicationPreflight.IsReady)
                {
                    yield return PaidCommerce(PublishLink(dto.EventId), dto);
                }
            }
            else
            {
                yield return Manage(PublishLink(dto.EventId), dto);
            }
        }
        else if (dto.StatusId == (int)TicketCatalogStatusEnum.Published)
        {
            yield return Manage(
                new LinkDefinition(
                    LinkRelations.CloneDraft,
                    RouteNames.CloneEventTicketCatalogDraft,
                    new { eventId = dto.EventId },
                    HttpMethods.Post,
                    "Clone ticket catalog draft",
                    RequiresAuth: true),
                dto);
        }
    }

    public IEnumerable<LinkDefinition> GetTicketTypeLinks(
        EventTicketCatalogManagementDto catalog,
        EventTicketTypeDto ticketType) =>
        catalog.StatusId == (int)TicketCatalogStatusEnum.Draft
            ? GetDraftTicketTypeLinks(catalog, ticketType.Id)
            : [];

    public IEnumerable<LinkDefinition> GetCapacityPoolLinks(
        EventTicketCatalogManagementDto catalog,
        EventCapacityPoolDto capacityPool) =>
        catalog.StatusId == (int)TicketCatalogStatusEnum.Draft
            ? GetDraftCapacityPoolLinks(catalog, capacityPool.Id)
            : [];

    private static IEnumerable<LinkDefinition> GetDraftTicketTypeLinks(
        EventTicketCatalogManagementDto catalog,
        Guid ticketTypeId)
    {
        Guid eventId = catalog.EventId;
        yield return Manage(
            new LinkDefinition(
                LinkRelations.Edit,
                RouteNames.UpdateEventTicketType,
                new { eventId, ticketTypeId },
                HttpMethods.Put,
                "Update ticket type",
                RequiresAuth: true),
            catalog);
        yield return Manage(
            LinkDefinition.Delete(
                RouteNames.DeleteEventTicketType,
                new { eventId, ticketTypeId }),
            catalog);
    }

    private static IEnumerable<LinkDefinition> GetDraftCapacityPoolLinks(
        EventTicketCatalogManagementDto catalog,
        Guid capacityPoolId)
    {
        Guid eventId = catalog.EventId;
        yield return Manage(
            new LinkDefinition(
                LinkRelations.Edit,
                RouteNames.UpdateEventCapacityPool,
                new { eventId, capacityPoolId },
                HttpMethods.Put,
                "Update capacity pool",
                RequiresAuth: true),
            catalog);
        yield return Manage(
            LinkDefinition.Delete(
                RouteNames.DeleteEventCapacityPool,
                new { eventId, capacityPoolId }),
            catalog);
    }

    private static LinkDefinition Manage(
        LinkDefinition link,
        EventTicketCatalogManagementDto catalog) =>
        link.RequirePermission(AuthorizationActions.Events.ManageTickets,
            ResourceKinds.Event,
            catalog.EventId.ToString("D"),
            new AuthorizationScope(TenantId: catalog.TenantId.ToString("D")),
            BuildEventFacts(catalog));

    private static LinkDefinition PublishLink(Guid eventId) => new(
        LinkRelations.Publish,
        RouteNames.PublishEventTicketCatalog,
        new { eventId },
        HttpMethods.Post,
        "Publish ticket catalog",
        RequiresAuth: true);

    private static LinkDefinition PaidCommerce(
        LinkDefinition link,
        EventTicketCatalogManagementDto catalog) =>
        link.RequirePermission(AuthorizationActions.Events.ManagePaidEventCommerce,
            ResourceKinds.Event,
            catalog.EventId.ToString("D"),
            new AuthorizationScope(TenantId: catalog.TenantId.ToString("D")),
            BuildEventFacts(catalog));

    private static IAuthorizationFacts BuildEventFacts(EventTicketCatalogManagementDto catalog) =>
        new EventAuthorizationFacts(
            catalog.TenantId,
            catalog.EventId,
            catalog.ActorId,
            catalog.ActorUserId,
            catalog.ActorOrganizationId,
            catalog.ActorGroupId,
            catalog.OrganizerActorId,
            catalog.OrganizerUserId,
            catalog.OrganizerOrganizationId,
            catalog.OrganizerGroupId,
            ProvenanceType: null,
            SubmittedByUserId: null);
}

public sealed class EventTicketCatalogManagementCollectionLinkPolicy(
    EventTicketCatalogManagementLinkPolicy detailPolicy)
    : ICollectionLinkPolicy<EventTicketCatalogManagementDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(
        EventTicketCatalogManagementDto dto,
        ClaimsPrincipal? user) => detailPolicy.GetLinks(dto, user);

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}
