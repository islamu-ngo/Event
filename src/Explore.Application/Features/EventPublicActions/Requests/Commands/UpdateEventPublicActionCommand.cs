using Explore.Application.Authorization;
using Explore.Application.DTOs.EventPublicAction;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventPublicActions.Requests.Commands;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManagePublicActions)]
public sealed record UpdateEventPublicActionCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid EventId { get; init; }
    public Guid ActionId { get; init; }
    public required ManageEventPublicActionDto Action { get; init; }
    string? ISecureRequest.ResourceId => EventId == Guid.Empty ? null : EventId.ToString();
}
