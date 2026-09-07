namespace Explore.Application.DTOs.Webhooks;

public sealed record WebhookDeliveryAttemptDto
{
    public Guid Id { get; init; }

    public Guid TenantId { get; init; }

    public int OwnerKindId { get; init; }

    public Guid OwnerId { get; init; }

    public Guid MessageId { get; init; }

    public string? MessageEventType { get; init; }

    public Guid EndpointId { get; init; }

    public int EndpointStatusId { get; init; }

    public required string EndpointStatusCode { get; init; }

    public required string EndpointStatusName { get; init; }

    public int AttemptNumber { get; init; }

    public int OutcomeId { get; init; }

    public required string OutcomeCode { get; init; }

    public required string OutcomeName { get; init; }

    public DateTime ScheduledAt { get; init; }

    public DateTime? SentAt { get; init; }

    public DateTime? CompletedAt { get; init; }

    public int? HttpStatusCode { get; init; }

    public string? FailureCategory { get; init; }

    public int? DurationMs { get; init; }

    public DateTime? NextRetryAt { get; init; }

    public DateTime CreatedAt { get; init; }

    public DateTime? UpdatedAt { get; init; }
}
