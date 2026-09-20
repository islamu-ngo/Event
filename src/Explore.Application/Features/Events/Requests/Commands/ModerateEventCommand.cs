using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;

namespace Explore.Application.Features.Events.Requests.Commands;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ModerateLight)]
public sealed record ModerateEventCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public const string DefaultReasonCode = "light_moderation";

    public Guid Id { get; init; }
    public string ReasonCode { get; init; } = DefaultReasonCode;
    public string? CorrelationId { get; init; }
    public Guid? SourceReportId { get; init; }
    public Guid? SourceReportDecisionId { get; init; }

    string? ISecureRequest.ResourceId => Id.ToString();
}
