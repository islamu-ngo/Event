using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventOrganizerClaim;
using Explore.Application.Features.EventOrganizerClaims.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventOrganizerClaims.Handlers.Queries;

public sealed class GetEventOrganizerClaimsRequestHandler(
    IEventOrganizerClaimRepository claimRepository,
    IMapper mapper)
    : IRequestHandler<GetEventOrganizerClaimsRequest, IReadOnlyList<EventOrganizerClaimDto>>
{
    public async Task<IReadOnlyList<EventOrganizerClaimDto>> Handle(
        GetEventOrganizerClaimsRequest request,
        CancellationToken cancellationToken)
    {
        var claims = await claimRepository.ListByEventAsync(request.EventId, cancellationToken);
        return mapper.Map<List<EventOrganizerClaimDto>>(claims);
    }
}
