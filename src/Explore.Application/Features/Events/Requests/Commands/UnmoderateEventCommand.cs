using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;

namespace Explore.Application.Features.Events.Requests.Commands;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.Unmoderate)]
public sealed record UnmoderateEventCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public const string DefaultReasonCode = "unmoderation";

    public Guid Id { get; init; }
    public string ReasonCode { get; init; } = DefaultReasonCode;
    public string? CorrelationId { get; init; }

    string? ISecureRequest.ResourceId => Id.ToString();
}
