using Explore.Application.Authorization;
using Explore.Application.DTOs.PrivacyErasure;
using MediatR;

namespace Explore.Application.Features.Users.Requests.Commands;

[AuthorizeResource(ResourceKinds.User, AuthorizationActions.Delete)]
public sealed record DeleteUserCommand : IRequest<PrivacyErasureStartDto>, ISecureRequest
{
    public Guid UserId { get; init; }
    public Guid IntentId { get; init; }

    string? ISecureRequest.ResourceId => UserId.ToString();
}
