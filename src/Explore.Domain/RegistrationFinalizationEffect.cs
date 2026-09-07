using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class RegistrationFinalizationEffect : ITenantEntity, IAuditableEntity
{
    public const int MaxLeaseOwnerLength = 200;

    private RegistrationFinalizationEffect()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Guid EventId { get; private set; }
    public Guid RegistrationOrderId { get; private set; }
    public OutboxMessageStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public long ProcessingFence { get; private set; }
    public string? ProcessingLeaseOwner { get; private set; }
    public Guid? ProcessingLeaseToken { get; private set; }
    public DateTime? ProcessingLeaseExpiresAt { get; private set; }
    public DateTime? NextAttemptAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static RegistrationFinalizationEffect Create(RegistrationOrder order, DateTime createdAt)
    {
        ArgumentNullException.ThrowIfNull(order);
        EnsureUtc(createdAt, nameof(createdAt));
        return new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = order.TenantId,
            EventId = order.EventId,
            RegistrationOrderId = order.Id,
            Status = OutboxMessageStatus.Pending,
            CreatedAt = createdAt
        };
    }

    public void Request(DateTime requestedAt)
    {
        EnsureUtc(requestedAt, nameof(requestedAt));
        if (Status == OutboxMessageStatus.Pending && (NextAttemptAt is null || NextAttemptAt <= requestedAt))
        {
            return;
        }

        Status = OutboxMessageStatus.Pending;
        NextAttemptAt = requestedAt;
        CompletedAt = null;
        ClearLease();
        UpdatedAt = requestedAt;
    }

    public void Claim(string leaseOwner, Guid leaseToken, DateTime leaseExpiresAt, DateTime claimedAt)
    {
        EnsureUtc(claimedAt, nameof(claimedAt));
        EnsureUtc(leaseExpiresAt, nameof(leaseExpiresAt));
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseOwner);
        string owner = leaseOwner.Trim();
        if (owner.Length > MaxLeaseOwnerLength || leaseToken == Guid.Empty || leaseExpiresAt <= claimedAt ||
            Status is not (OutboxMessageStatus.Pending or OutboxMessageStatus.Failed) ||
            (NextAttemptAt.HasValue && NextAttemptAt > claimedAt))
        {
            throw new InvalidOperationException("Registration finalization effect is not claimable.");
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
            throw new InvalidOperationException("Only an expired finalization claim can be recovered.");
        }

        Status = OutboxMessageStatus.Failed;
        NextAttemptAt = recoveredAt;
        ClearLease();
        UpdatedAt = recoveredAt;
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
}
