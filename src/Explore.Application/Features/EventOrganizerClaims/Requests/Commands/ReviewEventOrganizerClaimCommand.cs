using Explore.Application.Authorization;
using Explore.Application.DTOs.EventOrganizerClaim;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventOrganizerClaims.Requests.Commands;

[AuthorizeResource(ResourceKinds.EventOrganizerClaim, AuthorizationActions.Events.ReviewOrganizerClaim)]
public sealed record ReviewEventOrganizerClaimCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid EventId { get; init; }
    public Guid ClaimId { get; init; }
    public required ReviewEventOrganizerClaimDto Review { get; init; }
    string? ISecureRequest.ResourceId => EventId == Guid.Empty ? null : EventId.ToString();
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new EventScopedAuthorizationFacts(Guid.Empty, EventId);
}
