namespace Explore.Domain;

public sealed class PrivacyErasureProviderWork
{
    private const int MaxFailureCodeLength = 100;
    private const int MaxLeaseOwnerLength = 100;
    private const int MaxProtectedLocatorLength = 8192;

    private PrivacyErasureProviderWork()
    {
    }

    public Guid Id { get; private set; }
    public Guid IntentId { get; private set; }
    public PrivacyErasureSubjectKind SubjectKind { get; private set; }
    public Guid SubjectId { get; private set; }
    public PrivacyErasureProviderKind ProviderKind { get; private set; }
    public PrivacyErasureProviderAction Action { get; private set; }
    public Guid? TenantId { get; private set; }
    public Guid? TargetId { get; private set; }
    public PrivacyErasureProviderLocatorKind LocatorKind { get; private set; }
    public string? ProtectedLocator { get; private set; }
    public int LocatorProtectionVersion { get; private set; }
    public DateTime LocatorExpiresAtUtc { get; private set; }
    public PrivacyErasureProviderWorkStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTime? NextAttemptAtUtc { get; private set; }
    public string? LeaseOwner { get; private set; }
    public Guid? LeaseToken { get; private set; }
    public long LeaseFence { get; private set; }
    public DateTime? LeaseExpiresAtUtc { get; private set; }
    public string? LastFailureCode { get; private set; }
    public DateTime? UnknownAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public DateTime? DeadLetteredAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static PrivacyErasureProviderWork Create(
        Guid id,
        PrivacyErasureIntent intent,
        PrivacyErasureProviderKind providerKind,
        PrivacyErasureProviderAction action,
        Guid? tenantId,
        Guid? targetId,
        PrivacyErasureProviderLocatorKind locatorKind,
        string protectedLocator,
        int locatorProtectionVersion,
        DateTime locatorExpiresAtUtc,
        DateTime createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(intent);
        RequireUuidV7(id, nameof(id));
        RequireUtc(createdAtUtc, nameof(createdAtUtc));
        if (!Enum.IsDefined(providerKind))
        {
            throw new ArgumentOutOfRangeException(nameof(providerKind));
        }

        if (!Enum.IsDefined(action))
        {
            throw new ArgumentOutOfRangeException(nameof(action));
        }

        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id must be null or non-empty.", nameof(tenantId));
        }

        if (targetId == Guid.Empty)
        {
            throw new ArgumentException("Target id must be null or non-empty.", nameof(targetId));
        }

        if (!Enum.IsDefined(locatorKind))
        {
            throw new ArgumentOutOfRangeException(nameof(locatorKind));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(protectedLocator);
        if (protectedLocator.Length > MaxProtectedLocatorLength)
        {
            throw new ArgumentOutOfRangeException(nameof(protectedLocator));
        }

        if (locatorProtectionVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(locatorProtectionVersion));
        }

        RequireUtc(locatorExpiresAtUtc, nameof(locatorExpiresAtUtc));
        if (locatorExpiresAtUtc <= createdAtUtc)
        {
            throw new ArgumentException("Locator expiry must be after work creation.", nameof(locatorExpiresAtUtc));
        }

        return new PrivacyErasureProviderWork
        {
            Id = id,
            IntentId = intent.IntentId,
            SubjectKind = intent.SubjectKind,
            SubjectId = intent.SubjectId,
            ProviderKind = providerKind,
            Action = action,
            TenantId = tenantId,
            TargetId = targetId,
            LocatorKind = locatorKind,
            ProtectedLocator = protectedLocator,
            LocatorProtectionVersion = locatorProtectionVersion,
            LocatorExpiresAtUtc = locatorExpiresAtUtc,
            Status = PrivacyErasureProviderWorkStatus.Pending,
            NextAttemptAtUtc = createdAtUtc,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc
        };
    }

    public PrivacyErasureProviderClaim Claim(
        string leaseOwner,
        Guid leaseToken,
        DateTime claimedAtUtc,
        DateTime leaseExpiresAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseOwner);
        if (leaseOwner.Length > MaxLeaseOwnerLength)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseOwner));
        }

        RequireUuidV7(leaseToken, nameof(leaseToken));
        RequireUtc(claimedAtUtc, nameof(claimedAtUtc));
        RequireUtc(leaseExpiresAtUtc, nameof(leaseExpiresAtUtc));
        if (leaseExpiresAtUtc <= claimedAtUtc)
        {
            throw new ArgumentException("Lease expiry must follow claim time.", nameof(leaseExpiresAtUtc));
        }

        bool reclaimable = Status == PrivacyErasureProviderWorkStatus.Processing
            && LeaseExpiresAtUtc <= claimedAtUtc;
        if (Status is not (PrivacyErasureProviderWorkStatus.Pending or PrivacyErasureProviderWorkStatus.RetryScheduled)
            && !reclaimable)
        {
            throw new InvalidOperationException("Provider work is not claimable.");
        }

        if (NextAttemptAtUtc > claimedAtUtc)
        {
            throw new InvalidOperationException("Provider work is not due yet.");
        }

        LeaseFence = checked(LeaseFence + 1);
        LeaseOwner = leaseOwner;
        LeaseToken = leaseToken;
        LeaseExpiresAtUtc = leaseExpiresAtUtc;
        AttemptCount = checked(AttemptCount + 1);
        Status = PrivacyErasureProviderWorkStatus.Processing;
        UpdatedAtUtc = claimedAtUtc;
        return new PrivacyErasureProviderClaim(LeaseFence, leaseToken, leaseExpiresAtUtc);
    }

    public void MarkSucceeded(long fenceToken, Guid leaseToken, DateTime completedAtUtc)
    {
        EnsureActiveClaim(fenceToken, leaseToken, completedAtUtc);
        Status = PrivacyErasureProviderWorkStatus.Completed;
        CompletedAtUtc = completedAtUtc;
        UpdatedAtUtc = completedAtUtc;
        ProtectedLocator = null;
        ClearLease();
    }

    public void MarkUnknown(long fenceToken, Guid leaseToken, DateTime unknownAtUtc, string failureCode)
    {
        EnsureActiveClaim(fenceToken, leaseToken, unknownAtUtc);
        LastFailureCode = NormalizeFailureCode(failureCode);
        Status = PrivacyErasureProviderWorkStatus.Unknown;
        UnknownAtUtc = unknownAtUtc;
        UpdatedAtUtc = unknownAtUtc;
        ClearLease();
    }

    public void ScheduleRetry(
        long fenceToken,
        Guid leaseToken,
        DateTime failedAtUtc,
        DateTime nextAttemptAtUtc,
        string failureCode)
    {
        EnsureActiveClaim(fenceToken, leaseToken, failedAtUtc);
        RequireUtc(nextAttemptAtUtc, nameof(nextAttemptAtUtc));
        if (nextAttemptAtUtc <= failedAtUtc)
        {
            throw new ArgumentException("Retry time must follow failure time.", nameof(nextAttemptAtUtc));
        }

        LastFailureCode = NormalizeFailureCode(failureCode);
        Status = PrivacyErasureProviderWorkStatus.RetryScheduled;
        NextAttemptAtUtc = nextAttemptAtUtc;
        UpdatedAtUtc = failedAtUtc;
        ClearLease();
    }

    public void DeadLetter(long fenceToken, Guid leaseToken, DateTime failedAtUtc, string failureCode)
    {
        EnsureActiveClaim(fenceToken, leaseToken, failedAtUtc);
        LastFailureCode = NormalizeFailureCode(failureCode);
        Status = PrivacyErasureProviderWorkStatus.DeadLettered;
        DeadLetteredAtUtc = failedAtUtc;
        UpdatedAtUtc = failedAtUtc;
        ClearLease();
    }

    public void ExpireLocator(DateTime expiredAtUtc)
    {
        RequireUtc(expiredAtUtc, nameof(expiredAtUtc));
        if (expiredAtUtc < LocatorExpiresAtUtc)
        {
            throw new InvalidOperationException("The provider locator has not expired.");
        }

        if (ProtectedLocator is null)
        {
            return;
        }

        ProtectedLocator = null;
        LastFailureCode = "locator_expired";
        Status = PrivacyErasureProviderWorkStatus.DeadLettered;
        DeadLetteredAtUtc ??= expiredAtUtc;
        UpdatedAtUtc = expiredAtUtc;
        ClearLease();
    }

    public void Reconcile(PrivacyErasureProviderReconciliation outcome, DateTime reconciledAtUtc)
    {
        RequireUtc(reconciledAtUtc, nameof(reconciledAtUtc));
        if (Status != PrivacyErasureProviderWorkStatus.Unknown)
        {
            throw new InvalidOperationException("Only unknown provider work can be reconciled.");
        }

        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome));
        }

        if (outcome == PrivacyErasureProviderReconciliation.Completed)
        {
            Status = PrivacyErasureProviderWorkStatus.Completed;
            CompletedAtUtc = reconciledAtUtc;
            ProtectedLocator = null;
        }
        else
        {
            Status = PrivacyErasureProviderWorkStatus.RetryScheduled;
            NextAttemptAtUtc = reconciledAtUtc;
            UnknownAtUtc = null;
        }

        UpdatedAtUtc = reconciledAtUtc;
    }

    private void EnsureActiveClaim(long fenceToken, Guid leaseToken, DateTime nowUtc)
    {
        RequireUtc(nowUtc, nameof(nowUtc));
        if (Status != PrivacyErasureProviderWorkStatus.Processing
            || fenceToken <= 0
            || fenceToken != LeaseFence
            || leaseToken == Guid.Empty
            || leaseToken != LeaseToken
            || LeaseExpiresAtUtc <= nowUtc)
        {
            throw new InvalidOperationException("The provider-work claim is stale or invalid.");
        }
    }

    private void ClearLease()
    {
        LeaseOwner = null;
        LeaseToken = null;
        LeaseExpiresAtUtc = null;
    }

    private static string NormalizeFailureCode(string failureCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        string normalized = failureCode.Trim().ToLowerInvariant();
        if (normalized.Length > MaxFailureCodeLength
            || normalized.Any(character => !(char.IsAsciiLetterOrDigit(character) || character == '_')))
        {
            throw new ArgumentException("Failure code must be a bounded lowercase code.", nameof(failureCode));
        }

        return normalized;
    }

    private static void RequireUuidV7(Guid value, string parameterName)
    {
        if (value == Guid.Empty || value.Version != 7 || value.Variant is < 8 or > 11)
        {
            throw new ArgumentException("Identifier must be an RFC 4122 UUIDv7 value.", parameterName);
        }
    }

    private static void RequireUtc(DateTime value, string parameterName)
    {
        if (value == default || value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Timestamp must be a non-default UTC value.", parameterName);
        }
    }
}

public sealed record PrivacyErasureProviderClaim(
    long FenceToken,
    Guid LeaseToken,
    DateTime LeaseExpiresAtUtc);
