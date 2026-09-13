using Explore.Application.Authorization;
using Explore.Application.DTOs.LocationRoom;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.LocationRooms.Requests.Commands;

[AuthorizeResource(ResourceKinds.LocationRoom, AuthorizationActions.Create)]
public sealed record CreateLocationRoomCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public required CreateLocationRoomDto LocationRoomDto { get; init; }

    string? ISecureRequest.ResourceId => null;
}
