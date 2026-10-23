using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.User;
using Explore.Application.Responses;

namespace Explore.Application.Features.Users.Requests.Commands;

[AuthorizeResource(ResourceKinds.User, AuthorizationActions.Update)]
public sealed record UpdateUserProfileImageCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public required Guid UserId { get; init; }
    public required Guid ExpectedConcurrencyStamp { get; init; }
    public required UpdateUserProfileImageDto ProfileImage { get; init; }
    string? ISecureRequest.ResourceId => UserId.ToString();
    public override string ToString() => nameof(UpdateUserProfileImageCommand);
}
