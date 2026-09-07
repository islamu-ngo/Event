using Explore.Domain.Interfaces;

namespace Explore.Domain;

public enum RegistrationProviderSubscriptionOperation { Renewal = 1, Sweep = 2 }

public sealed class RegistrationProviderSubscriptionState : ITenantEntity, IAuditableEntity, ISoftDeletable, IConcurrencyAware
{
    public const string RenewalRejectedFailureCategory = "renewal_rejected";
    public const string RenewalInDoubtFailureCategory = "renewal_in_doubt";
    public const int MaxProviderEventTypeLength = 120;
    public const int MaxWatchIdLength = 200;
    public const int MaxResponseCheckpointLength = 1024;
    public const int MaxFailureCategoryLength = 80;

    private RegistrationProviderSubscriptionState() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Guid RegistrationProviderBindingId { get; private set; }
    public RegistrationProviderBinding? Binding { get; private set; }
    public string ProviderEventType { get; private set; } = string.Empty;
    public string WatchId { get; private set; } = string.Empty;
    public DateTime WatchExpiresAt { get; private set; }
    public string? ResponseCheckpoint { get; private set; }
    public DateTime? LastNotificationAt { get; private set; }
    public DateTime? PendingNotificationAt { get; private set; }
    public DateTime? LastSweepSuccessAt { get; private set; }
    public DateTime? LastRenewalAttemptAt { get; private set; }
    public DateTime? LastRenewalSuccessAt { get; private set; }
    public DateTime? NextRenewalAttemptAt { get; private set; }
    public DateTime? NextSweepAttemptAt { get; private set; }
    public string? FailureCategory { get; private set; }
    public int RenewalFailureCount { get; private set; }
    public int SweepFailureCount { get; private set; }
    public long ProcessingGeneration { get; private set; }
    public Guid? LeaseToken { get; private set; }
    public DateTime? LeaseExpiresAt { get; private set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public Guid ConcurrencyStamp { get; set; }

    public static RegistrationProviderSubscriptionState Create(
        Guid tenantId,
        Guid registrationProviderBindingId,
        string providerEventType,
        string watchId,
        DateTime watchExpiresAt,
        string? responseCheckpoint,
        DateTime createdAt)
    {
        if (tenantId == Guid.Empty || registrationProviderBindingId == Guid.Empty)
            throw new ArgumentException("Subscription state identities must be valid.");

        EnsureUtc(watchExpiresAt, nameof(watchExpiresAt));
        EnsureUtc(createdAt, nameof(createdAt));

        return new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            RegistrationProviderBindingId = registrationProviderBindingId,
            ProviderEventType = NormalizeCompact(providerEventType, nameof(providerEventType), MaxProviderEventTypeLength),
            WatchId = NormalizeCompact(watchId, nameof(watchId), MaxWatchIdLength),
            WatchExpiresAt = watchExpiresAt,
            ResponseCheckpoint = NormalizeOptional(responseCheckpoint, nameof(responseCheckpoint), MaxResponseCheckpointLength),
            CreatedAt = createdAt,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
    }

    public void Claim(Guid leaseToken, DateTime leaseExpiresAt, DateTime claimedAt)
    {
        EnsureUtc(leaseExpiresAt, nameof(leaseExpiresAt));
        EnsureUtc(claimedAt, nameof(claimedAt));
        if (leaseToken == Guid.Empty || leaseExpiresAt <= claimedAt || (LeaseExpiresAt.HasValue && LeaseExpiresAt > claimedAt))
            throw new InvalidOperationException("Subscription state is already claimed or lease input is invalid.");

        LeaseToken = leaseToken;
        LeaseExpiresAt = leaseExpiresAt;
        ProcessingGeneration = checked(ProcessingGeneration + 1);
        Touch(claimedAt);
    }

    public void RecordNotification(Guid leaseToken, long generation, DateTime notifiedAt)
    {
        EnsureClaim(leaseToken, generation, notifiedAt);
        ReceiveNotification(notifiedAt);
    }

    public void ReceiveNotification(DateTime notifiedAt)
    {
        EnsureUtc(notifiedAt, nameof(notifiedAt));
        LastNotificationAt = notifiedAt;
        PendingNotificationAt = notifiedAt;
        NextSweepAttemptAt = null;
        Touch(notifiedAt);
    }

    public void SettleCheckpoint(Guid leaseToken, long generation, string responseCheckpoint, DateTime settledAt) =>
        SettleCheckpoint(leaseToken, generation, responseCheckpoint, null, settledAt);

    public void SettleCheckpoint(Guid leaseToken, long generation, string responseCheckpoint, DateTime? nextSweepAttemptAt, DateTime settledAt)
    {
        if (nextSweepAttemptAt is { } nextSweep) EnsureUtc(nextSweep, nameof(nextSweepAttemptAt));
        EnsureClaim(leaseToken, generation, settledAt);
        ResponseCheckpoint = NormalizeOptional(responseCheckpoint, nameof(responseCheckpoint), MaxResponseCheckpointLength)
            ?? throw new ArgumentException("Response checkpoint must be non-blank.", nameof(responseCheckpoint));
        FailureCategory = null;
        SweepFailureCount = 0;
        PendingNotificationAt = null;
        LastSweepSuccessAt = settledAt;
        NextSweepAttemptAt = nextSweepAttemptAt;
        ClearLease();
        Touch(settledAt);
    }

    public void MarkRenewalAttempt(Guid leaseToken, long generation, DateTime attemptedAt)
    {
        EnsureClaim(leaseToken, generation, attemptedAt);
        LastRenewalAttemptAt = attemptedAt;
        Touch(attemptedAt);
    }

    public void BeginRenewalHandoff(Guid leaseToken, long generation, DateTime startedAt)
    {
        EnsureClaim(leaseToken, generation, startedAt);
        FailureCategory = RenewalInDoubtFailureCategory;
        NextRenewalAttemptAt = null;
        Touch(startedAt);
    }

    public void ParkRenewalInDoubt(Guid leaseToken, long generation, DateTime parkedAt)
    {
        EnsureClaim(leaseToken, generation, parkedAt);
        FailureCategory = RenewalInDoubtFailureCategory;
        RenewalFailureCount = checked(RenewalFailureCount + 1);
        NextRenewalAttemptAt = null;
        ClearLease();
        Touch(parkedAt);
    }

    public void MarkRenewalSuccess(Guid leaseToken, long generation, string watchId, DateTime watchExpiresAt, DateTime renewedAt)
    {
        EnsureClaim(leaseToken, generation, renewedAt);
        EnsureUtc(watchExpiresAt, nameof(watchExpiresAt));
        if (watchExpiresAt <= renewedAt) throw new ArgumentOutOfRangeException(nameof(watchExpiresAt));

        WatchId = NormalizeCompact(watchId, nameof(watchId), MaxWatchIdLength);
        WatchExpiresAt = watchExpiresAt;
        LastRenewalAttemptAt = renewedAt;
        LastRenewalSuccessAt = renewedAt;
        FailureCategory = null;
        RenewalFailureCount = 0;
        NextRenewalAttemptAt = null;
        ClearLease();
        Touch(renewedAt);
    }

    public void RejectRenewal(Guid leaseToken, long generation, DateTime rejectedAt)
    {
        EnsureClaim(leaseToken, generation, rejectedAt);
        FailureCategory = RenewalRejectedFailureCategory;
        RenewalFailureCount = checked(RenewalFailureCount + 1);
        NextRenewalAttemptAt = null;
        ClearLease();
        Touch(rejectedAt);
    }

    public void Fail(RegistrationProviderSubscriptionOperation operation, Guid leaseToken, long generation, string failureCategory, DateTime nextAttemptAt, DateTime failedAt)
    {
        EnsureClaim(leaseToken, generation, failedAt);
        EnsureUtc(nextAttemptAt, nameof(nextAttemptAt));
        if (!Enum.IsDefined(operation)) throw new ArgumentException("Subscription operation must be valid.", nameof(operation));
        if (nextAttemptAt <= failedAt) throw new ArgumentOutOfRangeException(nameof(nextAttemptAt));
        FailureCategory = NormalizeCompact(failureCategory, nameof(failureCategory), MaxFailureCategoryLength);
        if (operation == RegistrationProviderSubscriptionOperation.Renewal)
        {
            RenewalFailureCount = checked(RenewalFailureCount + 1);
            NextRenewalAttemptAt = nextAttemptAt;
        }
        else
        {
            SweepFailureCount = checked(SweepFailureCount + 1);
            NextSweepAttemptAt = nextAttemptAt;
        }
        ClearLease();
        Touch(failedAt);
    }

    public void Fail(Guid leaseToken, long generation, string failureCategory, DateTime failedAt) =>
        Fail(RegistrationProviderSubscriptionOperation.Sweep, leaseToken, generation, failureCategory, failedAt.AddMinutes(5), failedAt);

    public void Remove(DateTime removedAt)
    {
        EnsureUtc(removedAt, nameof(removedAt));
        IsDeleted = true;
        DeletedAt = removedAt;
        ClearLease();
        Touch(removedAt);
    }

    private void EnsureClaim(Guid leaseToken, long generation, DateTime observedAt)
    {
        EnsureUtc(observedAt, nameof(observedAt));
        if (LeaseToken != leaseToken || ProcessingGeneration != generation || LeaseExpiresAt is null || LeaseExpiresAt <= observedAt)
            throw new InvalidOperationException("Subscription state claim is no longer active.");
    }

    private void Touch(DateTime updatedAt)
    {
        UpdatedAt = updatedAt;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    private void ClearLease()
    {
        LeaseToken = null;
        LeaseExpiresAt = null;
    }

    private static void EnsureUtc(DateTime value, string parameterName)
    {
        if (value == default || value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Timestamp must be a non-default UTC value.", parameterName);
    }

    private static string NormalizeCompact(string value, string parameterName, int maxLength)
    {
        string normalized = value?.Trim() ?? string.Empty;
        return normalized.Length is > 0 && normalized.Length <= maxLength && !normalized.Any(char.IsWhiteSpace) && !normalized.Any(char.IsControl)
            ? normalized
            : throw new ArgumentException($"Value must be non-blank, compact, and at most {maxLength} characters.", parameterName);
    }

    private static string? NormalizeOptional(string? value, string parameterName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string normalized = value.Trim();
        return normalized.Length <= maxLength && !normalized.Any(char.IsControl)
            ? normalized
            : throw new ArgumentException($"Value must be at most {maxLength} characters and contain no control characters.", parameterName);
    }
}
