using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventOrganizerClaim;
using Explore.Application.Features.EventOrganizerClaims.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventOrganizerClaims.Handlers.Queries;

public sealed class GetEventOrganizerClaimRequestHandler(
    IEventOrganizerClaimRepository claimRepository,
    IMapper mapper)
    : IRequestHandler<GetEventOrganizerClaimRequest, EventOrganizerClaimDto?>
{
    public async Task<EventOrganizerClaimDto?> Handle(
        GetEventOrganizerClaimRequest request,
        CancellationToken cancellationToken)
    {
        var claim = await claimRepository.GetDetailsAsync(
            request.ClaimId,
            trackChanges: false,
            cancellationToken);
        return claim is not null && claim.EventId == request.EventId
            ? mapper.Map<EventOrganizerClaimDto>(claim)
            : null;
    }
}
