namespace Explore.Domain;

public sealed class WebhookConsumerStatusLookup
{
    public int Id { get; set; }
    public required string MasterCode { get; set; }
    public required string FullName { get; set; }
    public string? Description { get; set; }
}

public enum WebhookConsumerStatus
{
    Active = 1,
    Disabled = 2,
    Archived = 3
}
