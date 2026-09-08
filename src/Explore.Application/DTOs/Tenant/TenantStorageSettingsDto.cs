using Explore.Application.DTOs.Storage;
using Explore.Application.Models.Common;
using Explore.Domain;

namespace Explore.Application.DTOs.Tenant;

public sealed record PatchTenantStorageSettingsDto
{
    public PatchTenantStoragePolicyDto? Policy { get; init; }
    public PatchTenantStorageS3Dto? S3 { get; init; }
}

public sealed record PatchTenantStoragePolicyDto
{
    public OptionalUpdate<string> Provider { get; init; }
    public OptionalUpdate<long> MaxUploadBytes { get; init; }
    public OptionalUpdate<long> TenantQuotaBytes { get; init; }
    public OptionalUpdate<List<StorageRouteSettingsDto>> Routes { get; init; }
}

public sealed record PatchTenantStorageS3Dto
{
    public OptionalUpdate<string> Endpoint { get; init; }
    public OptionalUpdate<string> PublicEndpoint { get; init; }
    public OptionalUpdate<string> BucketName { get; init; }
    public OptionalUpdate<string> Region { get; init; }
    public OptionalUpdate<bool> ForcePathStyle { get; init; }
    public OptionalUpdate<int> UploadUrlExpirationMinutes { get; init; }
}

public sealed record TenantStorageSettingsDto
{
    private IReadOnlyList<StorageRouteSettingsDto> _routes =
        Array.AsReadOnly(Array.Empty<StorageRouteSettingsDto>());

    public Guid TenantId { get; init; }
    public string Provider { get; set; } = StorageProviders.Local;
    public long MaxUploadBytes { get; set; } = 10 * 1024 * 1024;
    public long TenantQuotaBytes { get; set; } = 1024L * 1024 * 1024;
    public bool IsReadOnly { get; init; }
    public bool TenantOverridesAllowed { get; init; }
    public bool TenantStorageLocked { get; init; } = true;
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

    public TenantStorageEffectivePolicyDto EffectivePolicy { get; init; } = new();
    public TenantStorageUsageDto Usage { get; init; } = new();
}

public sealed record TenantStorageEffectivePolicyDto
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

public sealed record TenantStorageUsageDto
{
    public string Provider { get; init; } = StorageProviders.Local;
    public long UsedBytes { get; init; }
    public long ReservedBytes { get; init; }
    public long QuarantinedBytes { get; init; }
    public long ObjectCount { get; init; }
    public long AvailableBytes { get; init; }
    public DateTime? LastRecalculatedAt { get; init; }
}
