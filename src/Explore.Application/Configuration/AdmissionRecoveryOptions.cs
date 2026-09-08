namespace Explore.Application.Configuration;

public sealed class AdmissionRecoveryOptions
{
    public const string SectionName = "Admissions:Recovery";

    public int ActiveKeyVersion { get; set; } = 1;
    public int[] RetainedKeyVersions { get; set; } = [];
    public int CapabilityLifetimeMinutes { get; set; } = 15;
    public int RateLimitBucketCount { get; set; } = 1024;
    public int RateLimitPermitCount { get; set; } = 5;
    public int RateLimitWindowSeconds { get; set; } = 900;
}
