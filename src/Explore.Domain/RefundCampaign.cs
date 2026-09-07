using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed record RefundCampaignClaim(Guid LeaseToken, long ProcessingFence);

public sealed record RefundCampaignBatchOutcome(int Total, int Generated, int OperatorCases)
{
    public void Validate()
    {
        if (Total < 0 || Generated < 0 || OperatorCases < 0 || Generated + OperatorCases > Total)
        {
            throw new ArgumentOutOfRangeException(nameof(Total));
        }
    }
}

public sealed class RefundCampaign : ITenantEntity, IAuditableEntity, IConcurrencyAware
{
    private RefundCampaign()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Guid EventId { get; private set; }
    public RefundCampaignKind Kind { get; private set; }
    public RefundCampaignStatus Status { get; private set; }
    public string DecisionReason { get; private set; } = string.Empty;
    public DateTime DecisionAt { get; private set; }
    public long Cursor { get; private set; }
    public Guid? ProcessingLeaseToken { get; private set; }
    public Guid? ProcessingLeaseOwner { get; private set; }
    public DateTime? ProcessingLeaseExpiresAt { get; private set; }
    public long ProcessingFence { get; private set; }
    public int TotalPaymentCount { get; private set; }
    public int GeneratedCount { get; private set; }
    public int PendingCount { get; private set; }
    public int SucceededCount { get; private set; }
    public int FailedCount { get; private set; }
    public int UnknownCount { get; private set; }
    public int OperatorCaseCount { get; private set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public Guid ConcurrencyStamp { get; set; }

    public static RefundCampaign CreateCancellation(
        Guid id,
        Guid tenantId,
        Guid eventId,
        Guid actorId,
        string decisionReason,
        DateTime decidedAt) => Create(
            id, tenantId, eventId, actorId, RefundCampaignKind.EventCancellation, decisionReason, decidedAt);

    public static RefundCampaign CreateMaterialChange(
        Guid id,
        Guid tenantId,
        Guid eventId,
        Guid actorId,
        string decisionReason,
        DateTime decidedAt) => Create(
            id, tenantId, eventId, actorId, RefundCampaignKind.MaterialChange, decisionReason, decidedAt);

    public RefundCampaignClaim Claim(Guid ownerId, DateTime claimedAt, TimeSpan leaseDuration)
    {
        EnsureUtc(claimedAt, nameof(claimedAt));
        if (ownerId == Guid.Empty || leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentException("A campaign claim requires an owner and positive lease.");
        }
        if (Status is RefundCampaignStatus.Completed or RefundCampaignStatus.RequiresOperator)
        {
            throw new InvalidOperationException("A terminal refund campaign cannot be claimed.");
        }
        if (ProcessingLeaseExpiresAt > claimedAt)
        {
            throw new InvalidOperationException("The refund campaign is already leased.");
        }

        ProcessingFence = checked(ProcessingFence + 1);
        ProcessingLeaseToken = Guid.CreateVersion7();
        ProcessingLeaseOwner = ownerId;
        ProcessingLeaseExpiresAt = claimedAt.Add(leaseDuration);
        Status = RefundCampaignStatus.Processing;
        Touch(claimedAt);
        return new(ProcessingLeaseToken.Value, ProcessingFence);
    }

    public void CompleteBatch(
        RefundCampaignClaim claim,
        long? cursor,
        RefundCampaignBatchOutcome outcome,
        bool hasMore,
        DateTime completedAt)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(outcome);
        outcome.Validate();
        EnsureClaim(claim, completedAt);
        if (cursor.HasValue && cursor.Value <= Cursor)
        {
            throw new InvalidOperationException("Refund campaign cursor must advance monotonically.");
        }

        if (outcome.OperatorCases == 0)
        {
            Cursor = cursor ?? Cursor;
            TotalPaymentCount = checked(TotalPaymentCount + outcome.Total);
        }
        OperatorCaseCount = checked(OperatorCaseCount + outcome.OperatorCases);
        Status = outcome.OperatorCases > 0
            ? RefundCampaignStatus.RequiresOperator
            : hasMore ? RefundCampaignStatus.Pending : RefundCampaignStatus.Completed;
        ClearLease();
        Touch(completedAt);
    }

    public void RefreshOutcomes(
        int generated,
        int pending,
        int succeeded,
        int failed,
        int unknown,
        int operatorCases,
        DateTime observedAt)
    {
        EnsureUtc(observedAt, nameof(observedAt));
        if (generated < 0 || pending < 0 || succeeded < 0 || failed < 0 || unknown < 0 || operatorCases < 0 ||
            checked(pending + succeeded + failed + unknown) > generated)
        {
            throw new ArgumentOutOfRangeException(nameof(pending));
        }
        GeneratedCount = generated;
        PendingCount = pending;
        SucceededCount = succeeded;
        FailedCount = failed;
        UnknownCount = unknown;
        OperatorCaseCount = Math.Max(OperatorCaseCount, operatorCases);
        Touch(observedAt);
    }

    public void RequireOperator(DateTime observedAt)
    {
        EnsureUtc(observedAt, nameof(observedAt));
        if (Status != RefundCampaignStatus.RequiresOperator)
        {
            OperatorCaseCount = checked(OperatorCaseCount + 1);
        }
        Status = RefundCampaignStatus.RequiresOperator;
        ClearLease();
        Touch(observedAt);
    }

    public void Resume(DateTime requestedAt)
    {
        EnsureUtc(requestedAt, nameof(requestedAt));
        if (Status == RefundCampaignStatus.Completed)
        {
            throw new InvalidOperationException("A completed refund campaign cannot be resumed.");
        }
        Cursor = 0;
        TotalPaymentCount = 0;
        OperatorCaseCount = 0;
        Status = RefundCampaignStatus.Pending;
        ClearLease();
        Touch(requestedAt);
    }

    private static RefundCampaign Create(
        Guid id,
        Guid tenantId,
        Guid eventId,
        Guid actorId,
        RefundCampaignKind kind,
        string decisionReason,
        DateTime decidedAt)
    {
        EnsureUtc(decidedAt, nameof(decidedAt));
        string reason = decisionReason?.Trim() ?? string.Empty;
        if (id == Guid.Empty || tenantId == Guid.Empty || eventId == Guid.Empty || actorId == Guid.Empty ||
            reason.Length is 0 or > 500 || reason.Any(char.IsControl))
        {
            throw new ArgumentException("A refund campaign requires valid tenant, event, actor, and decision evidence.");
        }
        return new()
        {
            Id = id,
            TenantId = tenantId,
            EventId = eventId,
            Kind = kind,
            Status = RefundCampaignStatus.Pending,
            DecisionReason = reason,
            DecisionAt = decidedAt,
            CreatedAt = decidedAt,
            CreatedBy = actorId,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
    }

    private void EnsureClaim(RefundCampaignClaim claim, DateTime observedAt)
    {
        EnsureUtc(observedAt, nameof(observedAt));
        if (ProcessingLeaseToken != claim.LeaseToken || ProcessingFence != claim.ProcessingFence ||
            ProcessingLeaseExpiresAt < observedAt)
        {
            throw new InvalidOperationException("Refund campaign claim is stale.");
        }
    }

    private void ClearLease()
    {
        ProcessingLeaseToken = null;
        ProcessingLeaseOwner = null;
        ProcessingLeaseExpiresAt = null;
    }

    private void Touch(DateTime observedAt)
    {
        UpdatedAt = observedAt;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    private static void EnsureUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Timestamp must be UTC.", parameterName);
        }
    }
}
