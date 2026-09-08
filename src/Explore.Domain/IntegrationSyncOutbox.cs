using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class IntegrationSyncOutbox : ITenantEntity, IAuditableEntity, ISoftDeletable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    public IntegrationKind Kind { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public Guid SourceId { get; set; }

    public Guid? EventId { get; set; }
    public Event? Event { get; set; }

    public Guid? RegistrationOrderId { get; set; }
    public RegistrationOrder? RegistrationOrder { get; set; }

    public Guid? UserId { get; set; }
    public User? User { get; set; }

    public string SubscriberEmail { get; set; } = string.Empty;
    public string? SubscriberName { get; set; }
    public string SubscriberPayloadJson { get; set; } = string.Empty;
    public int ListmonkListId { get; set; }
    public bool PreconfirmSubscriptions { get; set; } = true;

    public IntegrationSyncStatus Status { get; set; } = IntegrationSyncStatus.Pending;
    public int AttemptCount { get; set; }
    public int MaxAttempts { get; set; } = 5;
    public DateTime? NextAttemptAt { get; set; }
    public DateTime? ProcessingStartedAt { get; set; }
    public Guid? ProcessingLeaseToken { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? DeadLetteredAt { get; set; }
    public DateTime? LastFailureAt { get; set; }
    public string? LastError { get; set; }
    public string? CorrelationId { get; set; }

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
}

public enum IntegrationKind
{
    Listmonk = 1
}

public enum IntegrationSyncStatus
{
    Pending = 1,
    Processing = 2,
    Completed = 3,
    RetryScheduled = 4,
    DeadLettered = 5,
    Skipped = 6
}
