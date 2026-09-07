using Explore.Application.Authorization;
using Explore.Application.DTOs.EventSessionAgendaItem;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventSessionAgendaItems.Requests.Commands;

[AuthorizeResource(ResourceKinds.EventSessionAgendaItem, AuthorizationActions.Update)]
public sealed record UpdateEventSessionAgendaItemCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid EventSessionAgendaItemId { get; init; }
    public required UpdateEventSessionAgendaItemDto AgendaItemDto { get; init; }
    public Guid EventSessionId { get; init; }
    public Guid EventId { get; init; }
    public Guid TenantId { get; init; }

    string? ISecureRequest.ResourceId => EventSessionAgendaItemId.ToString();
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new EventScopedAuthorizationFacts(TenantId, EventId, EventSessionId);
}
