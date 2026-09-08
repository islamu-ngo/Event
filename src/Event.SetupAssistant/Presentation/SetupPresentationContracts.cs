namespace ISLAMU.Event.SetupAssistant.Presentation;

public enum SetupOperationStatus
{
    Succeeded,
    Cancelled,
    Rejected
}

public enum SetupOperationInvalidationReason
{
    Replaced,
    Cancelled,
    Deactivated,
    Disposed,
    SessionTerminated
}

public enum SetupAccessibilityStatus
{
    NotEvaluated
}

public interface ISetupPresentationOperation
{
    Task<SetupPresentationOutcome> ExecuteAsync(CancellationToken cancellationToken);
}

public sealed class SetupPresentationOutcome
{
    public SetupPresentationOutcome(object coreResult, ReadOnlyMemory<byte> output)
    {
        ArgumentNullException.ThrowIfNull(coreResult);
        CoreResult = coreResult;
        Output = output;
    }

    public object CoreResult { get; }

    public ReadOnlyMemory<byte> Output { get; }

    public override string ToString() => nameof(SetupPresentationOutcome);
}

public sealed class SetupOperationSettledMessage
{
    public SetupOperationSettledMessage(
        Guid sessionId,
        SetupWorkspaceId workspaceId,
        Guid operationId,
        SetupOperationGeneration generation,
        SetupOperationStatus status)
    {
        SessionId = sessionId;
        WorkspaceId = workspaceId;
        OperationId = operationId;
        Generation = generation;
        Status = status;
    }

    public SetupOperationGeneration Generation { get; }

    public Guid OperationId { get; }

    public Guid SessionId { get; }

    public SetupOperationStatus Status { get; }

    public SetupWorkspaceId WorkspaceId { get; }

    public override string ToString() => nameof(SetupOperationSettledMessage);
}

public sealed class SetupOperationInvalidatedEventArgs : EventArgs
{
    public SetupOperationInvalidatedEventArgs(
        SetupOperationGeneration generation,
        SetupOperationInvalidationReason reason)
    {
        Generation = generation;
        Reason = reason;
    }

    public SetupOperationGeneration Generation { get; }

    public SetupOperationInvalidationReason Reason { get; }

    public override string ToString() => nameof(SetupOperationInvalidatedEventArgs);
}

public sealed class SetupCompletionDiscardedEventArgs : EventArgs
{
    public SetupCompletionDiscardedEventArgs(
        Guid operationId,
        SetupOperationGeneration generation)
    {
        OperationId = operationId;
        Generation = generation;
    }

    public SetupOperationGeneration Generation { get; }

    public Guid OperationId { get; }

    public override string ToString() => nameof(SetupCompletionDiscardedEventArgs);
}
