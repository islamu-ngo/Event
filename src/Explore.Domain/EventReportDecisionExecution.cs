using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class EventReportDecisionExecution : ITenantEntity
{
    public const int MaxFailureCodeLength = 100;

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; private set; } = null!;
    public Guid ReportId { get; private set; }
    public EventReport Report { get; private set; } = null!;
    public Guid DecisionId { get; private set; }
    public EventReportDecision Decision { get; private set; } = null!;
    public EventReportDecisionExecutionState State { get; private set; }
    public EventReportDecisionEnforcementReceiptKind EnforcementReceiptKind { get; private set; }
    public Guid? EnforcementReceiptId { get; private set; }
    public Guid? ModerationRecordId { get; private set; }
    public EventModerationRecord? ModerationRecord { get; private set; }
    public Guid? ProcessingLeaseToken { get; private set; }
    public DateTime? ProcessingLeaseExpiresAtUtc { get; private set; }
    public int AttemptCount { get; private set; }
    public string? LastFailureCode { get; private set; }
    public DateTime? LastFailureAtUtc { get; private set; }
    public DateTime? EnforcementCompletedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public long Version { get; private set; }

    public static EventReportDecisionExecution Create(
        Guid tenantId,
        Guid reportId,
        Guid decisionId,
        DateTime createdAtUtc,
        Guid? executionId = null)
    {
        RequireGuid(tenantId, nameof(tenantId));
        RequireGuid(reportId, nameof(reportId));
        RequireGuid(decisionId, nameof(decisionId));
        EnsureUtc(createdAtUtc, nameof(createdAtUtc));
        if (executionId == Guid.Empty)
        {
            throw new ArgumentException("Execution id cannot be empty.", nameof(executionId));
        }

        return new EventReportDecisionExecution
        {
            Id = executionId ?? Guid.CreateVersion7(),
            TenantId = tenantId,
            ReportId = reportId,
            DecisionId = decisionId,
            State = EventReportDecisionExecutionState.Requested,
            EnforcementReceiptKind = EventReportDecisionEnforcementReceiptKind.None,
            CreatedAt = createdAtUtc,
            Version = 1
        };
    }

    public void ClaimEnforcement(Guid leaseToken, DateTime claimedAtUtc, DateTime leaseExpiresAtUtc)
    {
        ValidateLeaseWindow(leaseToken, claimedAtUtc, leaseExpiresAtUtc);
        bool canClaim = State == EventReportDecisionExecutionState.Requested
            || (State == EventReportDecisionExecutionState.InProgress
                && ProcessingLeaseExpiresAtUtc <= claimedAtUtc);
        if (!canClaim)
        {
            throw new InvalidOperationException("The report-decision enforcement work is not claimable.");
        }

        State = EventReportDecisionExecutionState.InProgress;
        ProcessingLeaseToken = leaseToken;
        ProcessingLeaseExpiresAtUtc = leaseExpiresAtUtc;
        AttemptCount++;
        LastFailureCode = null;
        LastFailureAtUtc = null;
        Touch(claimedAtUtc);
    }

    public void ClaimCompletion(Guid leaseToken, DateTime claimedAtUtc, DateTime leaseExpiresAtUtc)
    {
        ValidateLeaseWindow(leaseToken, claimedAtUtc, leaseExpiresAtUtc);
        bool canClaim = State == EventReportDecisionExecutionState.CompletionPending
            && (ProcessingLeaseToken is null || ProcessingLeaseExpiresAtUtc <= claimedAtUtc);
        if (!canClaim)
        {
            throw new InvalidOperationException("The report-decision completion work is not claimable.");
        }

        ProcessingLeaseToken = leaseToken;
        ProcessingLeaseExpiresAtUtc = leaseExpiresAtUtc;
        AttemptCount++;
        LastFailureCode = null;
        LastFailureAtUtc = null;
        Touch(claimedAtUtc);
    }

    public void RecordEnforcementReceipt(
        Guid leaseToken,
        EventReportDecisionEnforcementReceiptKind receiptKind,
        Guid? receiptId,
        DateTime completedAtUtc)
    {
        EnsureActiveLease(leaseToken, completedAtUtc);
        if (receiptKind == EventReportDecisionEnforcementReceiptKind.None)
        {
            throw new ArgumentOutOfRangeException(nameof(receiptKind), "An enforcement receipt kind is required.");
        }

        bool requiresReceiptId = receiptKind is
            EventReportDecisionEnforcementReceiptKind.LightModeration or
            EventReportDecisionEnforcementReceiptKind.HeavyRedaction;
        if (requiresReceiptId != receiptId.HasValue || receiptId == Guid.Empty)
        {
            throw new ArgumentException(
                "Only light- and heavy-moderation receipts require a non-empty receipt id.",
                nameof(receiptId));
        }

        State = EventReportDecisionExecutionState.CompletionPending;
        EnforcementReceiptKind = receiptKind;
        EnforcementReceiptId = receiptId;
        ModerationRecordId = requiresReceiptId ? receiptId : null;
        EnforcementCompletedAtUtc = completedAtUtc;
        ProcessingLeaseToken = null;
        ProcessingLeaseExpiresAtUtc = null;
        LastFailureCode = null;
        LastFailureAtUtc = null;
        Touch(completedAtUtc);
    }

    public void ReleaseEnforcementClaim(Guid leaseToken, string failureCode, DateTime failedAtUtc)
    {
        EnsureActiveLease(leaseToken, failedAtUtc);
        State = EventReportDecisionExecutionState.Requested;
        EnforcementReceiptKind = EventReportDecisionEnforcementReceiptKind.None;
        EnforcementReceiptId = null;
        ModerationRecordId = null;
        EnforcementCompletedAtUtc = null;
        ProcessingLeaseToken = null;
        ProcessingLeaseExpiresAtUtc = null;
        LastFailureCode = NormalizeFailureCode(failureCode);
        LastFailureAtUtc = failedAtUtc;
        Touch(failedAtUtc);
    }

    public void ReleaseCompletionClaim(Guid leaseToken, string failureCode, DateTime failedAtUtc)
    {
        EnsureCompletionLease(leaseToken, failedAtUtc);
        ProcessingLeaseToken = null;
        ProcessingLeaseExpiresAtUtc = null;
        LastFailureCode = NormalizeFailureCode(failureCode);
        LastFailureAtUtc = failedAtUtc;
        Touch(failedAtUtc);
    }

    public void Complete(Guid leaseToken, DateTime completedAtUtc)
    {
        EnsureCompletionLease(leaseToken, completedAtUtc);
        State = EventReportDecisionExecutionState.Completed;
        ProcessingLeaseToken = null;
        ProcessingLeaseExpiresAtUtc = null;
        LastFailureCode = null;
        LastFailureAtUtc = null;
        CompletedAtUtc = completedAtUtc;
        Touch(completedAtUtc);
    }

    private void EnsureActiveLease(Guid leaseToken, DateTime utcNow)
    {
        EnsureUtc(utcNow, nameof(utcNow));
        if (leaseToken == Guid.Empty
            || State != EventReportDecisionExecutionState.InProgress
            || ProcessingLeaseToken != leaseToken
            || ProcessingLeaseExpiresAtUtc is not { } expiresAt
            || expiresAt <= utcNow)
        {
            throw new InvalidOperationException("The report-decision enforcement lease is not active.");
        }
    }

    private void EnsureCompletionLease(Guid leaseToken, DateTime utcNow)
    {
        EnsureUtc(utcNow, nameof(utcNow));
        if (leaseToken == Guid.Empty
            || State != EventReportDecisionExecutionState.CompletionPending
            || ProcessingLeaseToken != leaseToken
            || ProcessingLeaseExpiresAtUtc is not { } expiresAt
            || expiresAt <= utcNow)
        {
            throw new InvalidOperationException("The report-decision completion lease is not active.");
        }
    }

    private void Touch(DateTime utcNow)
    {
        UpdatedAt = utcNow;
        Version++;
    }

    private static string NormalizeFailureCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A failure code is required.", nameof(value));
        }

        string normalized = value.Trim();
        if (normalized.Length > MaxFailureCodeLength)
        {
            throw new ArgumentException($"Failure code cannot exceed {MaxFailureCodeLength} characters.", nameof(value));
        }

        return normalized;
    }

    private static void RequireGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty identifier is required.", parameterName);
        }
    }

    private static void EnsureUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The timestamp must be UTC.", parameterName);
        }
    }

    private static void ValidateLeaseWindow(
        Guid leaseToken,
        DateTime claimedAtUtc,
        DateTime leaseExpiresAtUtc)
    {
        RequireGuid(leaseToken, nameof(leaseToken));
        EnsureUtc(claimedAtUtc, nameof(claimedAtUtc));
        EnsureUtc(leaseExpiresAtUtc, nameof(leaseExpiresAtUtc));
        if (leaseExpiresAtUtc <= claimedAtUtc)
        {
            throw new ArgumentException("Execution lease expiration must be after its claim time.", nameof(leaseExpiresAtUtc));
        }
    }
}
