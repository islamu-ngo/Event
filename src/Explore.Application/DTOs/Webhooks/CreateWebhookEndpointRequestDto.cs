namespace Explore.Application.DTOs.Webhooks;

public sealed record CreateWebhookEndpointRequestDto
{
    public Guid ConsumerId { get; init; }

    public required string Url { get; init; }

    public string? Description { get; init; }

    public required string SecretRef { get; init; }

    public IReadOnlyList<Guid> EventTypeIds { get; init; } = [];

    public int? MaxAttempts { get; init; }

    public int? TimeoutSeconds { get; init; }

    public int? RateLimitPerMinute { get; init; }
}
