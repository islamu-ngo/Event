using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Models.Storage;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;

namespace Explore.Application.Services;

public sealed class StoragePolicyResolver : IStoragePolicyResolver
{
    internal const long DefaultMaxUploadBytes = 10 * 1024 * 1024;
    internal const long DefaultTenantQuotaBytes = 1024L * 1024 * 1024;
    internal const long DefaultInstanceMaxUploadBytes = 100L * 1024 * 1024;

    private static readonly StoragePolicyIntent DefaultPolicyRequest = new(
        StorageObjectPurposes.Attachment,
        StorageObjectVisibilities.PrivateOwner,
        "application/octet-stream");

    private static readonly string[] PolicySettingKeys =
    [
        GovernanceSettingKeys.Deployment.Mode,
        GovernanceSettingKeys.TenantDelegation.LockStorage,
        GovernanceSettingKeys.Storage.Provider,
        GovernanceSettingKeys.Storage.DefaultMaxUploadBytes,
        GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes,
        GovernanceSettingKeys.Storage.InstanceMaxUploadBytes,
        GovernanceSettingKeys.Storage.RouteMatrix
    ];

    private readonly IHierarchicalSettingsResolver _settingsResolver;
    private readonly IFileStorageProviderResolver _providerResolver;
    private readonly IS3ConfigResolver? _s3ConfigResolver;

    public StoragePolicyResolver(
        IHierarchicalSettingsResolver settingsResolver,
        IFileStorageProviderResolver providerResolver,
        IS3ConfigResolver? s3ConfigResolver = null)
    {
        _settingsResolver = settingsResolver;
        _providerResolver = providerResolver;
        _s3ConfigResolver = s3ConfigResolver;
    }

    public Task<ResolvedStoragePolicy> ResolveAsync(
        Guid? tenantId,
        CancellationToken cancellationToken = default)
        => ResolveAsync(tenantId, DefaultPolicyRequest, cancellationToken);

    public async Task<ResolvedStoragePolicy> ResolveAsync(
        Guid? tenantId,
        StoragePolicyIntent request,
        CancellationToken cancellationToken = default)
    {
        var instanceSettings = await ResolveSettingsAsync(new SettingContext(), cancellationToken);
        var isMultiTenant = IsMultiTenant(instanceSettings);
        var tenantStorageLocked = ReadBool(instanceSettings, GovernanceSettingKeys.TenantDelegation.LockStorage, defaultValue: true);
        var tenantOverridesAllowed = tenantId.HasValue && (!isMultiTenant || !tenantStorageLocked);

        var effectiveSettings = instanceSettings;
        if (tenantOverridesAllowed)
        {
            effectiveSettings = await ResolveSettingsAsync(new SettingContext(TenantId: tenantId), cancellationToken);
        }

        var instanceMaxUploadBytes = PositiveOrDefault(
            ReadLong(instanceSettings, GovernanceSettingKeys.Storage.InstanceMaxUploadBytes, DefaultInstanceMaxUploadBytes),
            DefaultInstanceMaxUploadBytes);
        var requestedMaxUploadBytes = PositiveOrDefault(
            ReadLong(effectiveSettings, GovernanceSettingKeys.Storage.DefaultMaxUploadBytes, DefaultMaxUploadBytes),
            DefaultMaxUploadBytes);
        var maxUploadBytes = Math.Min(requestedMaxUploadBytes, instanceMaxUploadBytes);
        var tenantQuotaBytes = PositiveOrDefault(
            ReadLong(effectiveSettings, GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes, DefaultTenantQuotaBytes),
            DefaultTenantQuotaBytes);
        var providerSource = SourceOf(effectiveSettings, GovernanceSettingKeys.Storage.Provider);
        var configuredProvider = NormalizeProvider(ReadString(effectiveSettings, GovernanceSettingKeys.Storage.Provider, StorageProviders.Local));
        var defaultProvider = await ResolveDefaultProviderAsync(configuredProvider, providerSource, cancellationToken);
        var maxUploadSource = SourceOf(effectiveSettings, GovernanceSettingKeys.Storage.DefaultMaxUploadBytes);
        var routeMatrixSource = SourceOf(effectiveSettings, GovernanceSettingKeys.Storage.RouteMatrix);
        var routeMatrix = ReadRouteMatrix(effectiveSettings);
        var routes = ResolveRoutes(routeMatrix, defaultProvider, maxUploadBytes, instanceMaxUploadBytes, providerSource, maxUploadSource, routeMatrixSource);
        var selectedRoute = SelectRoute(routes, ResolveRouteKey(request));

        return new ResolvedStoragePolicy(
            tenantId,
            selectedRoute.Provider,
            selectedRoute.MaxUploadBytes,
            tenantQuotaBytes,
            instanceMaxUploadBytes,
            tenantOverridesAllowed,
            tenantStorageLocked,
            selectedRoute.ProviderSource,
            selectedRoute.MaxUploadSource,
            SourceOf(effectiveSettings, GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes),
            selectedRoute.RouteKey,
            routeMatrix.Version,
            routes,
            selectedRoute);
    }

    public Task<IFileStorageProvider> ResolveProviderAsync(
        Guid? tenantId,
        CancellationToken cancellationToken = default)
        => ResolveProviderAsync(tenantId, DefaultPolicyRequest, cancellationToken);

    public async Task<IFileStorageProvider> ResolveProviderAsync(
        Guid? tenantId,
        StoragePolicyIntent request,
        CancellationToken cancellationToken = default)
    {
        var policy = await ResolveAsync(tenantId, request, cancellationToken);
        return _providerResolver.GetRequired(policy.Provider);
    }


    private async Task<string> ResolveDefaultProviderAsync(
        string configuredProvider,
        SettingSource providerSource,
        CancellationToken cancellationToken)
    {
        if (configuredProvider != StorageProviders.Local || _s3ConfigResolver is null)
        {
            return configuredProvider;
        }

        if (providerSource != SettingSource.SystemDefault && providerSource != SettingSource.SystemLocked)
        {
            return configuredProvider;
        }

        return await _s3ConfigResolver.IsConfiguredAsync(cancellationToken)
            ? StorageProviders.S3Compatible
            : StorageProviders.Local;
    }

    private async Task<Dictionary<string, ResolvedSetting>> ResolveSettingsAsync(
        SettingContext context,
        CancellationToken cancellationToken)
    {
        var resolved = await _settingsResolver.ResolveBatchAsync(PolicySettingKeys, context, cancellationToken);
        return resolved.ToDictionary(setting => setting.Key, setting => setting);
    }

    private static IReadOnlyList<ResolvedStorageRoutePolicy> ResolveRoutes(
        StorageRouteMatrixDocument routeMatrix,
        string defaultProvider,
        long defaultMaxUploadBytes,
        long instanceMaxUploadBytes,
        SettingSource providerSource,
        SettingSource maxUploadSource,
        SettingSource routeMatrixSource)
    {
        var configuredRoutes = routeMatrix.Routes
            .Where(route => !string.IsNullOrWhiteSpace(route.RouteKey))
            .GroupBy(route => NormalizeRouteKey(route.RouteKey))
            .ToDictionary(group => group.Key, group => group.First());

        return StorageRouteKeys.All
            .Select(routeKey => ResolveRoute(
                routeKey,
                configuredRoutes.GetValueOrDefault(routeKey),
                defaultProvider,
                defaultMaxUploadBytes,
                instanceMaxUploadBytes,
                providerSource,
                maxUploadSource,
                routeMatrixSource))
            .ToList();
    }

    private static ResolvedStorageRoutePolicy ResolveRoute(
        string routeKey,
        StorageRouteSetting? route,
        string defaultProvider,
        long defaultMaxUploadBytes,
        long instanceMaxUploadBytes,
        SettingSource providerSource,
        SettingSource maxUploadSource,
        SettingSource routeMatrixSource)
    {
        var hasRoute = route is not null;
        var routeProvider = hasRoute ? NormalizeProvider(route!.Provider) : defaultProvider;
        var routeMaxUploadBytes = hasRoute
            ? PositiveOrDefault(route!.MaxUploadBytes, defaultMaxUploadBytes)
            : defaultMaxUploadBytes;

        return new ResolvedStorageRoutePolicy(
            routeKey,
            routeProvider,
            Math.Min(routeMaxUploadBytes, instanceMaxUploadBytes),
            hasRoute ? routeMatrixSource : providerSource,
            hasRoute && route!.MaxUploadBytes > 0 ? routeMatrixSource : maxUploadSource);
    }

    private static ResolvedStorageRoutePolicy SelectRoute(
        IReadOnlyList<ResolvedStorageRoutePolicy> routes,
        string routeKey)
        => routes.FirstOrDefault(route => route.RouteKey == routeKey)
            ?? routes.First(route => route.RouteKey == StorageRouteKeys.General);

    private static StorageRouteMatrixDocument ReadRouteMatrix(IReadOnlyDictionary<string, ResolvedSetting> settings)
    {
        if (!settings.TryGetValue(GovernanceSettingKeys.Storage.RouteMatrix, out var setting))
        {
            return StorageRouteMatrixDocument.Empty;
        }

        var document = SettingValueSerializer.Deserialize(setting.Value, StorageRouteMatrixDocument.Empty);
        return document.Routes is null
            ? StorageRouteMatrixDocument.Empty
            : document;
    }

    private static string ResolveRouteKey(StoragePolicyIntent request)
    {
        var purpose = request.Purpose.Trim().ToLowerInvariant();
        var visibility = request.Visibility.Trim().ToLowerInvariant();
        var contentType = request.ContentType.Trim().ToLowerInvariant();

        if (purpose is StorageObjectPurposes.LegacyImage or StorageObjectPurposes.ProfileImage or StorageObjectPurposes.EventImage ||
            visibility == StorageObjectVisibilities.PublicImage ||
            contentType.StartsWith("image/", StringComparison.Ordinal))
        {
            return StorageRouteKeys.Images;
        }

        if (purpose == StorageObjectPurposes.Document || IsDocumentContentType(contentType))
        {
            return StorageRouteKeys.Documents;
        }

        return StorageRouteKeys.General;
    }

    private static bool IsDocumentContentType(string contentType)
        => contentType is
            "application/pdf" or
            "application/rtf" or
            "text/rtf" or
            "application/msword" or
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" or
            "application/vnd.ms-excel" or
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" or
            "application/vnd.ms-powerpoint" or
            "application/vnd.openxmlformats-officedocument.presentationml.presentation" or
            "application/vnd.oasis.opendocument.text" or
            "application/vnd.oasis.opendocument.spreadsheet";

    private static bool IsMultiTenant(IReadOnlyDictionary<string, ResolvedSetting> settings)
    {
        var deploymentMode = ReadString(settings, GovernanceSettingKeys.Deployment.Mode, "SingleTenant");
        return deploymentMode.Equals("MultiTenant", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeProvider(string? provider)
        => provider?.Trim().ToLowerInvariant() switch
        {
            StorageProviders.S3Compatible => StorageProviders.S3Compatible,
            StorageProviders.Local => StorageProviders.Local,
            _ => StorageProviders.Local
        };

    private static string NormalizeRouteKey(string? routeKey)
        => routeKey?.Trim().ToLowerInvariant() switch
        {
            StorageRouteKeys.Images => StorageRouteKeys.Images,
            StorageRouteKeys.Documents => StorageRouteKeys.Documents,
            StorageRouteKeys.General => StorageRouteKeys.General,
            _ => StorageRouteKeys.General
        };

    private static long PositiveOrDefault(long value, long defaultValue)
        => value > 0 ? value : defaultValue;

    private static bool ReadBool(
        IReadOnlyDictionary<string, ResolvedSetting> settings,
        string key,
        bool defaultValue)
        => settings.TryGetValue(key, out var setting)
            ? SettingValueSerializer.DeserializeBool(setting.Value, defaultValue)
            : defaultValue;

    private static long ReadLong(
        IReadOnlyDictionary<string, ResolvedSetting> settings,
        string key,
        long defaultValue)
        => settings.TryGetValue(key, out var setting)
            ? SettingValueSerializer.DeserializeLong(setting.Value, defaultValue)
            : defaultValue;

    private static string ReadString(
        IReadOnlyDictionary<string, ResolvedSetting> settings,
        string key,
        string defaultValue)
        => settings.TryGetValue(key, out var setting)
            ? SettingValueSerializer.DeserializeString(setting.Value, defaultValue)
            : defaultValue;

    private static SettingSource SourceOf(
        IReadOnlyDictionary<string, ResolvedSetting> settings,
        string key)
        => settings.TryGetValue(key, out var setting) ? setting.Source : SettingSource.SystemDefault;
}
