using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.OrganizerPaymentConnections;

namespace Explore.Application.Features.OrganizerPaymentConnections;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManagePaidEventCommerce)]
public sealed record GetEventOrganizerPaymentConnectionQuery(Guid EventId)
    : IQuery<EventOrganizerPaymentConnectionManagementDto?>, ISecureRequest
{
    string? ISecureRequest.ResourceId => EventId == Guid.Empty ? null : EventId.ToString();
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new EventScopedAuthorizationFacts(Guid.Empty, EventId);
}
