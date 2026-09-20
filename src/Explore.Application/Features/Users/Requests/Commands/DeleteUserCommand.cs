using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.PrivacyErasure;

namespace Explore.Application.Features.Users.Requests.Commands;

[AuthorizeResource(ResourceKinds.User, AuthorizationActions.Delete)]
public sealed record DeleteUserCommand : ICommand<PrivacyErasureStartDto>, ISecureRequest
{
    public Guid UserId { get; init; }
    public Guid IntentId { get; init; }

    string? ISecureRequest.ResourceId => UserId.ToString();
}
