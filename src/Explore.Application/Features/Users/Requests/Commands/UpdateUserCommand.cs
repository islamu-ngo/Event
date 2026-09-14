using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.User;
using Explore.Application.Responses;

namespace Explore.Application.Features.Users.Requests.Commands;

[AuthorizeResource(ResourceKinds.User, AuthorizationActions.Update)]
public sealed record UpdateUserCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid UserId { get; init; }

    public Guid ExpectedConcurrencyStamp { get; init; }

    public required UpdateUserDto UpdateUserDto { get; init; }

    string? ISecureRequest.ResourceId => UserId.ToString();
}
