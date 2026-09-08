namespace Explore.Domain;

public sealed class WebhookConsumerKindLookup
{
    public int Id { get; set; }
    public required string MasterCode { get; set; }
    public required string FullName { get; set; }
    public string? Description { get; set; }
}

public enum WebhookConsumerKind
{
    Tenant = 1,
    Organization = 2,
    Group = 3,
    User = 4,
    Instance = 5
}
