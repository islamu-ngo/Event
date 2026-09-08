using Explore.Domain.Interfaces;

namespace Explore.Domain;

public enum FairReturnOrchestrationEffectStatus
{
    Pending = 1,
    Processing = 2,
    Completed = 3,
    DeadLettered = 4,
    Unknown = 5,
}

public sealed class WaitlistPaymentIntent :
    ITenantEntity,
    IAuditableEntity,
    IConcurrencyAware
{
    private Guid _tenantId;

    private WaitlistPaymentIntent()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId
    {
        get => _tenantId;
        set => TenantIdentity.Set(
            ref _tenantId,
            value,
            nameof(WaitlistPaymentIntent));
    }
    public Guid FairReturnSourceBindingId { get; private set; }
    public Guid ReplacementPaymentAttemptId { get; private set; }
    public Guid ReservedRefundAttemptId { get; private set; }
    public Guid OriginalPaymentAllocationId { get; private set; }
    public Guid StableOperationId { get; private set; }
    public Guid RefundIntentId { get; private set; }
    public string ProviderIdempotencyKey { get; private set; } = string.Empty;
    public DateTime? ReplacementPaymentSettledAt { get; private set; }
    public Guid ConcurrencyStamp { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static WaitlistPaymentIntent Create(
        Guid id,
        FairReturnSourceBinding binding,
        RefundAttempt reservedRefundAttempt,
        Guid replacementPaymentAttemptId,
        Guid originalPaymentAllocationId,
        Guid stableOperationId,
        Guid refundIntentId,
        DateTime createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(
            reservedRefundAttempt);
        foreach ((Guid Value, string Name) in new[]
                 {
                     (id, nameof(id)),
                     (replacementPaymentAttemptId,
                         nameof(replacementPaymentAttemptId)),
                     (originalPaymentAllocationId,
                         nameof(originalPaymentAllocationId)),
                     (stableOperationId,
                         nameof(stableOperationId)),
                     (refundIntentId,
                         nameof(refundIntentId)),
                 })
        {
            FairReturnSupplyPolicy.RequireUuidV7(
                Value,
                Name);
        }
        if (binding.TenantId !=
                reservedRefundAttempt.TenantId
            || reservedRefundAttempt.PaymentAttemptId ==
                replacementPaymentAttemptId)
        {
            throw new InvalidOperationException(
                "Refund reservation authority is invalid.");
        }
        string providerIdempotencyKey =
            reservedRefundAttempt.ProviderIdempotencyKey;
        if (string.IsNullOrWhiteSpace(
                providerIdempotencyKey))
        {
            throw new InvalidOperationException(
                "Refund idempotency is required.");
        }
        DateTime createdAt =
            FairReturnSupplyPolicy.RequireUtc(
                createdAtUtc,
                nameof(createdAtUtc));
        return new WaitlistPaymentIntent
        {
            Id = id,
            TenantId = binding.TenantId,
            FairReturnSourceBindingId = binding.Id,
            ReplacementPaymentAttemptId =
                replacementPaymentAttemptId,
            ReservedRefundAttemptId =
                reservedRefundAttempt.Id,
            OriginalPaymentAllocationId =
                originalPaymentAllocationId,
            StableOperationId = stableOperationId,
            RefundIntentId = refundIntentId,
            ProviderIdempotencyKey =
                providerIdempotencyKey,
            ConcurrencyStamp = Guid.CreateVersion7(),
            CreatedAt = createdAt,
        };
    }

    public bool ObserveReplacementSettlement(
        DateTime settledAtUtc)
    {
        DateTime settledAt =
            FairReturnSupplyPolicy.RequireUtc(
                settledAtUtc,
                nameof(settledAtUtc));
        if (ReplacementPaymentSettledAt.HasValue)
        {
            return false;
        }
        ReplacementPaymentSettledAt = settledAt;
        UpdatedAt = settledAt;
        ConcurrencyStamp = Guid.CreateVersion7();
        return true;
    }
}

public sealed class FairReturnOrchestrationEffect :
    ITenantEntity,
    IAuditableEntity,
    IConcurrencyAware
{
    private Guid _tenantId;

    private FairReturnOrchestrationEffect()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId
    {
        get => _tenantId;
        set => TenantIdentity.Set(
            ref _tenantId,
            value,
            nameof(FairReturnOrchestrationEffect));
    }
    public Guid WaitlistPaymentIntentId { get; private set; }
    public Guid StableOperationId { get; private set; }
    public long StableCursor { get; private set; }
    public int StatusId { get; private set; }
    public DateTime NextAttemptAt { get; private set; }
    public DateTime? LeaseExpiresAt { get; private set; }
    public string? LeaseOwner { get; private set; }
    public long ProcessingFence { get; private set; }
    public int AttemptCount { get; private set; }
    public int MaximumAttempts { get; private set; }
    public string? LastFailureCode { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public DateTime? DeadLetteredAt { get; private set; }
    public Guid ConcurrencyStamp { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static FairReturnOrchestrationEffect Create(
        Guid id,
        WaitlistPaymentIntent intent,
        int maximumAttempts,
        DateTime dueAtUtc)
    {
        ArgumentNullException.ThrowIfNull(intent);
        FairReturnSupplyPolicy.RequireUuidV7(id, nameof(id));
        if (maximumAttempts is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumAttempts));
        }
        DateTime dueAt =
            FairReturnSupplyPolicy.RequireUtc(
                dueAtUtc,
                nameof(dueAtUtc));
        return new FairReturnOrchestrationEffect
        {
            Id = id,
            TenantId = intent.TenantId,
            WaitlistPaymentIntentId = intent.Id,
            StableOperationId =
                intent.StableOperationId,
            StableCursor = dueAt.Ticks,
            StatusId =
                (int)FairReturnOrchestrationEffectStatus
                    .Pending,
            NextAttemptAt = dueAt,
            MaximumAttempts = maximumAttempts,
            ConcurrencyStamp = Guid.CreateVersion7(),
            CreatedAt = dueAt,
        };
    }

    public bool TryClaim(
        string leaseOwner,
        DateTime claimedAtUtc,
        TimeSpan leaseDuration)
    {
        DateTime claimedAt =
            FairReturnSupplyPolicy.RequireUtc(
                claimedAtUtc,
                nameof(claimedAtUtc));
        string owner = leaseOwner?.Trim()
            ?? string.Empty;
        if (owner.Length is < 1 or > 64
            || leaseDuration <= TimeSpan.Zero
            || leaseDuration > TimeSpan.FromHours(1))
        {
            throw new ArgumentException(
                "Lease is invalid.",
                nameof(leaseOwner));
        }
        bool isDue =
            StatusId ==
                (int)FairReturnOrchestrationEffectStatus
                    .Pending
            && NextAttemptAt <= claimedAt;
        bool leaseExpired =
            StatusId ==
                (int)FairReturnOrchestrationEffectStatus
                    .Processing
            && LeaseExpiresAt <= claimedAt;
        if (!isDue && !leaseExpired)
        {
            return false;
        }
        StatusId =
            (int)FairReturnOrchestrationEffectStatus
                .Processing;
        LeaseOwner = owner;
        LeaseExpiresAt = claimedAt.Add(
            leaseDuration);
        ProcessingFence = checked(
            ProcessingFence + 1);
        AttemptCount = checked(AttemptCount + 1);
        UpdatedAt = claimedAt;
        ConcurrencyStamp = Guid.CreateVersion7();
        return true;
    }

    public bool Complete(
        long processingFence,
        DateTime completedAtUtc)
    {
        DateTime completedAt =
            FairReturnSupplyPolicy.RequireUtc(
                completedAtUtc,
                nameof(completedAtUtc));
        if (!Owns(processingFence))
        {
            return false;
        }
        StatusId =
            (int)FairReturnOrchestrationEffectStatus
                .Completed;
        CompletedAt = completedAt;
        ClearLease();
        UpdatedAt = completedAt;
        ConcurrencyStamp = Guid.CreateVersion7();
        return true;
    }

    public FairReturnOrchestrationEffectStatus Fail(
        long processingFence,
        string failureCode,
        bool retryable,
        DateTime failedAtUtc,
        DateTime retryAtUtc)
    {
        DateTime failedAt =
            FairReturnSupplyPolicy.RequireUtc(
                failedAtUtc,
                nameof(failedAtUtc));
        DateTime retryAt =
            FairReturnSupplyPolicy.RequireUtc(
                retryAtUtc,
                nameof(retryAtUtc));
        if (!Owns(processingFence))
        {
            return (FairReturnOrchestrationEffectStatus)
                StatusId;
        }
        string code = failureCode?.Trim()
            .ToUpperInvariant() ?? string.Empty;
        if (code.Length is < 1 or > 64)
        {
            throw new ArgumentException(
                "Failure code is invalid.",
                nameof(failureCode));
        }
        LastFailureCode = code;
        bool deadLetter =
            !retryable
            || AttemptCount >= MaximumAttempts;
        StatusId = deadLetter
            ? (int)FairReturnOrchestrationEffectStatus
                .DeadLettered
            : (int)FairReturnOrchestrationEffectStatus
                .Pending;
        if (deadLetter)
        {
            DeadLetteredAt = failedAt;
        }
        else
        {
            NextAttemptAt = retryAt;
        }
        ClearLease();
        UpdatedAt = failedAt;
        ConcurrencyStamp = Guid.CreateVersion7();
        return (FairReturnOrchestrationEffectStatus)
            StatusId;
    }

    public bool EnterRecovery(
        long recoveryFence,
        DateTime occurredAtUtc)
    {
        DateTime occurredAt =
            FairReturnSupplyPolicy.RequireUtc(
                occurredAtUtc,
                nameof(occurredAtUtc));
        if (StatusId is
                (int)FairReturnOrchestrationEffectStatus.Completed or
                (int)FairReturnOrchestrationEffectStatus.DeadLettered ||
            recoveryFence <= ProcessingFence)
        {
            return false;
        }

        if (StatusId ==
            (int)FairReturnOrchestrationEffectStatus.Processing)
        {
            StatusId =
                (int)FairReturnOrchestrationEffectStatus.Unknown;
            LastFailureCode =
                "RECOVERY_PROVIDER_AMBIGUOUS";
        }

        ProcessingFence = recoveryFence;
        ClearLease();
        UpdatedAt = occurredAt;
        ConcurrencyStamp = Guid.CreateVersion7();
        return true;
    }

    public bool ResolveRecoveryUnknown(
        long expectedFence,
        bool retry,
        DateTime occurredAtUtc)
    {
        DateTime occurredAt =
            FairReturnSupplyPolicy.RequireUtc(
                occurredAtUtc,
                nameof(occurredAtUtc));
        if (StatusId !=
                (int)FairReturnOrchestrationEffectStatus.Unknown ||
            ProcessingFence != expectedFence)
        {
            return false;
        }

        StatusId = retry
            ? (int)FairReturnOrchestrationEffectStatus.Pending
            : (int)FairReturnOrchestrationEffectStatus.DeadLettered;
        NextAttemptAt = occurredAt;
        LastFailureCode = retry
            ? null
            : "RECOVERY_OPERATOR_DEAD_LETTER";
        DeadLetteredAt = retry ? null : occurredAt;
        UpdatedAt = occurredAt;
        ConcurrencyStamp = Guid.CreateVersion7();
        return true;
    }

    private bool Owns(long processingFence) =>
        StatusId ==
            (int)FairReturnOrchestrationEffectStatus
                .Processing
        && ProcessingFence == processingFence;

    private void ClearLease()
    {
        LeaseOwner = null;
        LeaseExpiresAt = null;
    }
}
