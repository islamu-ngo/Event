namespace Explore.Domain;

public sealed class WebhookProviderModeLookup
{
    public int Id { get; set; }
    public required string MasterCode { get; set; }
    public required string FullName { get; set; }
    public string? Description { get; set; }
}

public enum WebhookProviderMode
{
    Disabled = 1,
    Local = 2,
    Svix = 3,
    Composite = 4,
    DryRun = 5
}
