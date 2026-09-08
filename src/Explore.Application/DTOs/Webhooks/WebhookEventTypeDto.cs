namespace Explore.Application.DTOs.Webhooks;

public sealed record WebhookEventTypeDto
{
    public Guid? Id { get; init; }

    public required string Name { get; init; }

    public required string GroupName { get; init; }

    public required string Description { get; init; }

    public int SchemaVersion { get; init; }

    public bool IsPublic { get; init; }

    public bool IsEnabled { get; init; }

    public int PayloadRetentionDays { get; init; }

    public required string SchemaJson { get; init; }

    public required string ExamplePayloadJson { get; init; }

    public IReadOnlyList<WebhookEventDataFieldDto> DataFields { get; init; } = [];
}
