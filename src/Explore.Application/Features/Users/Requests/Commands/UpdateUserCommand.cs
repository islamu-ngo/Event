using Explore.Application.Authorization;
using Explore.Application.DTOs.User;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Users.Requests.Commands;

[AuthorizeResource(ResourceKinds.User, AuthorizationActions.Update)]
public sealed record UpdateUserCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid UserId { get; init; }

    public Guid ExpectedConcurrencyStamp { get; init; }

    public required UpdateUserDto UpdateUserDto { get; init; }

    string? ISecureRequest.ResourceId => UserId.ToString();
}
