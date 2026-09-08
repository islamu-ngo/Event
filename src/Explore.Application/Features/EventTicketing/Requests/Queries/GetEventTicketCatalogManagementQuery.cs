using Explore.Application.Authorization;
using Explore.Application.DTOs.EventTicketing;
using MediatR;

namespace Explore.Application.Features.EventTicketing.Requests.Queries;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManageTickets)]
public sealed record GetEventTicketCatalogManagementQuery(Guid EventId) : IRequest<EventTicketCatalogManagementDto?>, ISecureRequest
{
    string? ISecureRequest.ResourceId => EventId == Guid.Empty ? null : EventId.ToString();
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new EventScopedAuthorizationFacts(Guid.Empty, EventId);
}
