using Explore.Application.Authorization;
using Explore.Application.DTOs.EventSession;

namespace Explore.Application.Features.EventSessions.Requests.Commands;

[AuthorizeResource(ResourceKinds.EventSession, AuthorizationActions.Update)]
public sealed record CompleteEventSessionCommand : IEventSessionLifecycleTransitionCommand
{
    public Guid Id { get; set; }
    public required EventSessionLifecycleRequestDto Request { get; set; }

    string? ISecureRequest.ResourceId => Id.ToString();
}
