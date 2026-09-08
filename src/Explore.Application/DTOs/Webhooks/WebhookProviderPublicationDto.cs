namespace Explore.Application.DTOs.Webhooks;

public sealed record WebhookProviderPublicationDto
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid WebhookMessageId { get; init; }
    public Guid WebhookConsumerId { get; init; }
    public Guid WebhookDeliveryPlanSnapshotId { get; init; }
    public int ProviderKindId { get; init; }
    public required string ProviderKindCode { get; init; }
    public required string ProviderKindName { get; init; }
    public int ModeSnapshotId { get; init; }
    public required string ModeSnapshotCode { get; init; }
    public required string ModeSnapshotName { get; init; }
    public int StatusId { get; init; }
    public required string StatusCode { get; init; }
    public required string StatusName { get; init; }
    public required string ProviderVersion { get; init; }
    public required string ProviderEventId { get; init; }
    public required string RequestHash { get; init; }
    public required string ProviderEnvironment { get; init; }
    public string? ProviderApplicationId { get; init; }
    public string? ExternalProviderMessageId { get; init; }
    public int AutomaticPublicationAttemptCount { get; init; }
    public int AutomaticReconciliationAttemptCount { get; init; }
    public DateTime? LastAutomaticReconciliationAt { get; init; }
    public DateTime? NextActionAt { get; init; }
    public string? FailureCategory { get; init; }
    public string? SafeDetail { get; init; }
    public long PublicationFence { get; init; }
    public long ConcurrencyVersion { get; init; }
    public int EventContractVersion { get; init; }
    public required string ProviderConfigurationVersion { get; init; }
    public required string RetentionPolicyVersion { get; init; }
    public DateTime PayloadRetentionUntil { get; init; }
    public DateTime PublicationRetentionUntil { get; init; }
    public DateTime IdempotencyValidUntil { get; init; }
    public DateTime PreparedAt { get; init; }
    public DateTime? PublishingStartedAt { get; init; }
    public DateTime? ProviderQueuedAt { get; init; }
    public DateTime? PublicationUnknownAt { get; init; }
    public DateTime? DeadLetteredAt { get; init; }
    public DateTime? ManualReconciliationAt { get; init; }
    public DateTime? AbandonedAt { get; init; }
    public DateTime? ProcessingLeaseExpiresAt { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public IReadOnlyList<WebhookProviderPublicationAttemptDto> Attempts { get; init; } = [];
}
