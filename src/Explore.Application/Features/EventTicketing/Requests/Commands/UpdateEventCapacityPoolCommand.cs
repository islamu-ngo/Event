using Explore.Application.Authorization;
using Explore.Application.DTOs.EventTicketing;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventTicketing.Requests.Commands;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManageTickets)]
public sealed record UpdateEventCapacityPoolCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid EventId { get; init; }
    public Guid CapacityPoolId { get; init; }
    public required ManageEventCapacityPoolDto CapacityPool { get; init; }
    string? ISecureRequest.ResourceId => EventId == Guid.Empty ? null : EventId.ToString();
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new EventScopedAuthorizationFacts(Guid.Empty, EventId);
}
