namespace Explore.Secrets.Configuration;

public sealed class TicketingRecoveryOperatorOptions
{
    public const string SectionName = "Ticketing:Recovery";

    public bool Enabled { get; set; }
    public string ExpectedReleaseRevision { get; set; } = string.Empty;
    public string ExpectedSchemaRevision { get; set; } = string.Empty;
    public int MinimumRetainedKeyVersion { get; set; }
    public long MinimumAuthorityFloor { get; set; }
    public long MinimumProviderCursor { get; set; }
    public long MinimumIdempotencyFloor { get; set; }
    public long MinimumWorkerFence { get; set; }
    public int WarningOldestDueSeconds { get; set; } = 60;
    public int UnhealthyOldestDueSeconds { get; set; } = 120;
    public int BacklogThreshold { get; set; } = 100;
    public int DeclaredRpoMinutes { get; set; } = 15;
    public int DeclaredRtoMinutes { get; set; } = 60;
    public string ManifestSigningKeyReference { get; set; } =
        "ticketing.recovery_manifest_hmac_key";
    public List<int> RetainedKeyVersions { get; set; } = [];
}
