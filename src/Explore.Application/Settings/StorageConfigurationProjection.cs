using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.DTOs.Storage;
using Explore.Application.DTOs.Tenant;
using Explore.Application.Models.Storage;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Constants;

namespace Explore.Application.Settings;

/// <summary>Pure configuration projection of the retained sources; no cache, health or usage reader.</summary>
internal static class StorageConfigurationProjection
{
    internal static ResolvedSetting? Resolve(StorageConfigurationState state, string key, bool tenant)
    {
        var system = state.System[key];
        var local = tenant ? state.Tenant[key] : null;
        return HierarchicalSettingMerge.Resolve(key, new SettingMergeFacts(
            system is null ? null : new SystemSettingFacts(system.Value, system.ValueType!.Value,
                system.IsLocked, system.Description, system.Category, system.AllowedValues),
            local is null ? null : new TenantSettingFacts(local.Value, local.IsLocked),
            null, null, null, tenant, null));
    }

    internal static string Text(StorageConfigurationState state, string key, bool tenant, string fallback = "") =>
        SettingValueSerializer.DeserializeString(Resolve(state, key, tenant)?.Value, fallback);

    internal static bool TenantAllowed(StorageConfigurationState state) =>
        !Text(state, GovernanceSettingKeys.Deployment.Mode, false, "SingleTenant")
            .Equals("MultiTenant", StringComparison.OrdinalIgnoreCase)
        || !SettingValueSerializer.DeserializeBool(Resolve(state, GovernanceSettingKeys.TenantDelegation.LockStorage, false)?.Value, true);

    internal static (InstanceStorageSettingsDto Instance, TenantStorageSettingsDto Tenant) Project(
        StorageConfigurationState state, Guid ambientTenant, bool configured)
    {
        long ceiling = Positive(SettingValueSerializer.DeserializeLong(
            Resolve(state, GovernanceSettingKeys.Storage.InstanceMaxUploadBytes, false)?.Value,
            StoragePolicyResolver.DefaultInstanceMaxUploadBytes), StoragePolicyResolver.DefaultInstanceMaxUploadBytes);
        bool allowed = TenantAllowed(state);
        long Upload(bool tenant) => Math.Min(ceiling, Positive(SettingValueSerializer.DeserializeLong(
            Resolve(state, GovernanceSettingKeys.Storage.DefaultMaxUploadBytes, tenant)?.Value,
            StoragePolicyResolver.DefaultMaxUploadBytes), StoragePolicyResolver.DefaultMaxUploadBytes));
        long Quota(bool tenant) => Positive(SettingValueSerializer.DeserializeLong(
            Resolve(state, GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes, tenant)?.Value,
            StoragePolicyResolver.DefaultTenantQuotaBytes), StoragePolicyResolver.DefaultTenantQuotaBytes);
        List<StorageRouteSettingsDto> Routes(bool tenant)
        {
            var selection = Resolve(state, GovernanceSettingKeys.Storage.Provider, tenant);
            string provider = NormalizeProvider(SettingValueSerializer.DeserializeString(selection?.Value, StorageProviders.Local));
            if (provider == StorageProviders.Local && configured
                && (selection is null || selection.Source is SettingSource.SystemDefault or SettingSource.SystemLocked))
                provider = StorageProviders.S3Compatible;
            var document = SettingValueSerializer.Deserialize(
                Resolve(state, GovernanceSettingKeys.Storage.RouteMatrix, tenant)?.Value, StorageRouteMatrixDocument.Empty);
            var configuredRoutes = (document.Routes ?? []).GroupBy(row => NormalizeRoute(row.RouteKey))
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            return StorageRouteKeys.All.Select(key =>
            {
                configuredRoutes.TryGetValue(key, out var row);
                return new StorageRouteSettingsDto
                {
                    RouteKey = key,
                    Provider = row is null ? provider : NormalizeProvider(row.Provider),
                    // Configuration projection preserves an explicit positive value so validation
                    // can reject it above the ceiling, rather than silently clamping a bad source.
                    MaxUploadBytes = row is { MaxUploadBytes: > 0 } ? row.MaxUploadBytes : Upload(tenant)
                };
            }).ToList();
        }
        var instanceRoutes = Routes(false);
        var tenantRoutes = Routes(allowed);
        var instance = new InstanceStorageSettingsDto
        {
            Provider = instanceRoutes.Single(row => row.RouteKey == StorageRouteKeys.General).Provider,
            DefaultMaxUploadBytes = Positive(SettingValueSerializer.DeserializeLong(
                state.System[GovernanceSettingKeys.Storage.DefaultMaxUploadBytes]?.Value,
                StoragePolicyResolver.DefaultMaxUploadBytes), StoragePolicyResolver.DefaultMaxUploadBytes),
            DefaultTenantQuotaBytes = Positive(SettingValueSerializer.DeserializeLong(
                state.System[GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes]?.Value,
                StoragePolicyResolver.DefaultTenantQuotaBytes), StoragePolicyResolver.DefaultTenantQuotaBytes),
            InstanceMaxUploadBytes = Positive(SettingValueSerializer.DeserializeLong(
                state.System[GovernanceSettingKeys.Storage.InstanceMaxUploadBytes]?.Value,
                StoragePolicyResolver.DefaultInstanceMaxUploadBytes), StoragePolicyResolver.DefaultInstanceMaxUploadBytes),
            LockTenantStorage = SettingValueSerializer.DeserializeBool(
                state.System[GovernanceSettingKeys.TenantDelegation.LockStorage]?.Value, true),
            Routes = instanceRoutes,
            S3Endpoint = RawText(GovernanceSettingKeys.Storage.Endpoint),
            S3PublicEndpoint = RawText(GovernanceSettingKeys.Storage.PublicEndpoint),
            S3BucketName = RawText(GovernanceSettingKeys.Storage.BucketName),
            S3Region = RawText(GovernanceSettingKeys.Storage.Region, "fsn1"),
            S3ForcePathStyle = SettingValueSerializer.DeserializeBool(state.System[GovernanceSettingKeys.Storage.ForcePathStyle]?.Value, true),
            S3UploadUrlExpirationMinutes = SettingValueSerializer.DeserializeInt(
                state.System[GovernanceSettingKeys.Storage.UploadUrlExpirationMinutes]?.Value, 60)
        };
        int expiration = SettingValueSerializer.DeserializeInt(
            Resolve(state, GovernanceSettingKeys.Storage.UploadUrlExpirationMinutes, allowed)?.Value, 60);
        var tenant = new TenantStorageSettingsDto
        {
            TenantId = ambientTenant,
            Provider = tenantRoutes.Single(row => row.RouteKey == StorageRouteKeys.General).Provider,
            MaxUploadBytes = Math.Min(ceiling, tenantRoutes.Single(row => row.RouteKey == StorageRouteKeys.General).MaxUploadBytes),
            TenantQuotaBytes = Quota(allowed),
            IsReadOnly = !allowed,
            TenantOverridesAllowed = allowed,
            TenantStorageLocked = instance.LockTenantStorage,
            Routes = tenantRoutes,
            S3Endpoint = Text(state, GovernanceSettingKeys.Storage.Endpoint, allowed),
            S3PublicEndpoint = Text(state, GovernanceSettingKeys.Storage.PublicEndpoint, allowed),
            S3BucketName = Text(state, GovernanceSettingKeys.Storage.BucketName, allowed),
            S3Region = Text(state, GovernanceSettingKeys.Storage.Region, allowed, "fsn1"),
            S3ForcePathStyle = SettingValueSerializer.DeserializeBool(
                Resolve(state, GovernanceSettingKeys.Storage.ForcePathStyle, allowed)?.Value, true),
            S3UploadUrlExpirationMinutes = expiration > 0 ? expiration : 60,
            EffectivePolicy = new() { InstanceMaxUploadBytes = ceiling }
        };
        return (instance, tenant);

        string RawText(string key, string fallback = "") =>
            SettingValueSerializer.DeserializeString(state.System[key]?.Value, fallback);
    }

    internal static string NormalizeProvider(string? provider) =>
        provider?.Trim().ToLowerInvariant() == StorageProviders.S3Compatible ? StorageProviders.S3Compatible : StorageProviders.Local;
    internal static string NormalizeRoute(string? key) => key?.Trim().ToLowerInvariant() switch
    {
        StorageRouteKeys.Images => StorageRouteKeys.Images,
        StorageRouteKeys.Documents => StorageRouteKeys.Documents,
        _ => StorageRouteKeys.General
    };
    private static long Positive(long value, long fallback) => value > 0 ? value : fallback;
}
