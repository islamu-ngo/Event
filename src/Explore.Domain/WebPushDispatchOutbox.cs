using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class WebPushDispatchOutbox : ITenantEntity, IAuditableEntity, ISoftDeletable
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }
    public Guid NotificationId { get; set; }
    public int CategoryId { get; set; }
    public NotificationPreferenceCategory? Category { get; set; }
    public Guid SubscriptionId { get; set; }
    public WebPushSubscription? Subscription { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public required string PayloadJson { get; set; }
    public WebPushDispatchStatus Status { get; set; } = WebPushDispatchStatus.Pending;
    public int AttemptCount { get; set; }
    public int MaxAttempts { get; set; } = 5;
    public DateTime? NextAttemptAt { get; set; }
    public DateTime? ProcessingStartedAt { get; set; }
    public Guid? ProcessingLeaseToken { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? DeadLetteredAt { get; set; }
    public DateTime? SkippedAt { get; set; }
    public DateTime? PermanentFailedAt { get; set; }
    public string? LastFailureCategory { get; set; }
    public string? LastError { get; set; }
    public DateTime? LastFailureAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
}

public enum WebPushDispatchStatus
{
    Pending = 1,
    Processing = 2,
    Delivered = 3,
    RetryScheduled = 4,
    DeadLettered = 5,
    Skipped = 6,
    PermanentFailed = 7
}
