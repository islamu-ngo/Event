namespace Explore.Application.DTOs.Webhooks;

public sealed record UpdateWebhookEndpointRequestDto
{
    public UpdateWebhookEndpointDestinationDto? Destination { get; init; }
    public UpdateWebhookEndpointSubscriptionsDto? Subscriptions { get; init; }
    public UpdateWebhookEndpointDeliveryPolicyDto? DeliveryPolicy { get; init; }
    public required UpdateWebhookEndpointGovernanceDto Governance { get; init; }
}

public sealed record UpdateWebhookEndpointDestinationDto
{
    public required string Url { get; init; }
    public string? Description { get; init; }
}

public sealed record UpdateWebhookEndpointSubscriptionsDto
{
    public IReadOnlyList<Guid> EventTypeIds { get; init; } = [];
}

public sealed record UpdateWebhookEndpointDeliveryPolicyDto
{
    public int? MaxAttempts { get; init; }
    public int? TimeoutSeconds { get; init; }
    public int? RateLimitPerMinute { get; init; }
}

public sealed record UpdateWebhookEndpointGovernanceDto
{
    public int ExpectedConfigurationVersion { get; init; }
    public int PendingWorkDecisionId { get; init; }
    public required string PendingWorkReason { get; init; }
    public bool AcknowledgeUncertainProviderPublications { get; init; }
}
