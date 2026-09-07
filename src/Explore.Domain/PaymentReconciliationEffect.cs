using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class PaymentReconciliationEffect : ITenantEntity, IAuditableEntity
{
    public const int MaxLeaseOwnerLength = 200;
    public const int MaxFailureCodeLength = 120;

    private PaymentReconciliationEffect()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Guid RegistrationOrderId { get; private set; }
    public Guid PaymentAttemptId { get; private set; }
    public Guid? SourceIncomingWebhookMessageId { get; private set; }
    public Guid? CheckoutDispatchEffectId { get; private set; }
    public DateTime? CheckoutDispatchUnknownAt { get; private set; }
    public long? CheckoutDispatchProcessingFence { get; private set; }
    public int? CheckoutDispatchAttemptCount { get; private set; }
    public OutboxMessageStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public long ProcessingFence { get; private set; }
    public string? ProcessingLeaseOwner { get; private set; }
    public Guid? ProcessingLeaseToken { get; private set; }
    public DateTime? ProcessingLeaseExpiresAt { get; private set; }
    public DateTime? NextAttemptAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public DateTime? ParkedAt { get; private set; }
    public DateTime? UnknownAt { get; private set; }
    public string? LastFailureCode { get; private set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static PaymentReconciliationEffect Create(
        PaymentAttempt attempt,
        DateTime dueAt,
        Guid? sourceIncomingWebhookMessageId = null,
        Guid? checkoutDispatchEffectId = null,
        DateTime? checkoutDispatchUnknownAt = null,
        long? checkoutDispatchProcessingFence = null,
        int? checkoutDispatchAttemptCount = null)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        EnsureUtc(dueAt, nameof(dueAt));
        return new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = attempt.TenantId,
            RegistrationOrderId = attempt.RegistrationOrderId,
            PaymentAttemptId = attempt.Id,
            SourceIncomingWebhookMessageId = sourceIncomingWebhookMessageId,
            CheckoutDispatchEffectId = checkoutDispatchEffectId,
            CheckoutDispatchUnknownAt = checkoutDispatchUnknownAt,
            CheckoutDispatchProcessingFence = checkoutDispatchProcessingFence,
            CheckoutDispatchAttemptCount = checkoutDispatchAttemptCount,
            Status = OutboxMessageStatus.Pending,
            NextAttemptAt = dueAt,
            CreatedAt = dueAt
        };
    }

    public void MakeDue(
        DateTime dueAt,
        Guid? sourceIncomingWebhookMessageId,
        Guid? checkoutDispatchEffectId = null,
        DateTime? checkoutDispatchUnknownAt = null,
        long? checkoutDispatchProcessingFence = null,
        int? checkoutDispatchAttemptCount = null)
    {
        EnsureUtc(dueAt, nameof(dueAt));
        if (Status is OutboxMessageStatus.Completed or OutboxMessageStatus.DeadLettered)
        {
            return;
        }

        SourceIncomingWebhookMessageId ??= sourceIncomingWebhookMessageId;
        if (checkoutDispatchEffectId.HasValue)
        {
            CheckoutDispatchEffectId = checkoutDispatchEffectId;
            CheckoutDispatchUnknownAt = checkoutDispatchUnknownAt;
            CheckoutDispatchProcessingFence = checkoutDispatchProcessingFence;
            CheckoutDispatchAttemptCount = checkoutDispatchAttemptCount;
        }
        if (Status != OutboxMessageStatus.Processing && (!NextAttemptAt.HasValue || dueAt < NextAttemptAt))
        {
            Status = OutboxMessageStatus.Failed;
            NextAttemptAt = dueAt;
            UnknownAt = null;
            UpdatedAt = dueAt;
        }
    }

    public void Claim(string leaseOwner, Guid leaseToken, DateTime leaseExpiresAt, DateTime claimedAt)
    {
        EnsureUtc(claimedAt, nameof(claimedAt));
        EnsureUtc(leaseExpiresAt, nameof(leaseExpiresAt));
        string owner = string.IsNullOrWhiteSpace(leaseOwner) ? string.Empty : leaseOwner.Trim();
        if (owner.Length is 0 or > MaxLeaseOwnerLength || leaseToken == Guid.Empty || leaseExpiresAt <= claimedAt ||
            Status is not (OutboxMessageStatus.Pending or OutboxMessageStatus.Failed) || NextAttemptAt > claimedAt)
        {
            throw new InvalidOperationException("Payment reconciliation effect is not claimable.");
        }

        Status = OutboxMessageStatus.Processing;
        ProcessingLeaseOwner = owner;
        ProcessingLeaseToken = leaseToken;
        ProcessingLeaseExpiresAt = leaseExpiresAt;
        ProcessingFence = checked(ProcessingFence + 1);
        AttemptCount = checked(AttemptCount + 1);
        NextAttemptAt = null;
        UpdatedAt = claimedAt;
    }

    public void RecoverExpiredClaim(DateTime recoveredAt)
    {
        EnsureUtc(recoveredAt, nameof(recoveredAt));
        if (Status != OutboxMessageStatus.Processing || ProcessingLeaseExpiresAt > recoveredAt)
        {
            throw new InvalidOperationException("Only an expired payment reconciliation claim can be recovered.");
        }

        Retry("payment_reconciliation_interrupted", recoveredAt, recoveredAt, unknown: true);
    }

    public void Retry(string failureCode, DateTime nextAttemptAt, DateTime settledAt, bool unknown)
    {
        EnsureUtc(nextAttemptAt, nameof(nextAttemptAt));
        EnsureUtc(settledAt, nameof(settledAt));
        EnsureActiveClaim();
        Status = OutboxMessageStatus.Failed;
        LastFailureCode = BoundFailure(failureCode);
        NextAttemptAt = nextAttemptAt;
        UnknownAt = unknown ? settledAt : null;
        ClearLease();
        UpdatedAt = settledAt;
    }

    public void Complete(DateTime completedAt)
    {
        EnsureUtc(completedAt, nameof(completedAt));
        EnsureActiveClaim();
        Status = OutboxMessageStatus.Completed;
        CompletedAt = completedAt;
        LastFailureCode = null;
        ClearLease();
        UpdatedAt = completedAt;
    }

    public void Park(string failureCode, DateTime parkedAt)
    {
        EnsureUtc(parkedAt, nameof(parkedAt));
        EnsureActiveClaim();
        Status = OutboxMessageStatus.DeadLettered;
        ParkedAt = parkedAt;
        LastFailureCode = BoundFailure(failureCode);
        ClearLease();
        UpdatedAt = parkedAt;
    }

    public void EnsureClaim(Guid leaseToken, long processingFence, DateTime observedAt)
    {
        EnsureUtc(observedAt, nameof(observedAt));
        if (Status != OutboxMessageStatus.Processing || ProcessingLeaseToken != leaseToken ||
            ProcessingFence != processingFence || ProcessingLeaseExpiresAt <= observedAt)
        {
            throw new InvalidOperationException("Payment reconciliation claim is stale.");
        }
    }

    private void EnsureActiveClaim()
    {
        if (Status != OutboxMessageStatus.Processing)
        {
            throw new InvalidOperationException("Payment reconciliation effect has no active claim.");
        }
    }

    private void ClearLease()
    {
        ProcessingLeaseOwner = null;
        ProcessingLeaseToken = null;
        ProcessingLeaseExpiresAt = null;
    }

    private static string BoundFailure(string value) =>
        string.IsNullOrWhiteSpace(value) ? "payment_reconciliation_unknown" : value.Trim()[..Math.Min(value.Trim().Length, MaxFailureCodeLength)];

    private static void EnsureUtc(DateTime value, string parameterName)
    {
        if (value == default || value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Timestamp must be non-default UTC.", parameterName);
        }
    }
}
