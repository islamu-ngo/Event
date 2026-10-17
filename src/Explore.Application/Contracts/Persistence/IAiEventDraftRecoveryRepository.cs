using Explore.Domain;
using Explore.Domain.Ai;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Feature-owned recovery for the implemented CreateEventDraft action only. Acquisition and reads
/// use fresh ordinary tenant transactions; primary admission and linkage use the event creator's
/// native Serializable transaction. An identity is comparison evidence, never authorization.
/// </summary>
public interface IAiEventDraftRecoveryRepository
{
    Task<AiToolExecution?> AcquireAsync(AiEventDraftRecoveryIdentity identity, CancellationToken cancellationToken);
    Task<AiToolExecution?> ReadAsync(AiEventDraftRecoveryIdentity identity, CancellationToken cancellationToken);
    Task<AiToolExecution?> ReadForPrimaryCreationAsync(
        AiEventDraftRecoveryIdentity identity, Guid executionId, Guid reservedEventId, CancellationToken cancellationToken);
    Task LinkPrimaryAsync(
        AiEventDraftRecoveryIdentity identity, Guid executionId, Event primaryEvent, CancellationToken cancellationToken);

    /// <summary>
    /// Reacquires exact current authority and the concurrency fence for later feature-owned
    /// settlement/custody changes. SaveSettlementAsync accepts only this tracked admission.
    /// Neither method owns an outer transaction or performs image/provider I/O.
    /// </summary>
    Task<AiToolExecution?> ReadForSettlementAsync(
        AiEventDraftRecoveryIdentity identity, Guid executionId, Guid expectedConcurrencyStamp,
        CancellationToken cancellationToken);
    Task SaveSettlementAsync(
        AiEventDraftRecoveryIdentity identity, AiToolExecution execution, CancellationToken cancellationToken);
}

public sealed record AiEventDraftRecoveryIdentity(
    Guid TenantId, Guid ConversationId, Guid ProposedActionId, Guid PrincipalUserId, Guid ActingActorId,
    DateTime ActionCreatedAt, DateTime ConfirmedAt, string PayloadDigest, Guid? ActionMessageId)
{
    public static AiEventDraftRecoveryIdentity FromConfirmedAction(AiProposedAction action)
    {
        if (action.Kind != AiProposedActionKind.CreateEventDraft
            || action.Status is not (AiProposedActionStatus.Confirmed or AiProposedActionStatus.Executed)
            || action.ConfirmedBy is not { } userId || action.ActingActorId is not { } actorId
            || action.ConfirmedAt is not { } confirmedAt)
            throw new InvalidOperationException("Exact persisted confirmation evidence is required.");
        return new(action.TenantId, action.ConversationId, action.Id, userId, actorId,
            action.CreatedAt, confirmedAt, AiToolExecution.DigestPayload(action.PayloadJson), action.MessageId);
    }

    public static AiEventDraftRecoveryIdentity FromExecution(AiToolExecution execution) =>
        new(execution.TenantId, execution.ConversationId, execution.ProposedActionId,
            execution.PrincipalUserId, execution.ActingActorId, execution.ActionCreatedAt,
            execution.ActionConfirmedAt, execution.PayloadDigest, execution.ActionMessageId);

    public override string ToString() => "AiEventDraftRecoveryIdentity { Redacted = true }";
}
