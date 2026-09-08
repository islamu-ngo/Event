using Explore.Application.Authorization;
using Explore.Application.DTOs.OrganizerPaymentConnections;
using MediatR;

namespace Explore.Application.Features.OrganizerPaymentConnections;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManagePaidEventCommerce)]
public sealed record GetEventOrganizerPaymentConnectionQuery(Guid EventId)
    : IRequest<EventOrganizerPaymentConnectionManagementDto?>, ISecureRequest
{
    string? ISecureRequest.ResourceId => EventId == Guid.Empty ? null : EventId.ToString();
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new EventScopedAuthorizationFacts(Guid.Empty, EventId);
}
