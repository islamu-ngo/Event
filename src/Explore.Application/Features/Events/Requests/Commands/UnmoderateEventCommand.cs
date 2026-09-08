using Explore.Application.Authorization;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Events.Requests.Commands;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.Unmoderate)]
public sealed record UnmoderateEventCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public const string DefaultReasonCode = "unmoderation";

    public Guid Id { get; init; }
    public string ReasonCode { get; init; } = DefaultReasonCode;
    public string? CorrelationId { get; init; }

    string? ISecureRequest.ResourceId => Id.ToString();
}
