using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IEventReportDecisionExecutionRepository : IGenericRepository<EventReportDecisionExecution, Guid>
{
    Task<EventReportDecisionExecution?> GetByDecisionIdAsync(
        Guid tenantId,
        Guid decisionId,
        bool trackChanges,
        CancellationToken cancellationToken);

    Task<EventReportDecisionExecutionClaimOutcome> TryClaimEnforcementAsync(
        Guid tenantId,
        Guid decisionId,
        Guid leaseToken,
        DateTime claimedAtUtc,
        DateTime leaseExpiresAtUtc,
        CancellationToken cancellationToken);

    Task<EventReportDecisionExecutionClaimOutcome> TryClaimCompletionAsync(
        Guid tenantId,
        Guid decisionId,
        Guid leaseToken,
        DateTime claimedAtUtc,
        DateTime leaseExpiresAtUtc,
        CancellationToken cancellationToken);

    Task<EventReportDecisionExecutionTransitionOutcome> TryRecordEnforcementReceiptAsync(
        Guid tenantId,
        Guid decisionId,
        Guid leaseToken,
        Explore.Domain.Enums.EventReportDecisionEnforcementReceiptKind receiptKind,
        Guid? receiptId,
        DateTime completedAtUtc,
        CancellationToken cancellationToken);

    Task<bool> TryReleaseEnforcementClaimAsync(
        Guid tenantId,
        Guid decisionId,
        Guid leaseToken,
        string failureCode,
        DateTime failedAtUtc,
        CancellationToken cancellationToken);

    Task<bool> TryReleaseCompletionClaimAsync(
        Guid tenantId,
        Guid decisionId,
        Guid leaseToken,
        string failureCode,
        DateTime failedAtUtc,
        CancellationToken cancellationToken);
}

public enum EventReportDecisionExecutionClaimOutcome
{
    Claimed = 1,
    SameLease = 2,
    CompletionPending = 3,
    Completed = 4,
    Unavailable = 5
}

public enum EventReportDecisionExecutionTransitionOutcome
{
    Applied = 1,
    AlreadyApplied = 2,
    Conflict = 3
}
