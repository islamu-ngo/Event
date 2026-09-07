namespace Explore.Domain;

public sealed class WebhookDeliveryAttemptOutcomeLookup
{
    public int Id { get; set; }
    public required string MasterCode { get; set; }
    public required string FullName { get; set; }
    public string? Description { get; set; }
}

public enum WebhookDeliveryAttemptOutcome
{
    Scheduled = 1,
    Sending = 2,
    Succeeded = 3,
    Failed = 4,
    Abandoned = 5
}
