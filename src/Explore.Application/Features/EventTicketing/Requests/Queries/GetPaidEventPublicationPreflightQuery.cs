using Explore.Application.Authorization;
using Explore.Application.DTOs.EventTicketing;
using MediatR;

namespace Explore.Application.Features.EventTicketing.Requests.Queries;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManageTickets)]
public sealed record GetPaidEventPublicationPreflightQuery(Guid EventId)
    : IRequest<PaidEventPublicationPreflightDto>, ISecureRequest
{
    string? ISecureRequest.ResourceId => EventId == Guid.Empty ? null : EventId.ToString();
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new EventScopedAuthorizationFacts(Guid.Empty, EventId);
}
