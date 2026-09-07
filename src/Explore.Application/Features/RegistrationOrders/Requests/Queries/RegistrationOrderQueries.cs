using Explore.Application.Authorization;
using Explore.Application.DTOs.RegistrationOrders;
using MediatR;

namespace Explore.Application.Features.RegistrationOrders.Requests.Queries;

public sealed record GetRegistrationOrderQuery(Guid OrderId) : IRequest<RegistrationOrderDto?>;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManageRegistrations)]
public sealed record GetEventRegistrationOrdersQuery(Guid EventId) : IRequest<IReadOnlyList<RegistrationOrderDto>>, ISecureRequest
{
    string? ISecureRequest.ResourceId => EventId == Guid.Empty ? null : EventId.ToString();

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new EventScopedAuthorizationFacts(Guid.Empty, EventId);
}
