namespace Explore.Domain;

public sealed class EmailDispatchProcessorState
{
    public Guid Id { get; set; }
    public required string ProcessorCode { get; set; }
    public long DeliveryPolicyRevision { get; set; }
    public long? OptionalSuppressedThroughRevision { get; set; }
    public DateTime? OptionalSuppressedThroughUtc { get; set; }
    public bool IsPaused { get; set; }
    public string? PauseReason { get; set; }
    public DateTime? PausedAt { get; set; }
    public Guid? PausedBy { get; set; }
    public int? GlobalSmtpRateLimitPerMinuteOverride { get; set; }
    public bool OptionalRemindersDeferred { get; set; }
    public int? SmtpAvailableTokens { get; set; }
    public DateTime? SmtpRefillAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
