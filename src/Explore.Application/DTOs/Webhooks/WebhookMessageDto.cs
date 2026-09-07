namespace Explore.Application.DTOs.Webhooks;

public sealed record WebhookMessageDto
{
    public Guid Id { get; init; }

    public Guid TenantId { get; init; }

    public int OwnerKindId { get; init; }

    public Guid OwnerId { get; init; }

    public required string EventType { get; init; }

    public required string EventId { get; init; }

    public required string AggregateKind { get; init; }

    public Guid AggregateId { get; init; }

    public Guid? ConsumerId { get; init; }

    public string? ConsumerName { get; init; }

    public required string PayloadHash { get; init; }

    public DateTime PayloadRetentionUntil { get; init; }

    public DateTime? PayloadClearedAt { get; init; }

    public DateTime CreatedAt { get; init; }

    public DateTime? UpdatedAt { get; init; }
}
