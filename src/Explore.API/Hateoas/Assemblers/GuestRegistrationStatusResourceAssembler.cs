// ABOUTME: Adds public-calendar discovery to the isolated guest status HAL resource.
// ABOUTME: Reuses the public query without capability propagation and leaves private status valid when no export exists.

using System.Security.Claims;
using Explore.API.Hateoas.Policies;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.Events.Requests.Queries;
using Explore.Application.Hateoas;
using MediatR;

namespace Explore.API.Hateoas.Assemblers;

public sealed class GuestRegistrationStatusResourceAssembler(
    IHateoasLinkGenerator linkGenerator,
    GuestRegistrationStatusLinkPolicy detailPolicy,
    ICollectionLinkPolicy<GuestRegistrationStatusDto> collectionPolicy,
    ISender sender)
    : ResourceAssemblerBase<GuestRegistrationStatusDto>(linkGenerator, detailPolicy, collectionPolicy)
{
    protected override async Task<IReadOnlyList<LinkDefinition>> GetDetailLinkDefinitionsAsync(
        GuestRegistrationStatusDto dto, ClaimsPrincipal? user, HttpContext httpContext)
    {
        var calendar = await sender.Send(new GetEventCalendarExportRequest(dto.EventId), httpContext.RequestAborted);
        return detailPolicy.GetLinks(dto, publicCalendarAvailable: calendar is not null).ToArray();
    }
}
