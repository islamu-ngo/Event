namespace Explore.Domain;

public sealed class IncomingWebhookProcessingAttemptOutcomeLookup
{
    public int Id { get; set; }
    public required string MasterCode { get; set; }
    public required string FullName { get; set; }
    public string? Description { get; set; }
}

public enum IncomingWebhookProcessingAttemptOutcome
{
    Claimed = 1,
    Processed = 2,
    SettledFromReceipt = 3,
    Ignored = 4,
    RejectedPermanent = 5,
    RetryScheduled = 6,
    DeadLettered = 7,
    PayloadConflict = 8,
    LeaseExpired = 9
}
