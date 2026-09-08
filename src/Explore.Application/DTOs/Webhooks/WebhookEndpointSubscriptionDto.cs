namespace Explore.Application.DTOs.Webhooks;

public sealed record WebhookEndpointSubscriptionDto
{
    public Guid Id { get; init; }

    public Guid EventTypeId { get; init; }

    public required string EventTypeName { get; init; }

    public required string EventTypeGroupName { get; init; }

    public bool IsEnabled { get; init; }

    public DateTime CreatedAt { get; init; }
}
