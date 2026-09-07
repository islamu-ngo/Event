namespace Explore.Application.DTOs.Webhooks;

public sealed record IncomingWebhookEffectStatusDto
{
    public Guid EffectOutboxId { get; init; }
    public Guid TenantId { get; init; }
    public Guid IncomingWebhookMessageId { get; init; }
    public string EffectKind { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public int ProcessingGeneration { get; init; }
    public long ProcessingFence { get; init; }
    public int AttemptCount { get; init; }
    public DateTime? NextAttemptAt { get; init; }
    public DateTime? LeaseExpiresAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public DateTime? DeadLetteredAt { get; init; }
    public string? FailureCategory { get; init; }
    public string? SafeDetail { get; init; }
}
