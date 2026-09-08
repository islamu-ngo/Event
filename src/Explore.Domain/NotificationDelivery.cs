using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class NotificationDelivery : ITenantEntity, IAuditableEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public Guid NotificationIntentId { get; set; }
    public NotificationIntent? NotificationIntent { get; set; }

    public int ChannelId { get; set; }
    public NotificationPreferenceChannel? Channel { get; set; }

    public int DeliveryPolicyId { get; set; }
    public NotificationDeliveryPolicy? DeliveryPolicy { get; set; }

    public bool IsRequired { get; set; }
    public int PolicyVersion { get; set; }
    public string? ConsentPurpose { get; set; }
    public int? ConsentVersion { get; set; }
    public string? PreferenceCategoryCode { get; set; }
    public bool? PreferenceEnabled { get; set; }
    public RecipientAddressSource? RecipientAddressSource { get; set; }
    public required string DisclosureLevel { get; set; }
    public required string TemplateKey { get; set; }
    public int TemplateVersion { get; set; }
    public bool LinkAllowed { get; set; }

    public Guid? NotificationId { get; set; }
    public Notification? Notification { get; set; }

    public Guid? EmailDispatchOutboxId { get; set; }
    public EmailDispatchOutbox? EmailDispatchOutbox { get; set; }

    public int StatusId { get; set; }
    public NotificationDeliveryStatus? Status { get; set; }

    public string? ProviderMessageId { get; set; }
    public string? ProviderStatus { get; set; }
    public string? FailureCategory { get; set; }
    public DateTime? QueuedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
