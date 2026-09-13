using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;

namespace Explore.Application.Features.EventSessionCustomPropertyProjections.Requests.Commands;

[AuthorizeResource(ResourceKinds.CustomPropertyProjection, AuthorizationActions.Update)]
public sealed record RebuildSingleEventSessionCustomPropertyProjectionCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid EventSessionId { get; init; }

    string? ISecureRequest.ResourceId => EventSessionId == Guid.Empty ? null : EventSessionId.ToString("D");

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        EventSessionId == Guid.Empty
        ? null
        : new CustomPropertyProjectionAuthorizationFacts(Guid.Empty, null, EventSessionId);
}
