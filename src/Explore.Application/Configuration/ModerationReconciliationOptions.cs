namespace Explore.Application.Configuration;

/// <summary>Explicit execution tenancy and a persisted credential supplied by the selected secret authority.</summary>
public sealed class ModerationReconciliationOptions
{
    public const string SectionName = "Moderation:Reconciliation";

    public bool Enabled { get; set; }
    public Guid TenantId { get; set; }
    public int BatchSize { get; set; } = 100;
    public int InitialDelaySeconds { get; set; } = 60;
    public int IntervalSeconds { get; set; } = 60;

}
