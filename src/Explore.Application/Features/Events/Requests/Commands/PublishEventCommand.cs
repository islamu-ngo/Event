using Explore.Application.Authorization;
using Explore.Application.DTOs.Event;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Events.Requests.Commands;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.Publish)]
public sealed record PublishEventCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid Id { get; init; }

    public required PublishEventRequestDto Request { get; init; }

    string? ISecureRequest.ResourceId => Id.ToString();

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new EventScopedAuthorizationFacts(Guid.Empty, Id);
}
