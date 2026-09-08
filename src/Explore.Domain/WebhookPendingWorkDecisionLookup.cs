namespace Explore.Domain;

public sealed class WebhookPendingWorkDecisionLookup
{
    public int Id { get; set; }
    public required string MasterCode { get; set; }
    public required string FullName { get; set; }
    public string? Description { get; set; }
}

public enum WebhookPendingWorkDecision
{
    PreserveExisting = 1,
    MigrateEligible = 2
}
