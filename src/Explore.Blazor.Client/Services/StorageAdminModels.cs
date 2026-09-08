using Explore.Blazor.Client.Clients;
using InstanceStorageRouteDto = Explore.Blazor.Client.Clients.Routes;
using TenantStorageRouteDto = Explore.Blazor.Client.Clients.Routes2;

namespace Explore.Blazor.Client.Services;

public static class StorageRouteOptions
{
    public const string Images = "images";
    public const string Documents = "documents";
    public const string General = "general";

    public static readonly string[] All = [Images, Documents, General];

    public static string Normalize(string? routeKey) =>
        All.Contains(routeKey?.Trim().ToLowerInvariant() ?? string.Empty, StringComparer.Ordinal)
            ? routeKey!.Trim().ToLowerInvariant()
            : General;

    public static string Label(string? routeKey) => Normalize(routeKey) switch
    {
        Images => "Images",
        Documents => "Documents",
        _ => "General uploads"
    };

    public static string Description(string? routeKey) => Normalize(routeKey) switch
    {
        Images => "Profile, event, and image MIME uploads.",
        Documents => "PDF, office, and document MIME uploads.",
        _ => "Attachments and uploads that do not match a specialized route."
    };
}

public static class StorageProviderOptions
{
    public const string Local = "local";
    public const string S3Compatible = "s3_compatible";

    public static string Normalize(string? provider) =>
        string.Equals(provider, S3Compatible, StringComparison.OrdinalIgnoreCase)
            ? S3Compatible
            : Local;

    public static string Label(string? provider) =>
        Normalize(provider) == S3Compatible ? "S3-compatible object storage" : "Local file storage";
}

public static class StorageAdminExtensions
{
    private const long MiB = 1024L * 1024L;
    private const long GiB = 1024L * 1024L * 1024L;

    public static HalResourceOfInstanceStorageSettingsDto InitializeForEditing(
        this HalResourceOfInstanceStorageSettingsDto settings)
    {
        settings.Provider = StorageProviderOptions.Normalize(settings.Provider);
        settings.DefaultMaxUploadBytes = PositiveOrDefault(settings.DefaultMaxUploadBytes, 10 * MiB);
        settings.DefaultTenantQuotaBytes = PositiveOrDefault(settings.DefaultTenantQuotaBytes, GiB);
        settings.InstanceMaxUploadBytes = PositiveOrDefault(settings.InstanceMaxUploadBytes, 100 * MiB);
        settings.LockTenantStorage ??= true;
        settings.Routes = NormalizeRoutes(settings.Routes, settings.DefaultMaxUploadBytes.Value);
        settings.S3Endpoint ??= string.Empty;
        settings.S3PublicEndpoint ??= string.Empty;
        settings.S3BucketName ??= string.Empty;
        settings.S3Region ??= string.Empty;
        settings.S3ForcePathStyle ??= true;
        settings.S3UploadUrlExpirationMinutes = PositiveOrDefault(settings.S3UploadUrlExpirationMinutes, 60);
        settings.EffectivePolicy ??= new EffectivePolicy();
        settings.Usage ??= new Usage();
        return settings;
    }

    public static HalResourceOfTenantStorageSettingsDto InitializeForEditing(
        this HalResourceOfTenantStorageSettingsDto settings)
    {
        settings.Provider = StorageProviderOptions.Normalize(settings.Provider);
        settings.MaxUploadBytes = PositiveOrDefault(settings.MaxUploadBytes, 10 * MiB);
        settings.TenantQuotaBytes = PositiveOrDefault(settings.TenantQuotaBytes, GiB);
        settings.IsReadOnly ??= true;
        settings.TenantOverridesAllowed ??= false;
        settings.TenantStorageLocked ??= true;
        settings.Routes = NormalizeRoutes(settings.Routes, settings.MaxUploadBytes.Value, settings.IsReadOnly.Value);
        settings.S3Endpoint ??= string.Empty;
        settings.S3PublicEndpoint ??= string.Empty;
        settings.S3BucketName ??= string.Empty;
        settings.S3Region ??= string.Empty;
        settings.S3ForcePathStyle ??= true;
        settings.S3UploadUrlExpirationMinutes = PositiveOrDefault(settings.S3UploadUrlExpirationMinutes, 60);
        settings.EffectivePolicy ??= new EffectivePolicy2();
        settings.Usage ??= new Usage2();
        return settings;
    }

    public static bool HasLink(this HalResourceOfInstanceStorageSettingsDto settings, string rel) =>
        settings._links?.ContainsKey(rel) == true;

    public static bool HasLink(this HalResourceOfTenantStorageSettingsDto settings, string rel) =>
        settings._links?.ContainsKey(rel) == true;

    public static bool IsEditable(this HalResourceOfTenantStorageSettingsDto settings) =>
        settings.HasLink("edit")
        && settings.IsReadOnly != true
        && settings.TenantStorageLocked != true
        && settings.TenantOverridesAllowed == true;

    public static PatchInstanceStorageSettingsDto ToUpdateRequest(
        this HalResourceOfInstanceStorageSettingsDto settings) => new()
        {
            Policy = new OptionalUpdateOfInstanceStoragePolicyWriteDto
            {
                HasValue = true,
                Value = new InstanceStoragePolicyWriteDto
                {
                    Provider = StorageProviderOptions.Normalize(settings.Provider),
                    DefaultMaxUploadBytes = settings.DefaultMaxUploadBytes,
                    DefaultTenantQuotaBytes = settings.DefaultTenantQuotaBytes,
                    InstanceMaxUploadBytes = settings.InstanceMaxUploadBytes,
                    LockTenantStorage = settings.LockTenantStorage,
                    Routes = settings.Routes?.Select(ToRequestRoute).ToList() ?? []
                }
            },
            S3Configuration = new OptionalUpdateOfInstanceS3ConfigurationWriteDto
            {
                HasValue = true,
                Value = new InstanceS3ConfigurationWriteDto
                {
                    Endpoint = NullIfWhiteSpace(settings.S3Endpoint),
                    PublicEndpoint = NullIfWhiteSpace(settings.S3PublicEndpoint),
                    BucketName = NullIfWhiteSpace(settings.S3BucketName),
                    Region = NullIfWhiteSpace(settings.S3Region),
                    ForcePathStyle = settings.S3ForcePathStyle,
                    UploadUrlExpirationMinutes = settings.S3UploadUrlExpirationMinutes
                }
            }
        };

    public static PatchTenantStorageSettingsDto ToPolicyPatchRequest(
        this HalResourceOfTenantStorageSettingsDto settings) => new()
        {
            Policy = new PatchTenantStoragePolicyDto
            {
                Provider = OptionalString(StorageProviderOptions.Normalize(settings.Provider)),
                MaxUploadBytes = OptionalLong(settings.MaxUploadBytes),
                TenantQuotaBytes = OptionalLong(settings.TenantQuotaBytes),
                Routes = new OptionalUpdateOfListOfStorageRouteSettingsDto
                {
                    HasValue = true,
                    Value = settings.Routes?.Select(ToRequestRoute).ToList() ?? []
                }
            }
        };

    public static PatchTenantStorageSettingsDto ToS3PatchRequest(
        this HalResourceOfTenantStorageSettingsDto settings) => new()
        {
            S3 = new PatchTenantStorageS3Dto
            {
                Endpoint = OptionalString(NullIfWhiteSpace(settings.S3Endpoint)),
                PublicEndpoint = OptionalString(NullIfWhiteSpace(settings.S3PublicEndpoint)),
                BucketName = OptionalString(NullIfWhiteSpace(settings.S3BucketName)),
                Region = OptionalString(NullIfWhiteSpace(settings.S3Region)),
                ForcePathStyle = new OptionalUpdateOfboolean
                {
                    HasValue = true,
                    Value = settings.S3ForcePathStyle
                },
                UploadUrlExpirationMinutes = new OptionalUpdateOfint
                {
                    HasValue = true,
                    Value = settings.S3UploadUrlExpirationMinutes
                }
            }
        };

    public static long BytesToMiB(long? bytes) => Math.Max(1, (bytes ?? MiB) / MiB);
    public static long MiBToBytes(long value) => Math.Max(1, value) * MiB;
    public static long BytesToGiB(long? bytes) => Math.Max(1, (bytes ?? GiB) / GiB);
    public static long GiBToBytes(long value) => Math.Max(1, value) * GiB;

    private static List<InstanceStorageRouteDto> NormalizeRoutes(
        ICollection<InstanceStorageRouteDto>? routes,
        long fallbackMaxUploadBytes)
    {
        var byKey = routes?.GroupBy(route => StorageRouteOptions.Normalize(route.RouteKey))
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal)
            ?? new Dictionary<string, InstanceStorageRouteDto>(StringComparer.Ordinal);

        return StorageRouteOptions.All.Select(routeKey =>
        {
            if (!byKey.TryGetValue(routeKey, out var route))
            {
                route = new InstanceStorageRouteDto();
            }

            route.RouteKey = routeKey;
            route.Provider = StorageProviderOptions.Normalize(route.Provider);
            route.MaxUploadBytes = PositiveOrDefault(route.MaxUploadBytes, fallbackMaxUploadBytes);
            route.ProviderSource ??= "SystemDefault";
            route.MaxUploadSource ??= "SystemDefault";
            route.IsReadOnly ??= false;
            return route;
        }).ToList();
    }

    private static List<TenantStorageRouteDto> NormalizeRoutes(
        ICollection<TenantStorageRouteDto>? routes,
        long fallbackMaxUploadBytes,
        bool isReadOnly)
    {
        var byKey = routes?.GroupBy(route => StorageRouteOptions.Normalize(route.RouteKey))
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal)
            ?? new Dictionary<string, TenantStorageRouteDto>(StringComparer.Ordinal);

        return StorageRouteOptions.All.Select(routeKey =>
        {
            if (!byKey.TryGetValue(routeKey, out var route))
            {
                route = new TenantStorageRouteDto();
            }

            route.RouteKey = routeKey;
            route.Provider = StorageProviderOptions.Normalize(route.Provider);
            route.MaxUploadBytes = PositiveOrDefault(route.MaxUploadBytes, fallbackMaxUploadBytes);
            route.ProviderSource ??= "SystemDefault";
            route.MaxUploadSource ??= "SystemDefault";
            route.IsReadOnly ??= isReadOnly;
            return route;
        }).ToList();
    }

    private static StorageRouteSettingsDto ToRequestRoute(InstanceStorageRouteDto route) => new()
    {
        RouteKey = StorageRouteOptions.Normalize(route.RouteKey),
        Provider = StorageProviderOptions.Normalize(route.Provider),
        MaxUploadBytes = PositiveOrDefault(route.MaxUploadBytes, MiB),
        ProviderSource = route.ProviderSource,
        MaxUploadSource = route.MaxUploadSource,
        IsReadOnly = route.IsReadOnly
    };

    private static StorageRouteSettingsDto ToRequestRoute(TenantStorageRouteDto route) => new()
    {
        RouteKey = StorageRouteOptions.Normalize(route.RouteKey),
        Provider = StorageProviderOptions.Normalize(route.Provider),
        MaxUploadBytes = PositiveOrDefault(route.MaxUploadBytes, MiB),
        ProviderSource = route.ProviderSource,
        MaxUploadSource = route.MaxUploadSource,
        IsReadOnly = route.IsReadOnly
    };

    private static OptionalUpdateOfstring OptionalString(string? value) => new()
    {
        HasValue = true,
        Value = value
    };

    private static OptionalUpdateOflong OptionalLong(long? value) => new()
    {
        HasValue = true,
        Value = value
    };

    private static long PositiveOrDefault(long? value, long fallback) => value is > 0 ? value.Value : fallback;
    private static int PositiveOrDefault(int? value, int fallback) => value is > 0 ? value.Value : fallback;
    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
