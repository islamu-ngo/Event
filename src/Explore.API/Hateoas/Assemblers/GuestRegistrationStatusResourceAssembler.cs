
using System.Security.Claims;
using Explore.API.Hateoas.Policies;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Events.Handlers.Queries;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.Events.Requests.Queries;
using Explore.Application.Features.RegistrationOrders.Queries;
using Explore.Application.Hateoas;

namespace Explore.API.Hateoas.Assemblers;

public sealed class GuestRegistrationStatusResourceAssembler(
    IHateoasLinkGenerator linkGenerator,
    GuestRegistrationStatusLinkPolicy detailPolicy,
    ICollectionLinkPolicy<GuestRegistrationStatusDto> collectionPolicy,
    IQueryHandler<GetEventCalendarExportRequest, PublicEventCalendarProjection?> calendarExportHandler,
    IQueryHandler<GetGuestRegistrationCancellationEligibilityQuery, bool?> eligibilityQueryHandler)
    : ResourceAssemblerBase<GuestRegistrationStatusDto>(linkGenerator, detailPolicy, collectionPolicy)
{
    protected override async Task<IReadOnlyList<LinkDefinition>> GetDetailLinkDefinitionsAsync(
        GuestRegistrationStatusDto dto, ClaimsPrincipal? user, HttpContext httpContext)
    {
        await using var calendar = await calendarExportHandler.QueryAsync(
            new GetEventCalendarExportRequest(dto.EventId), httpContext.RequestAborted);
        var publicCalendarAvailable = calendar is not null
            && await calendar.DiscloseAsync((_, _) => Task.FromResult(true), (_, _) => Task.CompletedTask,
                httpContext.RequestAborted) == ModerationPublicEventCalendarOutcome.Allowed;
        bool? eligible = await eligibilityQueryHandler.QueryAsync(new GetGuestRegistrationCancellationEligibilityQuery(
            dto.EventId, dto.OrderId, httpContext.Request.Headers["X-Registration-Order-Capability"].ToString()),
            httpContext.RequestAborted);
        return detailPolicy.GetLinks(dto with { CanCancelRegistration = eligible is true },
            publicCalendarAvailable).ToArray();
    }
}
