namespace Explore.Application.DTOs.Webhooks;

public sealed record WebhookEndpointDto
{
    public Guid Id { get; init; }

    public Guid? TenantId { get; init; }

    public Guid? InstanceId { get; init; }

    public Guid OwnerId { get; init; }

    public int OwnerKindId { get; init; }

    public required string OwnerKindCode { get; init; }

    public required string OwnerKindName { get; init; }

    public Guid ConsumerId { get; init; }

    public string? ConsumerName { get; init; }

    public int ProviderModeId { get; init; }

    public required string ProviderModeCode { get; init; }

    public required string ProviderModeName { get; init; }

    public required string DestinationHost { get; init; }

    public string? Description { get; init; }

    public int StatusId { get; init; }

    public required string StatusCode { get; init; }

    public required string StatusName { get; init; }

    public int SecretVersion { get; init; }

    public int ConfigurationVersion { get; init; }

    public string? ProviderEndpointId { get; init; }

    public int MaxAttempts { get; init; }

    public int TimeoutSeconds { get; init; }

    public int? RateLimitPerMinute { get; init; }

    public DateTime? LastSuccessAt { get; init; }

    public DateTime? LastFailureAt { get; init; }

    public int ConsecutiveFailureCount { get; init; }

    public DateTime? CircuitOpenedAt { get; init; }

    public DateTime? AutoPausedAt { get; init; }

    public string? AutoPauseReason { get; init; }

    public DateTime? LastResumedAt { get; init; }

    public long DeliveryStateVersion { get; init; }

    public DateTime CreatedAt { get; init; }

    public DateTime? UpdatedAt { get; init; }

    public IReadOnlyList<WebhookEndpointSubscriptionDto> Subscriptions { get; init; } = [];
}
