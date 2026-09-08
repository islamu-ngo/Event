using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class EmailDispatchReceipt : ITenantEntity, IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public Guid PublishEventId { get; set; }
    public Guid EmailDispatchOutboxId { get; set; }
    public EmailDispatchOutbox? EmailDispatchOutbox { get; set; }

    public EmailDispatchReceiptStatus Status { get; set; } = EmailDispatchReceiptStatus.Received;
    public string? ConsumerId { get; set; }
    public DateTime FirstSeenAt { get; set; }
    public DateTime? ProcessingStartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? FailedAt { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public string? ProviderMessageId { get; set; }

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

public enum EmailDispatchReceiptStatus
{
    Received = 1,
    Processing = 2,
    Completed = 3,
    Failed = 4,
    Unknown = 5,
    Skipped = 6
}
