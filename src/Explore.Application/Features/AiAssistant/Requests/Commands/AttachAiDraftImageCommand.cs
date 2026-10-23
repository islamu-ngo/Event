using System.Text.Json.Serialization;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Responses;
using Explore.Domain.Ai;

namespace Explore.Application.Features.AiAssistant.Requests.Commands;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Update)]
public sealed record AttachAiDraftImageCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    private AttachAiDraftImageCommand(AiToolExecution execution)
    {
        Identity = AiEventDraftRecoveryIdentity.FromExecution(execution);
        ExecutionId = execution.Id;
        ExpectedExecutionStamp = execution.ConcurrencyStamp;
        EventId = execution.PrimaryEventId!.Value;
        StorageObjectId = execution.ImageStorageObjectId!.Value;
    }

    [JsonIgnore]
    internal AiEventDraftRecoveryIdentity Identity { get; }
    public Guid ExecutionId { get; }
    public Guid ExpectedExecutionStamp { get; }
    public Guid EventId { get; }
    public Guid StorageObjectId { get; }

    public static AttachAiDraftImageCommand FromExecution(AiToolExecution execution)
    {
        if (!execution.HasDurablePrimaryReservation || execution.PrimaryEventId != execution.ReservedEventId ||
            execution.PrimaryEventId is not { } primaryId || primaryId == Guid.Empty ||
            execution.ImageUploadSessionId is null || execution.ImageStorageObjectId is not { } objectId ||
            objectId == Guid.Empty)
            throw new InvalidOperationException("Image attachment requires the retained draft and upload custody.");
        return new(execution);
    }

    string? ISecureRequest.ResourceId => EventId.ToString();
}
