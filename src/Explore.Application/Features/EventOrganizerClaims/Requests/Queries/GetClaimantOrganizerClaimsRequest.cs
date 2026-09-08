using Explore.Application.DTOs.EventOrganizerClaim;
using MediatR;

namespace Explore.Application.Features.EventOrganizerClaims.Requests.Queries;

public sealed record GetClaimantOrganizerClaimsRequest(Guid ClaimantActorId)
    : IRequest<IReadOnlyList<EventOrganizerClaimDto>>;
