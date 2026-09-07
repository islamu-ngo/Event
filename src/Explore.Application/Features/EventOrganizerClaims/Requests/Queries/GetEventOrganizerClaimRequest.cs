using Explore.Application.Authorization;
using Explore.Application.DTOs.EventOrganizerClaim;
using MediatR;

namespace Explore.Application.Features.EventOrganizerClaims.Requests.Queries;

[AuthorizeResource(ResourceKinds.EventOrganizerClaim, AuthorizationActions.Events.ViewOrganizerClaims)]
public sealed record GetEventOrganizerClaimRequest(Guid EventId, Guid ClaimId)
    : IRequest<EventOrganizerClaimDto?>, ISecureRequest
{
    string? ISecureRequest.ResourceId => EventId == Guid.Empty ? null : EventId.ToString();
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new EventScopedAuthorizationFacts(Guid.Empty, EventId);
}
