using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class RegistrationProviderSubmissionWriteEffect : ITenantEntity, IAuditableEntity
{
    public const int MaxLeaseOwnerLength = 200;
    public const int MaxFailureCodeLength = 120;

    private RegistrationProviderSubmissionWriteEffect() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Guid EventId { get; private set; }
    public Guid RegistrationOrderId { get; private set; }
    public Guid RegistrationAttemptId { get; private set; }
    public Guid RegistrationSubmissionId { get; private set; }
    public Guid RegistrationProviderBindingId { get; private set; }
    public OutboxMessageStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public long ProcessingFence { get; private set; }
    public string? ProcessingLeaseOwner { get; private set; }
    public Guid? ProcessingLeaseToken { get; private set; }
    public DateTime? ProcessingLeaseExpiresAt { get; private set; }
    public DateTime? NextAttemptAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public DateTime? DeadLetteredAt { get; private set; }
    public DateTime? ParkedAt { get; private set; }
    public string? FailureCode { get; private set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static RegistrationProviderSubmissionWriteEffect Create(
        RegistrationAttempt attempt,
        RegistrationSubmission submission,
        DateTime createdAt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(submission);
        EnsureUtc(createdAt, nameof(createdAt));
        if (attempt.RegistrationProviderBindingId is not { } bindingId || bindingId == Guid.Empty ||
            attempt.TenantId != submission.TenantId || attempt.EventId != submission.EventId ||
            attempt.Id != submission.RegistrationAttemptId)
        {
            throw new ArgumentException("Provider submission write effect must match a provider-bound accepted submission.");
        }

        return new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = submission.TenantId,
            EventId = submission.EventId,
            RegistrationOrderId = submission.RegistrationOrderId,
            RegistrationAttemptId = attempt.Id,
            RegistrationSubmissionId = submission.Id,
            RegistrationProviderBindingId = bindingId,
            Status = OutboxMessageStatus.Pending,
            CreatedAt = createdAt
        };
    }

    public void Claim(string leaseOwner, Guid leaseToken, DateTime leaseExpiresAt, DateTime claimedAt)
    {
        EnsureUtc(claimedAt, nameof(claimedAt));
        EnsureUtc(leaseExpiresAt, nameof(leaseExpiresAt));
        string owner = leaseOwner?.Trim() ?? string.Empty;
        if (owner.Length is 0 or > MaxLeaseOwnerLength || leaseToken == Guid.Empty || leaseExpiresAt <= claimedAt ||
            Status is not (OutboxMessageStatus.Pending or OutboxMessageStatus.Failed) ||
            (NextAttemptAt.HasValue && NextAttemptAt > claimedAt))
        {
            throw new InvalidOperationException("Provider submission write effect is not claimable.");
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
            throw new InvalidOperationException("Only an expired provider write claim can be recovered.");
        }

        Status = OutboxMessageStatus.Failed;
        NextAttemptAt = recoveredAt;
        ClearLease();
        UpdatedAt = recoveredAt;
    }

    public void Complete(Guid leaseToken, long processingFence, DateTime completedAt)
    {
        EnsureActiveClaim(leaseToken, processingFence, completedAt);
        Status = OutboxMessageStatus.Completed;
        CompletedAt = completedAt;
        FailureCode = null;
        NextAttemptAt = null;
        ClearLease();
        UpdatedAt = completedAt;
    }

    public void ScheduleRetry(Guid leaseToken, long processingFence, string failureCode, DateTime nextAttemptAt, DateTime failedAt)
    {
        EnsureActiveClaim(leaseToken, processingFence, failedAt);
        EnsureUtc(nextAttemptAt, nameof(nextAttemptAt));
        if (nextAttemptAt <= failedAt) throw new ArgumentOutOfRangeException(nameof(nextAttemptAt));
        Status = OutboxMessageStatus.Failed;
        FailureCode = NormalizeFailureCode(failureCode);
        NextAttemptAt = nextAttemptAt;
        ClearLease();
        UpdatedAt = failedAt;
    }

    public void DeadLetter(Guid leaseToken, long processingFence, string failureCode, DateTime deadLetteredAt)
    {
        EnsureActiveClaim(leaseToken, processingFence, deadLetteredAt);
        Status = OutboxMessageStatus.DeadLettered;
        FailureCode = NormalizeFailureCode(failureCode);
        DeadLetteredAt = deadLetteredAt;
        NextAttemptAt = null;
        ClearLease();
        UpdatedAt = deadLetteredAt;
    }

    public void ParkAmbiguous(Guid leaseToken, long processingFence, string failureCode, DateTime parkedAt)
    {
        EnsureActiveClaim(leaseToken, processingFence, parkedAt);
        Status = OutboxMessageStatus.DeadLettered;
        FailureCode = NormalizeFailureCode(failureCode);
        ParkedAt = parkedAt;
        NextAttemptAt = null;
        ClearLease();
        UpdatedAt = parkedAt;
    }

    private void EnsureActiveClaim(Guid leaseToken, long processingFence, DateTime observedAt)
    {
        EnsureUtc(observedAt, nameof(observedAt));
        if (Status != OutboxMessageStatus.Processing || ProcessingLeaseToken != leaseToken ||
            ProcessingFence != processingFence || ProcessingLeaseExpiresAt is null || ProcessingLeaseExpiresAt <= observedAt)
        {
            throw new InvalidOperationException("Provider submission write effect claim is no longer active.");
        }
    }

    private void ClearLease()
    {
        ProcessingLeaseOwner = null;
        ProcessingLeaseToken = null;
        ProcessingLeaseExpiresAt = null;
    }

    private static void EnsureUtc(DateTime value, string parameterName)
    {
        if (value == default || value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Timestamp must be a non-default UTC value.", parameterName);
        }
    }

    private static string NormalizeFailureCode(string value)
    {
        string normalized = value?.Trim() ?? string.Empty;
        return normalized.Length is > 0 and <= MaxFailureCodeLength && !normalized.Any(char.IsWhiteSpace)
            ? normalized
            : throw new ArgumentException("Failure code must be non-blank, compact, and bounded.", nameof(value));
    }
}
