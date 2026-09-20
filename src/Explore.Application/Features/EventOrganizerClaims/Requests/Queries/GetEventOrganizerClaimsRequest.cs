using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventOrganizerClaim;

namespace Explore.Application.Features.EventOrganizerClaims.Requests.Queries;

[AuthorizeResource(ResourceKinds.EventOrganizerClaim, AuthorizationActions.Events.ViewOrganizerClaims)]
public sealed record GetEventOrganizerClaimsRequest(Guid EventId)
    : IQuery<IReadOnlyList<EventOrganizerClaimDto>>, ISecureRequest
{
    string? ISecureRequest.ResourceId => EventId == Guid.Empty ? null : EventId.ToString();
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new EventScopedAuthorizationFacts(Guid.Empty, EventId);
}
