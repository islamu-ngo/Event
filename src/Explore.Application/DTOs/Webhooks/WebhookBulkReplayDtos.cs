namespace Explore.Application.DTOs.Webhooks;

public sealed record WebhookBulkReplayFilterDto
{
    public DateTime FromUtc { get; init; }

    public DateTime ToUtc { get; init; }

    public Guid? WebhookConsumerId { get; init; }

    public Guid? WebhookEndpointId { get; init; }

    public string? EventType { get; init; }

    public int MaxItems { get; init; } = 100;
}

public sealed record WebhookBulkReplayPreviewDto
{
    public required WebhookBulkReplayFilterDto Filter { get; init; }

    public int EligibleCount { get; init; }

    public int EstimatedSelectedCount { get; init; }

    public int ExcludedCount { get; init; }

    public int ExcludedHeldCount { get; init; }

    public int ExcludedPayloadUnavailableCount { get; init; }

    public int ExcludedEndpointUnavailableCount { get; init; }

    public int ExcludedIneligibleLocalStateCount { get; init; }

    public int ExcludedProviderConflictCount { get; init; }

    public int ExcludedProviderUnknownCount { get; init; }

    public int ExcludedProviderManualReconciliationCount { get; init; }

    public int ExcludedProviderIneligibleCount { get; init; }

    public int MaximumItemsPerOperation { get; init; }

    public int MaximumReservedItemsPerTenant { get; init; }

    public DateTime PreviewedAt { get; init; }
}

public sealed record WebhookBulkReplayOperationDto
{
    public Guid Id { get; init; }

    public Guid TenantId { get; init; }

    public Guid OperationKey { get; init; }

    public int StatusId { get; init; }

    public required string StatusCode { get; init; }

    public required string StatusName { get; init; }

    public required WebhookBulkReplayFilterDto Filter { get; init; }

    public required string ReasonCode { get; init; }

    public string? CancellationReasonCode { get; init; }

    public int EstimatedEligibleCount { get; init; }

    public int EstimatedSelectedCount { get; init; }

    public int EstimatedExcludedCount { get; init; }

    public int ScheduledCount { get; init; }

    public string? FailureCode { get; init; }

    public long ConcurrencyVersion { get; init; }

    public DateTime QueuedAt { get; init; }

    public DateTime? StartedAt { get; init; }

    public DateTime? CompletedAt { get; init; }

    public DateTime? CancelledAt { get; init; }

    public DateTime? FailedAt { get; init; }
}

public sealed record ScheduleWebhookBulkReplayRequestDto
{
    public Guid OperationKey { get; init; }

    public required WebhookBulkReplayFilterDto Filter { get; init; }

    public required string ReasonCode { get; init; }
}

public sealed record CancelWebhookBulkReplayRequestDto
{
    public long ExpectedConcurrencyVersion { get; init; }

    public required string ReasonCode { get; init; }
}
