using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.RegistrationOrders;

namespace Explore.Application.Features.RegistrationOrders.Requests.Queries;

public sealed record GetRegistrationOrderQuery(Guid OrderId) : IQuery<RegistrationOrderDto?>;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManageRegistrations)]
public sealed record GetEventRegistrationOrdersQuery(Guid EventId) : IQuery<IReadOnlyList<RegistrationOrderDto>>, ISecureRequest
{
    string? ISecureRequest.ResourceId => EventId == Guid.Empty ? null : EventId.ToString();

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new EventScopedAuthorizationFacts(Guid.Empty, EventId);
}
