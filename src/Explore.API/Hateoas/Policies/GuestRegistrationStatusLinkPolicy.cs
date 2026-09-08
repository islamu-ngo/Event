
using System.Security.Claims;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Hateoas;

namespace Explore.API.Hateoas.Policies;

public sealed class GuestRegistrationStatusLinkPolicy : ILinkPolicy<GuestRegistrationStatusDto>
{
    public IEnumerable<LinkDefinition> GetLinks(GuestRegistrationStatusDto dto, ClaimsPrincipal? user) =>
        GetLinks(dto, publicCalendarAvailable: false);

    public IEnumerable<LinkDefinition> GetLinks(GuestRegistrationStatusDto dto, bool publicCalendarAvailable)
    {
        yield return LinkDefinition.Self(RouteNames.GetGuestRegistrationStatus,
            new { eventId = dto.EventId, orderId = dto.OrderId });
        if (dto.CanCancelRegistration)
        {
            yield return new LinkDefinition(LinkRelations.CancelRegistration, RouteNames.CancelConfirmedGuestRegistration,
                new { eventId = dto.EventId, orderId = dto.OrderId }, HttpMethods.Post);
        }
        if (publicCalendarAvailable)
        {
            yield return new LinkDefinition(LinkRelations.Calendar, RouteNames.GetEventCalendar,
                new { id = dto.EventId });
        }
    }
}

public sealed class GuestRegistrationStatusCollectionLinkPolicy : ICollectionLinkPolicy<GuestRegistrationStatusDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(GuestRegistrationStatusDto dto, ClaimsPrincipal? user)
    {
        yield break;
    }

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user)
    {
        yield break;
    }
}
