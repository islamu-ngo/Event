using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventTicketing;
using Explore.Application.Responses;

namespace Explore.Application.Features.EventTicketing.Requests.Commands;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManageTickets)]
public sealed record UpdateEventTicketTypeCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid EventId { get; init; }
    public Guid TicketTypeId { get; init; }
    public required ManageEventTicketTypeDto TicketType { get; init; }
    string? ISecureRequest.ResourceId => EventId == Guid.Empty ? null : EventId.ToString();
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new EventScopedAuthorizationFacts(Guid.Empty, EventId);
}
