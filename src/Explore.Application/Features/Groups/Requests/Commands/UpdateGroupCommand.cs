using Explore.Application.Authorization;
using Explore.Application.DTOs.Group;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Groups.Requests.Commands;

[AuthorizeResource(ResourceKinds.Group, AuthorizationActions.Update)]
public sealed record UpdateGroupCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid GroupId { get; init; }

    public required string UserId { get; init; }

    public Guid ExpectedConcurrencyStamp { get; init; }

    public required UpdateGroupDto UpdateGroupDto { get; init; }

    string? ISecureRequest.ResourceId => GroupId.ToString();
}
