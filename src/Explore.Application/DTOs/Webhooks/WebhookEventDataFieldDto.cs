namespace Explore.Application.DTOs.Webhooks;

public sealed record WebhookEventDataFieldDto
{
    public required string Name { get; init; }

    public required string JsonType { get; init; }

    public required string Description { get; init; }

    public required string ExampleJson { get; init; }

    public bool Required { get; init; }
}
