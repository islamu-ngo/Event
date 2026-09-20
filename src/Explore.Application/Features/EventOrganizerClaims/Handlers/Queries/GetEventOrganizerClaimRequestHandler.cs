using Explore.Application.Mappings;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventOrganizerClaim;
using Explore.Application.Features.EventOrganizerClaims.Requests.Queries;

namespace Explore.Application.Features.EventOrganizerClaims.Handlers.Queries;

public sealed class GetEventOrganizerClaimRequestHandler(
    IEventOrganizerClaimRepository claimRepository)
    : IQueryHandler<GetEventOrganizerClaimRequest, EventOrganizerClaimDto?>
{
    public async Task<EventOrganizerClaimDto?> QueryAsync(
        GetEventOrganizerClaimRequest request,
        CancellationToken cancellationToken)
    {
        var claim = await claimRepository.GetDetailsAsync(
            request.ClaimId,
            trackChanges: false,
            cancellationToken);
        return claim is not null && claim.EventId == request.EventId
            ? EventMapper.ToDetail(claim)
            : null;
    }
}
