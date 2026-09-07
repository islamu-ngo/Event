using Explore.Application.Authorization;
using Explore.Application.DTOs.LocationRoom;
using MediatR;

namespace Explore.Application.Features.LocationRooms.Requests.Queries;

[AuthorizeResource(ResourceKinds.LocationRoom, AuthorizationActions.LocationRooms.View)]
public sealed record GetLocationRoomDetailRequest : IRequest<LocationRoomDto?>, ISecureRequest
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }

    string? ISecureRequest.ResourceId => Id == Guid.Empty ? null : Id.ToString("D");

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        TenantId == Guid.Empty
        ? null
        : new TenantScopedAuthorizationFacts(TenantId);
}
