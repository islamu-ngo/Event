using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.DTOs.Storage;
using Explore.Application.Models.Storage;
using Explore.Application.Telemetry;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;
using Microsoft.Extensions.DependencyInjection;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;

namespace Explore.Application.Services;

public class InstanceStorageSettingService : IInstanceStorageSettingService
{
    private readonly ISystemSettingRepository _systemSettingRepository;
    private readonly IStoragePolicyResolver _storagePolicyResolver;
    private readonly IInstanceStorageAdministration _storageAdministration;
    private readonly BusinessMetrics _metrics;
    private readonly IPolicyMutationExecutionScopeFactory _scopeFactory;

    public InstanceStorageSettingService(
        ISystemSettingRepository systemSettingRepository,
        IStoragePolicyResolver storagePolicyResolver,
        IInstanceStorageAdministration storageAdministration,
        BusinessMetrics metrics,
        IPolicyMutationExecutionScopeFactory scopeFactory)
    {
        _systemSettingRepository = systemSettingRepository;
        _storagePolicyResolver = storagePolicyResolver;
        _storageAdministration = storageAdministration;
        _metrics = metrics;
        _scopeFactory = scopeFactory;
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
        var providerStatus = await ReadProviderHealthAsync(cancellationToken);
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

    public Task<StorageSettingsMutation> ObserveConfigurationAsync(UpdateInstanceStorageSettingsCommand command,
        CancellationToken cancellationToken = default) =>
        StorageSettingsMutation.ObserveAsync(command, this, null, _scopeFactory, cancellationToken);

    public Task ApplySettingsAsync(StorageSettingsMutation observation, CancellationToken cancellationToken = default)
    {
        if (observation.TenantId is not null)
            throw new UnauthorizedAccessException("Instance configuration requires its original Global observation.");
        return observation.ExecuteAsync(this, cancellationToken);
    }

    public async Task<InstanceStorageProviderStatusDto> TestProviderAsync(
        TestInstanceStorageProviderQuery query, CancellationToken cancellationToken = default)
    {
        await using var scope = await _scopeFactory.CreateAsync(PolicyMutationScope.Instance, cancellationToken);
        var result = await scope.Services.GetRequiredService<IStorageProbeOriginalAction>().ExecuteAsync(query, cancellationToken);
        _metrics.RecordStorageProviderTest(result.Provider, result.IsAvailable ? "succeeded" : "failed", result.FailureCode);
        return result;
    }

    public async Task<InstanceStorageProviderStatusDto> TestProviderAsync(
        TestInstanceStorageProviderDuringSetupQuery query, CancellationToken cancellationToken = default)
    {
        await using var scope = await _scopeFactory.CreateAsync(PolicyMutationScope.Instance, cancellationToken);
        var result = await scope.Services.GetRequiredService<IStorageProbeOriginalAction>().ExecuteAsync(query, cancellationToken);
        _metrics.RecordStorageProviderTest(result.Provider, result.IsAvailable ? "succeeded" : "failed", result.FailureCode);
        return result;
    }

    private async Task<InstanceStorageProviderStatusDto> ReadProviderHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            var provider = await _storagePolicyResolver.ResolveProviderAsync(null, cancellationToken);
            var status = await provider.TestAsync(cancellationToken, testWritePermissions: false);

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

    public async Task<InstanceStorageUsageDto> RecalculateUsageAsync(
        RecalculateInstanceStorageUsageCommand command, CancellationToken cancellationToken = default)
    {
        return MapUsage(await _storageAdministration.RecalculateUsageAsync(command, cancellationToken));
    }

    public async Task<InstanceStorageUsageDto> RecalculateUsageAsync(
        RecalculateInstanceStorageUsageDuringSetupCommand command, CancellationToken cancellationToken = default)
    {
        return MapUsage(await _storageAdministration.RecalculateUsageAsync(command, cancellationToken));
    }

    private async Task<InstanceStorageUsageDto> ReadUsageAsync(CancellationToken cancellationToken)
    {
        var counters = await _storageAdministration.ReadUsageAsync(cancellationToken);
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

    private static InstanceStorageUsageDto MapUsage(IReadOnlyList<InstanceStorageProviderTotals> counters)
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

}
