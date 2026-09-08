using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class NotificationExternalDelegation : ITenantEntity, IAuditableEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public Guid NotificationIntentId { get; set; }
    public NotificationIntent? NotificationIntent { get; set; }

    public int ProviderKindId { get; set; }
    public ExternalWorkflowProviderKindLookup? ProviderKind { get; set; }

    public int? AccountAuthorityKindId { get; set; }
    public AccountAuthorityKindLookup? AccountAuthorityKind { get; set; }

    public int StatusId { get; set; }
    public NotificationExternalDelegationStatus? Status { get; set; }

    public int RecipientKindId { get; set; }
    public NotificationRecipientKind? RecipientKind { get; set; }

    public required string TemplateKey { get; set; }
    public string? SafePayloadHash { get; set; }
    public string? ExternalProviderId { get; set; }
    public string? ExternalCorrelationId { get; set; }
    public string? ExternalDeliveryStatus { get; set; }
    public string? FailureCategory { get; set; }

    public Guid? ReportId { get; set; }
    public EventReport? Report { get; set; }

    public Guid? ReportDecisionId { get; set; }
    public EventReportDecision? ReportDecision { get; set; }

    public DateTime? RequestedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
