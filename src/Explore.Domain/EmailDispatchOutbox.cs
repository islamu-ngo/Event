using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class EmailDispatchOutbox : ITenantEntity, IAuditableEntity, ISoftDeletable
{
    public static bool CanParkForOperator(EmailDispatchStatus status, EmailDispatchParkReason? parkReason) =>
        status is EmailDispatchStatus.Pending or EmailDispatchStatus.RetryScheduled or EmailDispatchStatus.DeadLettered
        || status == EmailDispatchStatus.Parked && parkReason == EmailDispatchParkReason.CapabilityUnavailable;

    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public Guid PublishEventId { get; set; } = Guid.CreateVersion7();
    public EmailDispatchKind Kind { get; set; }
    public required string SourceType { get; set; }
    public Guid SourceId { get; set; }

    public Guid NotificationIntentId { get; set; }
    public NotificationIntent? NotificationIntent { get; set; }

    public Guid? EventId { get; set; }
    public Event? Event { get; set; }

    public Guid? RegistrationOrderId { get; set; }
    public RegistrationOrder? RegistrationOrder { get; set; }

    public Guid RecipientUserId { get; set; }
    public TenantUser? RecipientTenantUser { get; set; }
    public RecipientAddressSource RecipientAddressSource { get; set; }

    public Guid? ManagedTenantProvisioningOperationId { get; set; }
    public ManagedTenantProvisioningOperation? ManagedTenantProvisioningOperation { get; set; }

    public required string RecipientEmail { get; set; }
    public required string Subject { get; set; }
    public string? PlainTextBody { get; set; }
    public string? HtmlBody { get; set; }
    public string? ReplyTo { get; set; }

    public EmailDispatchStatus Status { get; set; } = EmailDispatchStatus.Pending;
    public int AttemptCount { get; set; }
    public int MaxAttempts { get; set; } = 5;
    public DateTime? NextAttemptAt { get; set; }
    public DateTime? ProcessingStartedAt { get; set; }
    public Guid? ProcessingLeaseToken { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? DeadLetteredAt { get; set; }
    public DateTime? ParkedAt { get; set; }
    public EmailDispatchParkReason? ParkReason { get; set; }
    public DateTime? UnknownAt { get; set; }
    public DateTime? ContentRedactedAt { get; set; }

    public string? LastFailureCategory { get; set; }
    public string? LastError { get; set; }
    public DateTime? LastFailureAt { get; set; }
    public string? ProviderMessageId { get; set; }
    public string? CorrelationId { get; set; }
    public DateTime? RabbitMqLastPublishedAt { get; set; }
    public DateTime? RabbitMqLastPublishAttemptAt { get; set; }
    public int RabbitMqPublishAttemptCount { get; set; }
    public string? RabbitMqLastPublishFailureCategory { get; set; }

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
}

public enum EmailDispatchKind
{
    RegistrationConfirmation = 1,
    RegistrationApproved = 2,
    RegistrationRejected = 3,
    WaitlistPromoted = 4,
    EventReminder = 5,
    EventCancelled = 6,
    OrganizerNotification = 7,
    TenantAdministratorInvitation = 8,
    RegistrationCancelled = 9,
    RegistrationRevoked = 10,
    EventUpdated = 11,
    ReportReceipt = 12,
    ReportOutcome = 13,
    ReportNeedsMoreInformation = 14,
    ModerationAvailabilityRequired = 15
}

public enum EmailDispatchStatus
{
    Pending = 1,
    Processing = 2,
    Sent = 3,
    RetryScheduled = 4,
    DeadLettered = 5,
    Parked = 6,
    Unknown = 7,
    Skipped = 8
}

public enum RecipientAddressSource
{
    TenantUserVerifiedEmail = 1,
    ManagedTenantAdministratorInvitation = 2
}

public enum EmailDispatchParkReason
{
    CapabilityUnavailable = 1,
    Operator = 2
}
