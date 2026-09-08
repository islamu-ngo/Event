namespace Explore.Domain;

public sealed class WebhookBulkReplayStatusLookup
{
    public int Id { get; set; }
    public required string MasterCode { get; set; }
    public required string FullName { get; set; }
    public string? Description { get; set; }
}

public enum WebhookBulkReplayStatus
{
    Queued = 1,
    Executing = 2,
    Completed = 3,
    Cancelled = 4,
    Failed = 5
}
