using Explore.Application.Mappings;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventOrganizerClaim;
using Explore.Application.Features.EventOrganizerClaims.Requests.Queries;

namespace Explore.Application.Features.EventOrganizerClaims.Handlers.Queries;

public sealed class GetEventOrganizerClaimsRequestHandler(
    IEventOrganizerClaimRepository claimRepository)
    : IQueryHandler<GetEventOrganizerClaimsRequest, IReadOnlyList<EventOrganizerClaimDto>>
{
    public async Task<IReadOnlyList<EventOrganizerClaimDto>> QueryAsync(
        GetEventOrganizerClaimsRequest request,
        CancellationToken cancellationToken)
    {
        var claims = await claimRepository.ListByEventAsync(request.EventId, cancellationToken);
        return claims.Select(EventMapper.ToDetail).ToList();
    }
}
