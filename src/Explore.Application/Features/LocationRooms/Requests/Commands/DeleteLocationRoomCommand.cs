using Explore.Application.Authorization;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.LocationRooms.Requests.Commands;

[AuthorizeResource(ResourceKinds.LocationRoom, AuthorizationActions.Delete)]
public sealed record DeleteLocationRoomCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid Id { get; init; }

    string? ISecureRequest.ResourceId => Id.ToString();
}
