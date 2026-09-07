using System.Security.Claims;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.EventAddOns;
using Explore.Application.Hateoas;

namespace Explore.API.Hateoas.Policies;

public sealed class EventAddOnCatalogLinkPolicy :
    ILinkPolicy<EventAddOnCatalogDto>
{
    public IEnumerable<LinkDefinition> GetLinks(
        EventAddOnCatalogDto dto,
        ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            LinkRelations.Self,
            dto.IsManagementView
                ? RouteNames.GetEventAddOnManagement
                : RouteNames.GetEventAddOnCatalog,
            new { eventId = dto.EventId },
            HttpMethods.Get);
        if (dto.CanManage)
        {
            yield return new LinkDefinition(
                LinkRelations.ManageEventAddOns,
                RouteNames.GetEventAddOnManagement,
                new { eventId = dto.EventId },
                HttpMethods.Get,
                "Manage event add-ons",
                RequiresAuth: true);
        }
        if (dto.CanCreateDraft)
        {
            yield return new LinkDefinition(
                LinkRelations.CreateEventAddOnCatalogDraft,
                RouteNames.CreateEventAddOnCatalogDraft,
                new { eventId = dto.EventId },
                HttpMethods.Post,
                "Create add-on catalog draft",
                RequiresAuth: true);
        }
        if (dto.CanAddItem)
        {
            yield return new LinkDefinition(
                LinkRelations.AddEventAddOnCatalogItem,
                RouteNames.AddEventAddOnCatalogItem,
                new { eventId = dto.EventId },
                HttpMethods.Post,
                "Add catalog item",
                RequiresAuth: true);
        }
        if (dto.CanPublish)
        {
            yield return new LinkDefinition(
                LinkRelations.PublishEventAddOnCatalog,
                RouteNames.PublishEventAddOnCatalog,
                new { eventId = dto.EventId },
                HttpMethods.Post,
                "Publish add-on catalog",
                RequiresAuth: true);
        }
        if (dto.CanRetire)
        {
            yield return new LinkDefinition(
                LinkRelations.RetireEventAddOnCatalog,
                RouteNames.RetireEventAddOnCatalog,
                new { eventId = dto.EventId },
                HttpMethods.Post,
                "Retire add-on catalog",
                RequiresAuth: true);
        }
    }
}

public sealed class EventAddOnCatalogCollectionLinkPolicy :
    ICollectionLinkPolicy<EventAddOnCatalogDto>;

public sealed class RegistrationOrderAddOnLinkPolicy :
    ILinkPolicy<RegistrationOrderAddOnSummaryDto>
{
    public IEnumerable<LinkDefinition> GetLinks(
        RegistrationOrderAddOnSummaryDto dto,
        ClaimsPrincipal? user)
    {
        var values = new
        {
            eventId = dto.EventId,
            registrationOrderId = dto.RegistrationOrderId,
        };
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetRegistrationOrderAddOns,
            values,
            HttpMethods.Get);
        if (dto.CanReserve)
        {
            yield return new LinkDefinition(
                LinkRelations.ReserveEventAddOns,
                RouteNames.ReserveRegistrationOrderAddOns,
                values,
                HttpMethods.Post,
                "Reserve selected add-ons",
                RequiresAuth: true);
        }
    }
}

public sealed class RegistrationOrderAddOnCollectionLinkPolicy :
    ICollectionLinkPolicy<RegistrationOrderAddOnSummaryDto>;

public sealed class RegistrationOrderAddOnLineLinkPolicy :
    ILinkPolicy<RegistrationOrderAddOnLineDto>
{
    public IEnumerable<LinkDefinition> GetLinks(
        RegistrationOrderAddOnLineDto dto,
        ClaimsPrincipal? user)
    {
        var values = new
        {
            eventId = dto.EventId,
            registrationOrderId = dto.RegistrationOrderId,
            registrationOrderAddOnLineId = dto.Id,
        };
        if (dto.CanFulfill)
        {
            yield return new LinkDefinition(
                LinkRelations.FulfillEventAddOn,
                RouteNames.FulfillRegistrationOrderAddOn,
                values,
                HttpMethods.Post,
                "Mark add-on fulfilled",
                RequiresAuth: true);
        }
        if (dto.CanRefund)
        {
            yield return new LinkDefinition(
                LinkRelations.RefundEventAddOn,
                RouteNames.RefundRegistrationOrderAddOn,
                values,
                HttpMethods.Post,
                "Refund add-on quantity",
                RequiresAuth: true);
        }
    }
}
