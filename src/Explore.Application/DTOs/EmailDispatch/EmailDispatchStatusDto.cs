// ABOUTME: Operator-safe status DTO for Basic Dispatch Mode email outbox rows.
// ABOUTME: Exposes lifecycle and retry fields while excluding recipient, body, subject, and provider secrets.

using Explore.Domain;

namespace Explore.Application.DTOs.EmailDispatch;

public sealed record EmailDispatchStatusDto
{
    public Guid OutboxId { get; init; }
    public Guid TenantId { get; init; }
    public string SourceType { get; init; } = string.Empty;
    public Guid SourceId { get; init; }
    public EmailDispatchStatus DeliveryStatus { get; init; }
    public int AttemptCount { get; init; }
    public DateTime? NextRetryAt { get; init; }
    public string? LastFailureCategory { get; init; }
    public DateTime? LastFailureAt { get; init; }
    public DateTime? UnknownAt { get; init; }
    public DateTime? DeliveredAt { get; init; }
    public DateTime? ParkedAt { get; init; }
    public EmailDispatchParkReason? ParkReason { get; init; }
    public DateTime? ContentRedactedAt { get; init; }
    public string? CorrelationId { get; init; }
}

public sealed record EmailDispatchProcessorControlDto
{
    public string ProcessorCode { get; init; } = "smtp";
    public bool IsPaused { get; init; }
    public string? PauseReason { get; init; }
    public DateTime? PausedAt { get; init; }
    public int? GlobalSmtpRateLimitPerMinuteOverride { get; init; }
    public bool OptionalRemindersDeferred { get; init; }
    public DateTime? UpdatedAt { get; init; }
}
