using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventSession;
using Explore.Application.Responses;

namespace Explore.Application.Features.EventSessions.Requests.Commands;

[AuthorizeResource(ResourceKinds.EventSession, AuthorizationActions.Update)]
public sealed record PublishEventSessionCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid Id { get; init; }
    public required PublishEventSessionRequestDto Request { get; init; }

    string? ISecureRequest.ResourceId => Id.ToString();
}
