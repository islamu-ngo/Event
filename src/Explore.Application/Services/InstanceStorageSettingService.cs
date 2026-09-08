using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Instance;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.DTOs.Storage;
using Explore.Application.Models.Storage;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Constants;

namespace Explore.Application.Services;

public class InstanceStorageSettingService : IInstanceStorageSettingService
{
    private readonly ISystemSettingRepository _systemSettingRepository;
    private readonly IStoragePolicyResolver _storagePolicyResolver;
    private readonly IStorageUsageCounterRepository _usageCounterRepository;
    private readonly IStorageObjectRepository _storageObjectRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly BusinessMetrics _metrics;

    public InstanceStorageSettingService(
        ISystemSettingRepository systemSettingRepository,
        IStoragePolicyResolver storagePolicyResolver,
        IStorageUsageCounterRepository usageCounterRepository,
        IStorageObjectRepository storageObjectRepository,
        IUnitOfWork unitOfWork,
        BusinessMetrics metrics)
    {
        _systemSettingRepository = systemSettingRepository;
        _storagePolicyResolver = storagePolicyResolver;
        _usageCounterRepository = usageCounterRepository;
        _storageObjectRepository = storageObjectRepository;
        _unitOfWork = unitOfWork;
        _metrics = metrics;
    }

    public async Task<InstanceStorageSettingsDto> ReadSettingsAsync(CancellationToken cancellationToken = default)
    {
        var provider = await _systemSettingRepository.GetByKey(GovernanceSettingKeys.Storage.Provider, cancellationToken);
        var defaultMaxUploadBytes = await _systemSettingRepository.GetByKey(GovernanceSettingKeys.Storage.DefaultMaxUploadBytes, cancellationToken);
        var defaultTenantQuotaBytes = await _systemSettingRepository.GetByKey(GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes, cancellationToken);
        var instanceMaxUploadBytes = await _systemSettingRepository.GetByKey(GovernanceSettingKeys.Storage.InstanceMaxUploadBytes, cancellationToken);
        var routeMatrix = await _systemSettingRepository.GetByKey(GovernanceSettingKeys.Storage.RouteMatrix, cancellationToken);
        var lockTenantStorage = await _systemSettingRepository.GetByKey(GovernanceSettingKeys.TenantDelegation.LockStorage, cancellationToken);
        var endpoint = await _systemSettingRepository.GetByKey(GovernanceSettingKeys.Storage.Endpoint, cancellationToken);
        var publicEndpoint = await _systemSettingRepository.GetByKey(GovernanceSettingKeys.Storage.PublicEndpoint, cancellationToken);
        var bucketName = await _systemSettingRepository.GetByKey(GovernanceSettingKeys.Storage.BucketName, cancellationToken);
        var region = await _systemSettingRepository.GetByKey(GovernanceSettingKeys.Storage.Region, cancellationToken);
        var forcePathStyle = await _systemSettingRepository.GetByKey(GovernanceSettingKeys.Storage.ForcePathStyle, cancellationToken);
        var uploadExpiration = await _systemSettingRepository.GetByKey(GovernanceSettingKeys.Storage.UploadUrlExpirationMinutes, cancellationToken);

        var policy = await _storagePolicyResolver.ResolveAsync(null, cancellationToken);
        var providerStatus = await TestProviderAsync(testWritePermissions: false, cancellationToken);
        var usage = await ReadUsageAsync(cancellationToken);

        return new InstanceStorageSettingsDto
        {
            Provider = policy.Provider,
            DefaultMaxUploadBytes = PositiveOrDefault(DeserializeLong(defaultMaxUploadBytes?.Value, StoragePolicyResolver.DefaultMaxUploadBytes), StoragePolicyResolver.DefaultMaxUploadBytes),
            DefaultTenantQuotaBytes = PositiveOrDefault(DeserializeLong(defaultTenantQuotaBytes?.Value, StoragePolicyResolver.DefaultTenantQuotaBytes), StoragePolicyResolver.DefaultTenantQuotaBytes),
            InstanceMaxUploadBytes = PositiveOrDefault(DeserializeLong(instanceMaxUploadBytes?.Value, StoragePolicyResolver.DefaultInstanceMaxUploadBytes), StoragePolicyResolver.DefaultInstanceMaxUploadBytes),
            LockTenantStorage = DeserializeBoolean(lockTenantStorage?.Value, true),
            Routes = MapConfiguredRoutes(routeMatrix?.Value, policy),
            S3Endpoint = DeserializeString(endpoint?.Value, string.Empty),
            S3PublicEndpoint = DeserializeString(publicEndpoint?.Value, string.Empty),
            S3BucketName = DeserializeString(bucketName?.Value, string.Empty),
            S3Region = DeserializeString(region?.Value, "fsn1"),
            S3ForcePathStyle = DeserializeBoolean(forcePathStyle?.Value, true),
            S3UploadUrlExpirationMinutes = DeserializeInt(uploadExpiration?.Value, 60),
            EffectivePolicy = MapPolicy(policy),
            Usage = usage,
            ProviderStatus = providerStatus
        };
    }

    public async Task ApplySettingsAsync(InstanceStorageSettingsDto settings, PatchInstanceStorageSettingsDto patch)
    {
        if (patch.Policy.HasValue)
        {
            var provider = NormalizeProvider(settings.Provider);

            await UpsertSystemSettingAsync(GovernanceSettingKeys.Storage.Provider,
                JsonSerializer.Serialize(provider), SettingValueType.String, false,
                "ObjectStorage", 1, "Selected storage provider. Local filesystem is default; S3-compatible storage is optional.");

            await UpsertSystemSettingAsync(GovernanceSettingKeys.Storage.DefaultMaxUploadBytes,
                JsonSerializer.Serialize(settings.DefaultMaxUploadBytes), SettingValueType.Long, false,
                "ObjectStorage", 2, "Default maximum upload size in bytes for tenant storage policy.");

            await UpsertSystemSettingAsync(GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes,
                JsonSerializer.Serialize(settings.DefaultTenantQuotaBytes), SettingValueType.Long, false,
                "ObjectStorage", 3, "Default tenant storage quota in bytes.");

            await UpsertSystemSettingAsync(GovernanceSettingKeys.Storage.InstanceMaxUploadBytes,
                JsonSerializer.Serialize(settings.InstanceMaxUploadBytes), SettingValueType.Long, false,
                "ObjectStorage", 4, "Instance-wide upload ceiling in bytes; tenant overrides cannot exceed this value.");

            await UpsertSystemSettingAsync(GovernanceSettingKeys.TenantDelegation.LockStorage,
                JsonSerializer.Serialize(settings.LockTenantStorage), SettingValueType.Boolean, false,
                "Governance", 5, "Lock tenant-level storage overrides when running multi-tenant deployments.");

            await UpsertSystemSettingAsync(GovernanceSettingKeys.Storage.RouteMatrix,
                JsonSerializer.Serialize(ToRouteMatrix(settings.Routes)), SettingValueType.Json, false,
                "ObjectStorage", 6, "Server-side route matrix that maps image, document, and general uploads to providers and byte limits.");
        }

        if (patch.S3Configuration.HasValue)
        {
            await UpsertSystemSettingAsync(GovernanceSettingKeys.Storage.Endpoint,
                JsonSerializer.Serialize(settings.S3Endpoint.Trim()), SettingValueType.String, false,
                "ObjectStorage", 7, "S3-compatible endpoint URL (e.g., https://fsn1.your-objectstorage.com)");

            await UpsertSystemSettingAsync(GovernanceSettingKeys.Storage.PublicEndpoint,
                JsonSerializer.Serialize(settings.S3PublicEndpoint.Trim()), SettingValueType.String, false,
                "ObjectStorage", 8, "Public endpoint for S3-compatible public object access when configured.");

            await UpsertSystemSettingAsync(GovernanceSettingKeys.Storage.BucketName,
                JsonSerializer.Serialize(settings.S3BucketName.Trim()), SettingValueType.String, false,
                "ObjectStorage", 9, "S3 bucket name for object storage");

            await UpsertSystemSettingAsync(GovernanceSettingKeys.Storage.Region,
                JsonSerializer.Serialize(settings.S3Region.Trim()), SettingValueType.String, false,
                "ObjectStorage", 12, "S3 region identifier (e.g., fsn1 for Hetzner, us-east-1 for AWS)");

            await UpsertSystemSettingAsync(GovernanceSettingKeys.Storage.ForcePathStyle,
                JsonSerializer.Serialize(settings.S3ForcePathStyle), SettingValueType.Boolean, false,
                "ObjectStorage", 13, "Use path-style URLs (required by most non-AWS S3 providers)");

            await UpsertSystemSettingAsync(GovernanceSettingKeys.Storage.UploadUrlExpirationMinutes,
                JsonSerializer.Serialize(settings.S3UploadUrlExpirationMinutes > 0 ? settings.S3UploadUrlExpirationMinutes : 60),
                SettingValueType.Integer, false,
                "ObjectStorage", 14, "Legacy S3 presigned upload URL expiration time in minutes");
        }
    }

    public async Task<InstanceStorageProviderStatusDto> TestProviderAsync(CancellationToken cancellationToken = default)
        => await TestProviderAsync(testWritePermissions: true, cancellationToken);

    private async Task<InstanceStorageProviderStatusDto> TestProviderAsync(
        bool testWritePermissions,
        CancellationToken cancellationToken)
    {
        try
        {
            var provider = await _storagePolicyResolver.ResolveProviderAsync(null, cancellationToken);
            var status = await provider.TestAsync(cancellationToken, testWritePermissions);

            _metrics.RecordStorageProviderTest(
                status.Provider,
                status.IsAvailable ? "succeeded" : "failed",
                status.FailureCode);

            return MapProviderStatus(status);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            _metrics.RecordStorageProviderTest(null, "failed", "provider_resolution_failed");
            throw;
        }
    }

    public async Task<InstanceStorageUsageDto> RecalculateUsageAsync(CancellationToken cancellationToken = default)
    {
        return await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var objects = await _storageObjectRepository.GetAllForInstanceStorageReportAsync(ct);
            var counters = await _usageCounterRepository.GetAllTrackedForInstanceStorageRecalculationAsync(ct);
            var countersByScope = counters
                .GroupBy(counter => (counter.TenantId, Provider: NormalizeProvider(counter.Provider)))
                .ToDictionary(group => group.Key, group => group.First());
            var groupedObjects = objects
                .GroupBy(storageObject => (storageObject.TenantId, Provider: NormalizeProvider(storageObject.Provider)))
                .ToDictionary(group => group.Key, group => group.ToList());
            var utcNow = DateTime.UtcNow;

            foreach (var group in groupedObjects)
            {
                if (!countersByScope.TryGetValue(group.Key, out var counter))
                {
                    counter = new StorageUsageCounter
                    {
                        TenantId = group.Key.TenantId,
                        Provider = group.Key.Provider
                    };
                    countersByScope[group.Key] = counter;
                    await _usageCounterRepository.Create(counter);
                }

                var quarantinedBytes = group.Value
                    .Where(storageObject => storageObject.LifecycleState == StorageObjectLifecycleStates.Quarantined)
                    .Sum(storageObject => storageObject.Size);
                var usedBytes = group.Value
                    .Where(storageObject => storageObject.LifecycleState != StorageObjectLifecycleStates.Quarantined)
                    .Sum(storageObject => storageObject.Size);

                counter.Recalculate(usedBytes, counter.ReservedBytes, quarantinedBytes, group.Value.Count, utcNow);
                await _usageCounterRepository.Update(counter);
            }

            foreach (var counter in counters.Where(counter => !groupedObjects.ContainsKey((counter.TenantId, NormalizeProvider(counter.Provider)))))
            {
                counter.Recalculate(0, counter.ReservedBytes, 0, 0, utcNow);
                await _usageCounterRepository.Update(counter);
            }

            return await ReadUsageAsync(ct);
        }, cancellationToken);
    }

    private async Task<InstanceStorageUsageDto> ReadUsageAsync(CancellationToken cancellationToken)
    {
        var counters = await _usageCounterRepository.GetAllForInstanceStorageReportAsync(cancellationToken);
        return MapUsage(counters);
    }

    private static int DeserializeInt(string? rawValue, int defaultValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return defaultValue;
        }

        try
        {
            return JsonSerializer.Deserialize<int>(rawValue);
        }
        catch
        {
            return int.TryParse(rawValue, out var parsed) ? parsed : defaultValue;
        }
    }

    private static long DeserializeLong(string? rawValue, long defaultValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return defaultValue;
        }

        try
        {
            return JsonSerializer.Deserialize<long>(rawValue);
        }
        catch
        {
            return long.TryParse(rawValue, out var parsed) ? parsed : defaultValue;
        }
    }

    private static bool DeserializeBoolean(string? rawValue, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return defaultValue;
        }

        try
        {
            return JsonSerializer.Deserialize<bool>(rawValue);
        }
        catch
        {
            return bool.TryParse(rawValue, out var parsed) ? parsed : defaultValue;
        }
    }

    private static string DeserializeString(string? rawValue, string defaultValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return defaultValue;
        }

        try
        {
            var deserialized = JsonSerializer.Deserialize<string>(rawValue);
            return string.IsNullOrWhiteSpace(deserialized) ? defaultValue : deserialized;
        }
        catch
        {
            return rawValue.Trim('"');
        }
    }

    private static string NormalizeProvider(string? provider)
        => provider?.Trim().ToLowerInvariant() switch
        {
            StorageProviders.S3Compatible => StorageProviders.S3Compatible,
            StorageProviders.Local => StorageProviders.Local,
            _ => StorageProviders.Local
        };

    private static long PositiveOrDefault(long value, long defaultValue)
        => value > 0 ? value : defaultValue;

    private static InstanceStorageEffectivePolicyDto MapPolicy(ResolvedStoragePolicy policy)
        => new()
        {
            Provider = policy.Provider,
            MaxUploadBytes = policy.MaxUploadBytes,
            TenantQuotaBytes = policy.TenantQuotaBytes,
            InstanceMaxUploadBytes = policy.InstanceMaxUploadBytes,
            TenantOverridesAllowed = policy.TenantOverridesAllowed,
            TenantStorageLocked = policy.TenantStorageLocked,
            ProviderSource = policy.ProviderSource.ToString(),
            MaxUploadSource = policy.MaxUploadSource.ToString(),
            QuotaSource = policy.QuotaSource.ToString(),
            Routes = MapResolvedRoutes(policy.Routes, isReadOnly: false)
        };

    private static List<StorageRouteSettingsDto> MapConfiguredRoutes(string? rawValue, ResolvedStoragePolicy policy)
    {
        var document = string.IsNullOrWhiteSpace(rawValue)
            ? StorageRouteMatrixDocument.Empty
            : JsonSerializer.Deserialize<StorageRouteMatrixDocument>(rawValue) ?? StorageRouteMatrixDocument.Empty;
        var configured = document.Routes
            .GroupBy(route => NormalizeRouteKey(route.RouteKey))
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        return policy.Routes
            .Select(route => configured.TryGetValue(route.RouteKey, out var setting)
                ? new StorageRouteSettingsDto
                {
                    RouteKey = route.RouteKey,
                    Provider = NormalizeProvider(setting.Provider),
                    MaxUploadBytes = PositiveOrDefault(setting.MaxUploadBytes, route.MaxUploadBytes),
                    ProviderSource = route.ProviderSource.ToString(),
                    MaxUploadSource = route.MaxUploadSource.ToString()
                }
                : MapResolvedRoute(route, isReadOnly: false))
            .ToList();
    }

    private static StorageRouteMatrixDocument ToRouteMatrix(IEnumerable<StorageRouteSettingsDto> routes)
    {
        var settings = routes
            .GroupBy(route => NormalizeRouteKey(route.RouteKey))
            .Where(group => StorageRouteKeys.All.Contains(group.Key))
            .Select(group => group.First())
            .Select(route => new StorageRouteSetting(
                NormalizeRouteKey(route.RouteKey),
                NormalizeProvider(route.Provider),
                route.MaxUploadBytes))
            .ToList();

        return new StorageRouteMatrixDocument(1, settings);
    }

    private static List<StorageRouteSettingsDto> MapResolvedRoutes(
        IReadOnlyList<ResolvedStorageRoutePolicy> routes,
        bool isReadOnly)
        => routes.Select(route => MapResolvedRoute(route, isReadOnly)).ToList();

    private static StorageRouteSettingsDto MapResolvedRoute(ResolvedStorageRoutePolicy route, bool isReadOnly)
        => new()
        {
            RouteKey = route.RouteKey,
            Provider = route.Provider,
            MaxUploadBytes = route.MaxUploadBytes,
            ProviderSource = route.ProviderSource.ToString(),
            MaxUploadSource = route.MaxUploadSource.ToString(),
            IsReadOnly = isReadOnly
        };

    private static string NormalizeRouteKey(string? routeKey)
        => routeKey?.Trim().ToLowerInvariant() switch
        {
            StorageRouteKeys.Images => StorageRouteKeys.Images,
            StorageRouteKeys.Documents => StorageRouteKeys.Documents,
            StorageRouteKeys.General => StorageRouteKeys.General,
            _ => StorageRouteKeys.General
        };

    private static InstanceStorageProviderStatusDto MapProviderStatus(FileStorageProviderStatus status)
        => new()
        {
            Provider = status.Provider,
            IsAvailable = status.IsAvailable,
            SupportsServerSideStreaming = status.SupportsServerSideStreaming,
            SupportsBrowserDirectUpload = status.SupportsBrowserDirectUpload,
            FailureCode = status.FailureCode,
            Message = status.Message,
            Preflight = status.Preflight
        };

    private static InstanceStorageUsageDto MapUsage(IReadOnlyList<StorageUsageCounter> counters)
    {
        var providerUsage = counters
            .GroupBy(counter => NormalizeProvider(counter.Provider))
            .Select(group => new InstanceStorageProviderUsageDto
            {
                Provider = group.Key,
                UsedBytes = group.Sum(counter => counter.UsedBytes),
                ReservedBytes = group.Sum(counter => counter.ReservedBytes),
                QuarantinedBytes = group.Sum(counter => counter.QuarantinedBytes),
                ObjectCount = group.Sum(counter => counter.ObjectCount),
                LastRecalculatedAt = group
                    .Where(counter => counter.LastRecalculatedAt.HasValue)
                    .Select(counter => counter.LastRecalculatedAt)
                    .DefaultIfEmpty()
                    .Max()
            })
            .OrderBy(usage => usage.Provider, StringComparer.Ordinal)
            .ToList();

        return new InstanceStorageUsageDto
        {
            UsedBytes = counters.Sum(counter => counter.UsedBytes),
            ReservedBytes = counters.Sum(counter => counter.ReservedBytes),
            QuarantinedBytes = counters.Sum(counter => counter.QuarantinedBytes),
            ObjectCount = counters.Sum(counter => counter.ObjectCount),
            LastRecalculatedAt = counters
                .Where(counter => counter.LastRecalculatedAt.HasValue)
                .Select(counter => counter.LastRecalculatedAt)
                .DefaultIfEmpty()
                .Max(),
            Providers = providerUsage
        };
    }

    private async Task UpsertSystemSettingAsync(
        string settingKey,
        string value,
        SettingValueType valueType,
        bool isLocked,
        string category,
        int displayOrder,
        string description)
    {
        await _systemSettingRepository.UpsertAsync(new SystemSetting
        {
            SettingKey = settingKey,
            Value = value,
            ValueType = valueType,
            IsLocked = isLocked,
            Description = description,
            Category = category,
            DisplayOrder = displayOrder,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
    }
}
