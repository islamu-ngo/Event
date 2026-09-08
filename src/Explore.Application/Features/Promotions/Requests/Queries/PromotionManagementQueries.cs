using Explore.Application.Authorization;
using Explore.Application.Features.Promotions;
using MediatR;

namespace Explore.Application.Features.Promotions.Requests.Queries;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManagePaidEventCommerce)]
public sealed record ListPromotionManagementQuery(Guid EventId, Guid TicketCatalogVersionId)
    : IRequest<IReadOnlyList<PromotionManagementDto>>, ISecureRequest
{
    public string? ResourceId => EventId.ToString();

    public IAuthorizationFacts? AuthorizationFacts => new EventScopedAuthorizationFacts(Guid.Empty, EventId);
}

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManagePaidEventCommerce)]
public sealed record GetPromotionManagementQuery(Guid EventId, Guid PromotionDefinitionId)
    : IRequest<PromotionManagementDto?>, ISecureRequest
{
    public string? ResourceId => EventId.ToString();

    public IAuthorizationFacts? AuthorizationFacts => new EventScopedAuthorizationFacts(Guid.Empty, EventId);
}
