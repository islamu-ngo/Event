using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;

namespace Explore.Application.Features.Events.Requests.Commands;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ModerateHeavy)]
public sealed record HeavyRedactEventCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public const string DefaultReasonCode = "heavy_redaction";
    public const string StorageDeletionPendingFailureCode = "event_heavy_redaction_storage_deletion_pending";
    public const string UserResolutionFailureCode = "event_heavy_redaction_user_unresolved";

    public Guid Id { get; init; }
    public string ReasonCode { get; init; } = DefaultReasonCode;
    public string? CorrelationId { get; init; }
    public Guid? SourceReportId { get; init; }
    public Guid? SourceReportDecisionId { get; init; }

    string? ISecureRequest.ResourceId => Id.ToString();
}
