namespace Explore.Infrastructure;

public sealed class StorageReconciliationSettings
{
    public const string SectionName = "StorageReconciliation";

    public bool Enabled { get; set; } = true;
    public bool DryRun { get; set; } = true;
    public int InitialDelaySeconds { get; set; } = 45;
    public int PollingIntervalMinutes { get; set; } = 360;
    public int BatchSize { get; set; } = 500;
    public int MissingObjectQuarantineGraceHours { get; set; } = 24;
    public int OrphanFileQuarantineGraceHours { get; set; } = 24;
    public int DeleteGraceHours { get; set; } = 720;
    public bool QuarantineMissingObjects { get; set; }
    public bool QuarantineOrphanLocalFiles { get; set; }
    public bool DeleteQuarantinedObjects { get; set; }
}
