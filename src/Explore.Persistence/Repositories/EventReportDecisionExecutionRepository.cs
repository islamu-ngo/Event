using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class EventReportDecisionExecutionRepository(
    ExploreDbContext dbContext)
    : GenericRepository<EventReportDecisionExecution, Guid>(dbContext), IEventReportDecisionExecutionRepository
{
    public Task<EventReportDecisionExecution?> GetByDecisionIdAsync(
        Guid tenantId,
        Guid decisionId,
        bool trackChanges,
        CancellationToken cancellationToken)
    {
        IQueryable<EventReportDecisionExecution> query = dbContext.EventReportDecisionExecutions
            .Where(execution => execution.TenantId == tenantId && execution.DecisionId == decisionId);
        if (!trackChanges)
        {
            query = query.AsNoTracking();
        }

        return query.SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<EventReportDecisionExecutionClaimOutcome> TryClaimEnforcementAsync(
        Guid tenantId,
        Guid decisionId,
        Guid leaseToken,
        DateTime claimedAtUtc,
        DateTime leaseExpiresAtUtc,
        CancellationToken cancellationToken)
    {
        ValidateClaim(tenantId, decisionId, leaseToken, claimedAtUtc, leaseExpiresAtUtc);
        claimedAtUtc = NormalizePostgresTimestamp(claimedAtUtc);
        leaseExpiresAtUtc = NormalizePostgresTimestamp(leaseExpiresAtUtc);

        int updated = await dbContext.EventReportDecisionExecutions
            .Where(execution =>
                execution.TenantId == tenantId
                && execution.DecisionId == decisionId
                && (execution.State == EventReportDecisionExecutionState.Requested
                    || (execution.State == EventReportDecisionExecutionState.InProgress
                        && execution.ProcessingLeaseExpiresAtUtc <= claimedAtUtc)))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(execution => execution.State, EventReportDecisionExecutionState.InProgress)
                .SetProperty(execution => execution.ProcessingLeaseToken, leaseToken)
                .SetProperty(execution => execution.ProcessingLeaseExpiresAtUtc, leaseExpiresAtUtc)
                .SetProperty(execution => execution.AttemptCount, execution => execution.AttemptCount + 1)
                .SetProperty(execution => execution.LastFailureCode, (string?)null)
                .SetProperty(execution => execution.LastFailureAtUtc, (DateTime?)null)
                .SetProperty(execution => execution.UpdatedAt, claimedAtUtc)
                .SetProperty(execution => execution.Version, execution => execution.Version + 1),
                cancellationToken);
        if (updated == 1)
        {
            return EventReportDecisionExecutionClaimOutcome.Claimed;
        }

        EventReportDecisionExecution? current = await GetByDecisionIdAsync(
            tenantId,
            decisionId,
            trackChanges: false,
            cancellationToken);
        if (current?.State == EventReportDecisionExecutionState.Completed)
        {
            return EventReportDecisionExecutionClaimOutcome.Completed;
        }

        if (current?.State == EventReportDecisionExecutionState.CompletionPending)
        {
            return EventReportDecisionExecutionClaimOutcome.CompletionPending;
        }

        DateTime reconciliationAtUtc = NormalizePostgresTimestamp(DateTime.UtcNow);
        return current?.State == EventReportDecisionExecutionState.InProgress
            && current.ProcessingLeaseToken == leaseToken
            && current.ProcessingLeaseExpiresAtUtc == leaseExpiresAtUtc
            && current.ProcessingLeaseExpiresAtUtc > reconciliationAtUtc
                ? EventReportDecisionExecutionClaimOutcome.SameLease
                : EventReportDecisionExecutionClaimOutcome.Unavailable;
    }

    public async Task<EventReportDecisionExecutionClaimOutcome> TryClaimCompletionAsync(
        Guid tenantId,
        Guid decisionId,
        Guid leaseToken,
        DateTime claimedAtUtc,
        DateTime leaseExpiresAtUtc,
        CancellationToken cancellationToken)
    {
        ValidateClaim(tenantId, decisionId, leaseToken, claimedAtUtc, leaseExpiresAtUtc);
        claimedAtUtc = NormalizePostgresTimestamp(claimedAtUtc);
        leaseExpiresAtUtc = NormalizePostgresTimestamp(leaseExpiresAtUtc);

        int updated = await dbContext.EventReportDecisionExecutions
            .Where(execution =>
                execution.TenantId == tenantId
                && execution.DecisionId == decisionId
                && execution.State == EventReportDecisionExecutionState.CompletionPending
                && (execution.ProcessingLeaseToken == null
                    || execution.ProcessingLeaseExpiresAtUtc <= claimedAtUtc))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(execution => execution.ProcessingLeaseToken, leaseToken)
                .SetProperty(execution => execution.ProcessingLeaseExpiresAtUtc, leaseExpiresAtUtc)
                .SetProperty(execution => execution.AttemptCount, execution => execution.AttemptCount + 1)
                .SetProperty(execution => execution.LastFailureCode, (string?)null)
                .SetProperty(execution => execution.LastFailureAtUtc, (DateTime?)null)
                .SetProperty(execution => execution.UpdatedAt, claimedAtUtc)
                .SetProperty(execution => execution.Version, execution => execution.Version + 1),
                cancellationToken);
        if (updated == 1)
        {
            return EventReportDecisionExecutionClaimOutcome.Claimed;
        }

        EventReportDecisionExecution? current = await GetByDecisionIdAsync(
            tenantId,
            decisionId,
            trackChanges: false,
            cancellationToken);
        if (current?.State == EventReportDecisionExecutionState.Completed)
        {
            return EventReportDecisionExecutionClaimOutcome.Completed;
        }

        DateTime reconciliationAtUtc = NormalizePostgresTimestamp(DateTime.UtcNow);
        return current?.State == EventReportDecisionExecutionState.CompletionPending
            && current.ProcessingLeaseToken == leaseToken
            && current.ProcessingLeaseExpiresAtUtc == leaseExpiresAtUtc
            && current.ProcessingLeaseExpiresAtUtc > reconciliationAtUtc
                ? EventReportDecisionExecutionClaimOutcome.SameLease
                : EventReportDecisionExecutionClaimOutcome.Unavailable;
    }

    public async Task<EventReportDecisionExecutionTransitionOutcome> TryRecordEnforcementReceiptAsync(
        Guid tenantId,
        Guid decisionId,
        Guid leaseToken,
        EventReportDecisionEnforcementReceiptKind receiptKind,
        Guid? receiptId,
        DateTime completedAtUtc,
        CancellationToken cancellationToken)
    {
        ValidateReceipt(leaseToken, receiptKind, receiptId, completedAtUtc);
        completedAtUtc = NormalizePostgresTimestamp(completedAtUtc);
        int updated = await dbContext.EventReportDecisionExecutions
            .Where(execution =>
                execution.TenantId == tenantId
                && execution.DecisionId == decisionId
                && execution.State == EventReportDecisionExecutionState.InProgress
                && execution.ProcessingLeaseToken == leaseToken
                && execution.ProcessingLeaseExpiresAtUtc > completedAtUtc)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(execution => execution.State, EventReportDecisionExecutionState.CompletionPending)
                .SetProperty(execution => execution.EnforcementReceiptKind, receiptKind)
                .SetProperty(execution => execution.EnforcementReceiptId, receiptId)
                .SetProperty(execution => execution.ModerationRecordId, receiptId)
                .SetProperty(execution => execution.EnforcementCompletedAtUtc, completedAtUtc)
                .SetProperty(execution => execution.ProcessingLeaseToken, (Guid?)null)
                .SetProperty(execution => execution.ProcessingLeaseExpiresAtUtc, (DateTime?)null)
                .SetProperty(execution => execution.LastFailureCode, (string?)null)
                .SetProperty(execution => execution.LastFailureAtUtc, (DateTime?)null)
                .SetProperty(execution => execution.UpdatedAt, completedAtUtc)
                .SetProperty(execution => execution.Version, execution => execution.Version + 1),
                cancellationToken);
        if (updated == 1)
        {
            return EventReportDecisionExecutionTransitionOutcome.Applied;
        }

        EventReportDecisionExecution? current = await GetByDecisionIdAsync(
            tenantId,
            decisionId,
            trackChanges: false,
            cancellationToken);
        bool requiresModerationRecord = receiptKind is
            EventReportDecisionEnforcementReceiptKind.LightModeration or
            EventReportDecisionEnforcementReceiptKind.HeavyRedaction;
        bool alreadyApplied = current is not null
            && current.State is EventReportDecisionExecutionState.CompletionPending
                or EventReportDecisionExecutionState.Completed
            && current.EnforcementReceiptKind == receiptKind
            && current.EnforcementReceiptId == receiptId
            && current.ModerationRecordId == (requiresModerationRecord ? receiptId : null)
            && current.EnforcementCompletedAtUtc is not null;
        return alreadyApplied
            ? EventReportDecisionExecutionTransitionOutcome.AlreadyApplied
            : EventReportDecisionExecutionTransitionOutcome.Conflict;
    }

    public async Task<bool> TryReleaseEnforcementClaimAsync(
        Guid tenantId,
        Guid decisionId,
        Guid leaseToken,
        string failureCode,
        DateTime failedAtUtc,
        CancellationToken cancellationToken)
    {
        string normalizedFailureCode = NormalizeFailureCode(failureCode);
        int updated = await dbContext.EventReportDecisionExecutions
            .Where(execution =>
                execution.TenantId == tenantId
                && execution.DecisionId == decisionId
                && execution.State == EventReportDecisionExecutionState.InProgress
                && execution.ProcessingLeaseToken == leaseToken)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(execution => execution.State, EventReportDecisionExecutionState.Requested)
                .SetProperty(execution => execution.EnforcementReceiptKind, EventReportDecisionEnforcementReceiptKind.None)
                .SetProperty(execution => execution.EnforcementReceiptId, (Guid?)null)
                .SetProperty(execution => execution.ModerationRecordId, (Guid?)null)
                .SetProperty(execution => execution.EnforcementCompletedAtUtc, (DateTime?)null)
                .SetProperty(execution => execution.ProcessingLeaseToken, (Guid?)null)
                .SetProperty(execution => execution.ProcessingLeaseExpiresAtUtc, (DateTime?)null)
                .SetProperty(execution => execution.LastFailureCode, normalizedFailureCode)
                .SetProperty(execution => execution.LastFailureAtUtc, failedAtUtc)
                .SetProperty(execution => execution.UpdatedAt, failedAtUtc)
                .SetProperty(execution => execution.Version, execution => execution.Version + 1),
                cancellationToken);
        return updated == 1;
    }

    public async Task<bool> TryReleaseCompletionClaimAsync(
        Guid tenantId,
        Guid decisionId,
        Guid leaseToken,
        string failureCode,
        DateTime failedAtUtc,
        CancellationToken cancellationToken)
    {
        string normalizedFailureCode = NormalizeFailureCode(failureCode);
        int updated = await dbContext.EventReportDecisionExecutions
            .Where(execution =>
                execution.TenantId == tenantId
                && execution.DecisionId == decisionId
                && execution.State == EventReportDecisionExecutionState.CompletionPending
                && execution.ProcessingLeaseToken == leaseToken)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(execution => execution.ProcessingLeaseToken, (Guid?)null)
                .SetProperty(execution => execution.ProcessingLeaseExpiresAtUtc, (DateTime?)null)
                .SetProperty(execution => execution.LastFailureCode, normalizedFailureCode)
                .SetProperty(execution => execution.LastFailureAtUtc, failedAtUtc)
                .SetProperty(execution => execution.UpdatedAt, failedAtUtc)
                .SetProperty(execution => execution.Version, execution => execution.Version + 1),
                cancellationToken);
        return updated == 1;
    }

    private static void ValidateClaim(
        Guid tenantId,
        Guid decisionId,
        Guid leaseToken,
        DateTime claimedAtUtc,
        DateTime leaseExpiresAtUtc)
    {
        if (tenantId == Guid.Empty || decisionId == Guid.Empty || leaseToken == Guid.Empty)
        {
            throw new ArgumentException("Tenant, decision, and lease identifiers are required.");
        }

        if (claimedAtUtc.Kind != DateTimeKind.Utc || leaseExpiresAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Execution lease timestamps must be UTC.");
        }

        if (leaseExpiresAtUtc <= claimedAtUtc)
        {
            throw new ArgumentException("Execution lease expiration must be after its claim time.");
        }
    }

    private static void ValidateReceipt(
        Guid leaseToken,
        EventReportDecisionEnforcementReceiptKind receiptKind,
        Guid? receiptId,
        DateTime completedAtUtc)
    {
        if (leaseToken == Guid.Empty)
        {
            throw new ArgumentException("Execution lease token is required.", nameof(leaseToken));
        }

        if (completedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Enforcement completion timestamp must be UTC.", nameof(completedAtUtc));
        }

        if (receiptKind == EventReportDecisionEnforcementReceiptKind.None)
        {
            throw new ArgumentOutOfRangeException(nameof(receiptKind));
        }

        bool requiresReceiptId = receiptKind is
            EventReportDecisionEnforcementReceiptKind.LightModeration or
            EventReportDecisionEnforcementReceiptKind.HeavyRedaction;
        if (requiresReceiptId != receiptId.HasValue || receiptId == Guid.Empty)
        {
            throw new ArgumentException("Only moderation receipts require a non-empty receipt id.", nameof(receiptId));
        }
    }

    private static DateTime NormalizePostgresTimestamp(DateTime value) =>
        new(value.Ticks - (value.Ticks % 10), DateTimeKind.Utc);

    private static string NormalizeFailureCode(string failureCode)
    {
        if (string.IsNullOrWhiteSpace(failureCode))
        {
            throw new ArgumentException("A failure code is required.", nameof(failureCode));
        }

        string normalized = failureCode.Trim();
        if (normalized.Length > EventReportDecisionExecution.MaxFailureCodeLength)
        {
            throw new ArgumentException(
                $"Failure code cannot exceed {EventReportDecisionExecution.MaxFailureCodeLength} characters.",
                nameof(failureCode));
        }

        return normalized;
    }
}
