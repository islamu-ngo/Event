namespace Explore.Application.DTOs.Webhooks;

public sealed record WebhookMessagePayloadDto
{
    public Guid MessageId { get; init; }

    public required string ContentType { get; init; }

    public required string ContentEncoding { get; init; }

    public required string PayloadBase64 { get; init; }

    public required string PayloadHash { get; init; }

    public long PayloadByteLength { get; init; }

    public DateTime PayloadRetentionUntil { get; init; }

    public DateTime RetrievedAt { get; init; }
}
