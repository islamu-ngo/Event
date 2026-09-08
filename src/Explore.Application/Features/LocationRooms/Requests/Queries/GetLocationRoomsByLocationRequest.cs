using Explore.Application.Authorization;
using Explore.Application.DTOs.LocationRoom;
using MediatR;

namespace Explore.Application.Features.LocationRooms.Requests.Queries;

[AuthorizeResource(ResourceKinds.LocationRoom, AuthorizationActions.LocationRooms.View)]
public sealed record GetLocationRoomsByLocationRequest : IRequest<List<LocationRoomListDto>>, ISecureRequest
{
    public Guid LocationId { get; init; }
    public Guid TenantId { get; init; }

    string? ISecureRequest.ResourceId => LocationId == Guid.Empty ? null : LocationId.ToString("D");

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        TenantId == Guid.Empty
        ? null
        : new TenantScopedAuthorizationFacts(TenantId);
}
