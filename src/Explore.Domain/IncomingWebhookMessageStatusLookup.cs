namespace Explore.Domain;

public sealed class IncomingWebhookMessageStatusLookup
{
    public int Id { get; set; }
    public required string MasterCode { get; set; }
    public required string FullName { get; set; }
    public string? Description { get; set; }
}

public enum IncomingWebhookMessageStatus
{
    Verified = 1,
    Processing = 2,
    RetryDue = 3,
    Processed = 4,
    Ignored = 5,
    RejectedPermanent = 6,
    DeadLettered = 7,
    PayloadConflict = 8
}
