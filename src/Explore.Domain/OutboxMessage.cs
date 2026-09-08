namespace Explore.Domain;

/// <summary>
/// Outbox entry for reliable delivery of side effects that must not run inside the transaction lambda.
/// Created atomically alongside domain writes; dispatched asynchronously by a background processor.
/// </summary>
public class OutboxMessage
{
    /// <summary>
    /// Unique identifier (UUID v7 for time-ordering).
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Type name of the source aggregate (e.g., "Event", "Registration").
    /// </summary>
    public required string AggregateType { get; set; }

    /// <summary>
    /// Identifier of the source aggregate for correlation.
    /// </summary>
    public Guid AggregateId { get; set; }

    /// <summary>
    /// Discriminator for the kind of side effect (e.g., "EventPublished", "RegistrationConfirmed").
    /// Consumers use this to route dispatch logic.
    /// </summary>
    public required string EventType { get; set; }

    /// <summary>
    /// JSON-serialized payload for the consumer. Null when the event type alone carries enough information.
    /// </summary>
    public string? Payload { get; set; }

    /// <summary>
    /// Current processing status.
    /// </summary>
    public OutboxMessageStatus Status { get; set; }

    /// <summary>
    /// When the outbox entry was created (set by EF default or caller).
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the entry was successfully dispatched. Null if pending or failed.
    /// </summary>
    public DateTime? ProcessedAt { get; set; }

    /// <summary>
    /// Number of failed dispatch attempts.
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// Error message from the last failed attempt.
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// Next retry timestamp, active processing lease, or pending dead-letter reconciliation lease.
    /// Null when no retry or reconciliation work remains.
    /// </summary>
    public DateTime? NextRetryAt { get; set; }

    /// <summary>
    /// Maximum retry attempts before dead-lettering. Processor stops retrying when RetryCount >= MaxRetries.
    /// </summary>
    public int MaxRetries { get; set; }

    /// <summary>
    /// When the entry was moved to dead-letter state after exhausting retries. Null if still active.
    /// </summary>
    public DateTime? DeadLetteredAt { get; set; }
}

/// <summary>
/// Processing status for outbox messages.
/// </summary>
public enum OutboxMessageStatus
{
    /// <summary>Awaiting dispatch by background processor.</summary>
    Pending = 1,

    /// <summary>Currently being dispatched (optimistic lock held).</summary>
    Processing = 2,

    /// <summary>Successfully dispatched to the consumer.</summary>
    Completed = 3,

    /// <summary>Failed after maximum retry attempts.</summary>
    Failed = 4,

    /// <summary>Quarantined after exhausting all retries.</summary>
    DeadLettered = 5,

    Unknown = 6
}
