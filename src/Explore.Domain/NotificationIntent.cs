// ABOUTME: Durable local notification intent recorded before any product email or external provider delegation runs.
// ABOUTME: Stores only safe payload references/hashes and normalized ownership metadata for tenant-audited dispatch.

using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class NotificationIntent : ITenantEntity, IAuditableEntity, ISoftDeletable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public int CategoryId { get; set; }
    public NotificationCategory? Category { get; set; }

    public int OwnershipTypeId { get; set; }
    public NotificationOwnershipType? OwnershipType { get; set; }

    public int RecipientKindId { get; set; }
    public NotificationRecipientKind? RecipientKind { get; set; }

    public int StatusId { get; set; }
    public NotificationIntentStatus? Status { get; set; }

    public required string TemplateKey { get; set; }
    public required string DeduplicationKey { get; set; }
    public string? SafePayloadReference { get; set; }
    public string? SafePayloadHash { get; set; }
    public string? CorrelationId { get; set; }

    public Guid RecipientUserId { get; set; }
    public TenantUser? RecipientTenantUser { get; set; }

    public Guid? FanoutOccurrenceId { get; set; }
    public NotificationFanoutOccurrence? FanoutOccurrence { get; set; }
    public long EmailDeliveryPolicyRevision { get; set; }

    public Guid? EventId { get; set; }
    public Event? Event { get; set; }

    public Guid? ReportId { get; set; }
    public EventReport? Report { get; set; }

    public Guid? ReportDecisionId { get; set; }
    public EventReportDecision? ReportDecision { get; set; }

    public ICollection<NotificationDelivery> Deliveries { get; set; } = [];
    public ICollection<NotificationExternalDelegation> ExternalDelegations { get; set; } = [];

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
}
