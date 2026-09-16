using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventOrganizerClaim;

namespace Explore.Application.Features.EventOrganizerClaims.Requests.Queries;

public sealed record GetClaimantOrganizerClaimsRequest(Guid ClaimantActorId)
    : IQuery<IReadOnlyList<EventOrganizerClaimDto>>;
