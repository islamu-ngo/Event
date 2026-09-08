using System.ComponentModel.DataAnnotations.Schema;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class WebhookDeliveryAttempt : ITenantEntity, IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public Guid MessageId { get; set; }
    public WebhookMessage? Message { get; set; }
    public Guid EndpointId { get; set; }
    public WebhookEndpoint? Endpoint { get; set; }

    public int AttemptNumber { get; set; }
    public int OutcomeId { get; set; }
    public WebhookDeliveryAttemptOutcomeLookup OutcomeLookup { get; set; } = null!;
    [NotMapped]
    public WebhookDeliveryAttemptOutcome Outcome
    {
        get => (WebhookDeliveryAttemptOutcome)OutcomeId;
        set => OutcomeId = (int)value;
    }
    public DateTime ScheduledAt { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public Guid? ProcessingLeaseToken { get; set; }
    public long ProcessingFence { get; set; }
    public DateTime? ProcessingStartedAt { get; set; }
    public DateTime? ProcessingLeaseExpiresAt { get; set; }
    public int? HttpStatusCode { get; set; }
    public string? FailureCategory { get; set; }
    public int? DurationMs { get; set; }
    public DateTime? NextRetryAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
