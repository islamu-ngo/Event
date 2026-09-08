using Explore.Application.Authorization;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventOrganizerClaims.Requests.Commands;

[AuthorizeResource(ResourceKinds.EventOrganizerClaim, AuthorizationActions.Events.WithdrawOrganizerClaim)]
public sealed record WithdrawEventOrganizerClaimCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid EventId { get; init; }
    public Guid ClaimId { get; init; }
    public Guid ExpectedConcurrencyStamp { get; init; }
    string? ISecureRequest.ResourceId => ClaimId == Guid.Empty ? null : ClaimId.ToString();
}
