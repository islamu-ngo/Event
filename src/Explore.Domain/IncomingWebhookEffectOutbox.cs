using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class IncomingWebhookEffectOutbox : ITenantEntity, IAuditableEntity
{
    public const int MaxProviderDecisionIdLength = 256;
    public const int MaxLeaseOwnerLength = 200;
    public const int MaxFailureCategoryLength = 100;
    public const int MaxSafeDetailLength = 1024;

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; private set; }
    public Guid IncomingWebhookMessageId { get; private set; }
    public IncomingWebhookMessage? IncomingWebhookMessage { get; private set; }
    public string Provider { get; private set; } = string.Empty;
    public string ProviderDecisionId { get; private set; } = string.Empty;
    public string EffectKind { get; private set; } = string.Empty;
    public string PayloadSha256 { get; private set; } = string.Empty;
    public OutboxMessageStatus Status { get; private set; }
    public int ProcessingGeneration { get; private set; }
    public long ProcessingFence { get; private set; }
    public int AttemptCount { get; private set; }
    public string? ProcessingLeaseOwner { get; private set; }
    public Guid? ProcessingLeaseToken { get; private set; }
    public DateTime? ProcessingLeaseExpiresAt { get; private set; }
    public DateTime? ProcessingStartedAt { get; private set; }
    public DateTime? NextAttemptAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public DateTime? DeadLetteredAt { get; private set; }
    public string? FailureCategory { get; private set; }
    public string? SafeDetail { get; private set; }

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static IncomingWebhookEffectOutbox CreatePending(
        Guid tenantId,
        Guid incomingWebhookMessageId,
        string provider,
        string providerDecisionId,
        string effectKind,
        string payloadSha256,
        DateTime createdAt)
    {
        if (tenantId == Guid.Empty || incomingWebhookMessageId == Guid.Empty)
        {
            throw new ArgumentException("Tenant and incoming webhook identifiers are required.");
        }

        if (createdAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Timestamp must use UTC kind.", nameof(createdAt));
        }

        return new IncomingWebhookEffectOutbox
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            IncomingWebhookMessageId = incomingWebhookMessageId,
            Provider = IncomingWebhookMessage.NormalizeRequired(
                provider,
                IncomingWebhookMessage.MaxProviderLength,
                nameof(provider)).ToLowerInvariant(),
            ProviderDecisionId = IncomingWebhookMessage.NormalizeRequired(
                providerDecisionId,
                MaxProviderDecisionIdLength,
                nameof(providerDecisionId)),
            EffectKind = IncomingWebhookEffectReceipt.NormalizeEffectKind(effectKind),
            PayloadSha256 = IncomingWebhookMessage.NormalizePayloadHash(payloadSha256),
            Status = OutboxMessageStatus.Pending,
            ProcessingGeneration = 1,
            CreatedAt = createdAt
        };
    }

    public void Claim(string leaseOwner, Guid leaseToken, DateTime leaseExpiresAt, DateTime claimedAt)
    {
        EnsureUtc(claimedAt, nameof(claimedAt));
        EnsureUtc(leaseExpiresAt, nameof(leaseExpiresAt));
        if (Status is not (OutboxMessageStatus.Pending or OutboxMessageStatus.Failed) ||
            (NextAttemptAt is not null && NextAttemptAt > claimedAt))
        {
            throw new InvalidOperationException("Incoming webhook effect is not due for processing.");
        }

        if (leaseToken == Guid.Empty || leaseExpiresAt <= claimedAt)
        {
            throw new ArgumentException("A valid future processing lease is required.");
        }

        ProcessingLeaseOwner = IncomingWebhookMessage.NormalizeRequired(
            leaseOwner,
            MaxLeaseOwnerLength,
            nameof(leaseOwner));
        ProcessingLeaseToken = leaseToken;
        ProcessingLeaseExpiresAt = leaseExpiresAt;
        ProcessingStartedAt = claimedAt;
        NextAttemptAt = null;
        ProcessingFence = checked(ProcessingFence + 1);
        AttemptCount = checked(AttemptCount + 1);
        Status = OutboxMessageStatus.Processing;
        UpdatedAt = claimedAt;
    }

    public void RecoverExpiredClaim(DateTime recoveredAt)
    {
        EnsureUtc(recoveredAt, nameof(recoveredAt));
        if (Status != OutboxMessageStatus.Processing ||
            ProcessingLeaseExpiresAt is null ||
            ProcessingLeaseExpiresAt > recoveredAt)
        {
            throw new InvalidOperationException("Only an expired incoming webhook effect claim can be recovered.");
        }

        Status = OutboxMessageStatus.Failed;
        NextAttemptAt = recoveredAt;
        FailureCategory = "coop_effect_lease_expired";
        SafeDetail = "The previous effect lease expired before settlement.";
        ClearLease();
        UpdatedAt = recoveredAt;
    }

    public void Complete(
        Guid leaseToken,
        long processingFence,
        int processingGeneration,
        DateTime completedAt)
    {
        EnsureActiveClaim(leaseToken, processingFence, processingGeneration, completedAt);
        Status = OutboxMessageStatus.Completed;
        CompletedAt = completedAt;
        FailureCategory = null;
        SafeDetail = null;
        ClearLease();
        UpdatedAt = completedAt;
    }

    public void ScheduleRetry(
        Guid leaseToken,
        long processingFence,
        int processingGeneration,
        string failureCategory,
        string safeDetail,
        DateTime nextAttemptAt,
        DateTime failedAt)
    {
        EnsureActiveClaim(leaseToken, processingFence, processingGeneration, failedAt);
        EnsureUtc(nextAttemptAt, nameof(nextAttemptAt));
        if (nextAttemptAt <= failedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(nextAttemptAt));
        }

        Status = OutboxMessageStatus.Failed;
        FailureCategory = NormalizeFailureCategory(failureCategory);
        SafeDetail = NormalizeSafeDetail(safeDetail);
        NextAttemptAt = nextAttemptAt;
        ClearLease();
        UpdatedAt = failedAt;
    }

    public void DeadLetter(
        Guid leaseToken,
        long processingFence,
        int processingGeneration,
        string failureCategory,
        string safeDetail,
        DateTime deadLetteredAt)
    {
        EnsureActiveClaim(leaseToken, processingFence, processingGeneration, deadLetteredAt);
        Status = OutboxMessageStatus.DeadLettered;
        FailureCategory = NormalizeFailureCategory(failureCategory);
        SafeDetail = NormalizeSafeDetail(safeDetail);
        DeadLetteredAt = deadLetteredAt;
        NextAttemptAt = null;
        ClearLease();
        UpdatedAt = deadLetteredAt;
    }

    public void Redrive(int expectedProcessingGeneration, DateTime redrivenAt)
    {
        EnsureUtc(redrivenAt, nameof(redrivenAt));
        if (Status != OutboxMessageStatus.DeadLettered)
        {
            throw new InvalidOperationException("Only dead-lettered incoming webhook effects can be redriven.");
        }

        if (ProcessingGeneration != expectedProcessingGeneration)
        {
            throw new InvalidOperationException("Incoming webhook effect generation changed before redrive.");
        }

        Status = OutboxMessageStatus.Pending;
        ProcessingGeneration = checked(ProcessingGeneration + 1);
        AttemptCount = 0;
        NextAttemptAt = redrivenAt;
        DeadLetteredAt = null;
        FailureCategory = null;
        SafeDetail = null;
        ClearLease();
        UpdatedAt = redrivenAt;
    }

    public void AcknowledgeResolution(string decisionCode, DateTime acknowledgedAt)
    {
        EnsureUtc(acknowledgedAt, nameof(acknowledgedAt));
        Status = OutboxMessageStatus.Completed;
        CompletedAt = acknowledgedAt;
        NextAttemptAt = null;
        FailureCategory = NormalizeFailureCategory(decisionCode);
        SafeDetail = "Organizer acknowledged registration provider reconciliation item.";
        ClearLease();
        UpdatedAt = acknowledgedAt;
    }

    private void EnsureActiveClaim(
        Guid leaseToken,
        long processingFence,
        int processingGeneration,
        DateTime observedAt)
    {
        EnsureUtc(observedAt, nameof(observedAt));
        if (Status != OutboxMessageStatus.Processing ||
            ProcessingLeaseToken != leaseToken ||
            ProcessingFence != processingFence ||
            ProcessingGeneration != processingGeneration ||
            ProcessingLeaseExpiresAt is null ||
            ProcessingLeaseExpiresAt <= observedAt)
        {
            throw new InvalidOperationException("The incoming webhook effect processing claim is no longer active.");
        }
    }

    private void ClearLease()
    {
        ProcessingLeaseOwner = null;
        ProcessingLeaseToken = null;
        ProcessingLeaseExpiresAt = null;
        ProcessingStartedAt = null;
    }

    private static string NormalizeFailureCategory(string value)
    {
        var normalized = IncomingWebhookMessage.NormalizeRequired(
            value,
            MaxFailureCategoryLength,
            nameof(value)).ToLowerInvariant();
        if (normalized.Any(character => !(char.IsAsciiLetterOrDigit(character) || character == '_')))
        {
            throw new ArgumentException("Failure category contains unsupported characters.", nameof(value));
        }

        return normalized;
    }

    private static string NormalizeSafeDetail(string value)
    {
        var normalized = IncomingWebhookMessage.NormalizeRequired(
            value,
            MaxSafeDetailLength,
            nameof(value));
        if (normalized.Any(char.IsControl))
        {
            throw new ArgumentException("Safe detail contains control characters.", nameof(value));
        }

        return normalized;
    }

    private static void EnsureUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Timestamp must use UTC kind.", parameterName);
        }
    }
}
