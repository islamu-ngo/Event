
using System.Security.Claims;
using Explore.API.Hateoas.Policies;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.Events.Requests.Queries;
using Explore.Application.Features.RegistrationOrders.Queries;
using Explore.Application.Hateoas;

namespace Explore.API.Hateoas.Assemblers;

public sealed class GuestRegistrationStatusResourceAssembler(
    IHateoasLinkGenerator linkGenerator,
    GuestRegistrationStatusLinkPolicy detailPolicy,
    ICollectionLinkPolicy<GuestRegistrationStatusDto> collectionPolicy,
    IQueryHandler<GetEventCalendarExportRequest, EventCalendarExportDto?> calendarExportHandler,
    IQueryHandler<GetGuestRegistrationCancellationEligibilityQuery, bool?> eligibilityQueryHandler)
    : ResourceAssemblerBase<GuestRegistrationStatusDto>(linkGenerator, detailPolicy, collectionPolicy)
{
    protected override async Task<IReadOnlyList<LinkDefinition>> GetDetailLinkDefinitionsAsync(
        GuestRegistrationStatusDto dto, ClaimsPrincipal? user, HttpContext httpContext)
    {
        var calendar = await calendarExportHandler.QueryAsync(new GetEventCalendarExportRequest(dto.EventId), httpContext.RequestAborted);
        bool? eligible = await eligibilityQueryHandler.QueryAsync(new GetGuestRegistrationCancellationEligibilityQuery(
            dto.EventId, dto.OrderId, httpContext.Request.Headers["X-Registration-Order-Capability"].ToString()),
            httpContext.RequestAborted);
        return detailPolicy.GetLinks(dto with { CanCancelRegistration = eligible is true },
            publicCalendarAvailable: calendar is not null).ToArray();
    }
}
