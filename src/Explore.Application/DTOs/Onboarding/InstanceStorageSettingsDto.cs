using Explore.Application.DTOs.Storage;
using Explore.Application.Models.Storage;
using Explore.Domain;

namespace Explore.Application.DTOs.Onboarding;

public sealed record InstanceStorageSettingsDto
{
    private IReadOnlyList<StorageRouteSettingsDto> _routes =
        Array.AsReadOnly(Array.Empty<StorageRouteSettingsDto>());

    public string Provider { get; set; } = StorageProviders.Local;
    public long DefaultMaxUploadBytes { get; set; } = 10 * 1024 * 1024;
    public long DefaultTenantQuotaBytes { get; set; } = 1024L * 1024 * 1024;
    public long InstanceMaxUploadBytes { get; set; } = 100L * 1024 * 1024;
    public bool LockTenantStorage { get; set; } = true;
    public IReadOnlyList<StorageRouteSettingsDto> Routes
    {
        get => _routes;
        init => _routes = value is null ? null! : Array.AsReadOnly(value.ToArray());
    }

    public string S3Endpoint { get; set; } = string.Empty;
    public string S3PublicEndpoint { get; set; } = string.Empty;
    public string S3BucketName { get; set; } = string.Empty;
    public string S3Region { get; set; } = string.Empty;
    public bool S3ForcePathStyle { get; set; } = true;
    public int S3UploadUrlExpirationMinutes { get; set; } = 60;

    public InstanceStorageEffectivePolicyDto EffectivePolicy { get; init; } = new();
    public InstanceStorageUsageDto Usage { get; init; } = new();
    public InstanceStorageProviderStatusDto ProviderStatus { get; init; } = new();
}

public sealed record InstanceStorageEffectivePolicyDto
{
    private IReadOnlyList<StorageRouteSettingsDto> _routes =
        Array.AsReadOnly(Array.Empty<StorageRouteSettingsDto>());

    public string Provider { get; init; } = StorageProviders.Local;
    public long MaxUploadBytes { get; init; } = 10 * 1024 * 1024;
    public long TenantQuotaBytes { get; init; } = 1024L * 1024 * 1024;
    public long InstanceMaxUploadBytes { get; init; } = 100L * 1024 * 1024;
    public bool TenantOverridesAllowed { get; init; }
    public bool TenantStorageLocked { get; init; } = true;
    public string ProviderSource { get; init; } = "SystemDefault";
    public string MaxUploadSource { get; init; } = "SystemDefault";
    public string QuotaSource { get; init; } = "SystemDefault";
    public IReadOnlyList<StorageRouteSettingsDto> Routes
    {
        get => _routes;
        init => _routes = value is null ? null! : Array.AsReadOnly(value.ToArray());
    }
}

public sealed record InstanceStorageUsageDto
{
    private IReadOnlyList<InstanceStorageProviderUsageDto> _providers =
        Array.AsReadOnly(Array.Empty<InstanceStorageProviderUsageDto>());

    public long UsedBytes { get; init; }
    public long ReservedBytes { get; init; }
    public long QuarantinedBytes { get; init; }
    public long ObjectCount { get; init; }
    public DateTime? LastRecalculatedAt { get; init; }
    public IReadOnlyList<InstanceStorageProviderUsageDto> Providers
    {
        get => _providers;
        init => _providers = value is null ? null! : Array.AsReadOnly(value.ToArray());
    }
}

public sealed record InstanceStorageProviderUsageDto
{
    public string Provider { get; init; } = StorageProviders.Local;
    public long UsedBytes { get; init; }
    public long ReservedBytes { get; init; }
    public long QuarantinedBytes { get; init; }
    public long ObjectCount { get; init; }
    public DateTime? LastRecalculatedAt { get; init; }
}

public sealed record InstanceStorageProviderStatusDto
{
    public string Provider { get; init; } = StorageProviders.Local;
    public bool IsAvailable { get; init; }
    public bool SupportsServerSideStreaming { get; init; }
    public bool SupportsBrowserDirectUpload { get; init; }
    public string? FailureCode { get; init; }
    public string? Message { get; init; }
    public S3PreflightResult? Preflight { get; init; }
}
