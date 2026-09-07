using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services;

namespace Explore.Blazor.Client.Services;

public sealed partial class HomeDiscoveryService(
    IPublicExperienceClient apiClient,
    IUserSettingsService settingsService,
    ILogger<HomeDiscoveryService> logger) : IHomeDiscoveryService
{
    private const string SettingsCategory = "home-discovery";
    private const string AreaSettingKey = "home_discovery.area_id";
    private const string ModeSettingKey = "home_discovery.mode";

    public async Task<HomeDiscoveryDto?> LoadAsync(
        Guid? urlAreaId,
        string? urlMode,
        CancellationToken cancellationToken = default)
    {
        var normalizedUrlMode = NormalizeMode(urlMode);
        Guid? savedAreaId = null;
        string? savedMode = null;

        if (!urlAreaId.HasValue || normalizedUrlMode is null)
        {
            (savedAreaId, savedMode) = await LoadSavedContextAsync(cancellationToken);
        }

        return await LoadCompositeAsync(
            urlAreaId ?? savedAreaId,
            normalizedUrlMode ?? savedMode,
            cancellationToken);
    }

    public async Task<HomeDiscoveryDto?> SelectAreaAsync(
        Guid areaId,
        CancellationToken cancellationToken = default)
    {
        await settingsService.UpdateSettingsBatchAsync(
            SettingsCategory,
            new Dictionary<string, string>
            {
                [AreaSettingKey] = areaId.ToString(),
                [ModeSettingKey] = "area"
            },
            cancellationToken);

        return await LoadCompositeAsync(areaId, "area", cancellationToken);
    }

    public async Task<HomeDiscoveryDto?> SelectOnlineAsync(
        Guid? preservedAreaId,
        CancellationToken cancellationToken = default)
    {
        await settingsService.UpdateSettingsBatchAsync(
            SettingsCategory,
            new Dictionary<string, string> { [ModeSettingKey] = "online" },
            cancellationToken);

        return await LoadCompositeAsync(preservedAreaId, "online", cancellationToken);
    }

    public PublicDiscoveryAreaDto? FindClosestArea(
        IEnumerable<PublicDiscoveryAreaDto> areas,
        double latitude,
        double longitude)
    {
        if (!IsValidCoordinate(latitude, longitude))
        {
            return null;
        }

        return areas
            .Where(area => area.Id.HasValue &&
                           area.CentroidLatitude.HasValue &&
                           area.CentroidLongitude.HasValue &&
                           IsValidCoordinate(area.CentroidLatitude.Value, area.CentroidLongitude.Value))
            .MinBy(area => HaversineCentralAngle(
                latitude,
                longitude,
                area.CentroidLatitude!.Value,
                area.CentroidLongitude!.Value));
    }

    private async Task<(Guid? AreaId, string? Mode)> LoadSavedContextAsync(
        CancellationToken cancellationToken)
    {
        var settings = await settingsService.GetSettingsAsync(SettingsCategory, cancellationToken);
        var values = settings?.Settings?
            .Where(setting => !string.IsNullOrWhiteSpace(setting.Key))
            .GroupBy(setting => setting.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.Ordinal);

        Guid? areaId = values is not null &&
                       values.TryGetValue(AreaSettingKey, out var rawAreaId) &&
                       Guid.TryParse(rawAreaId, out var parsedAreaId)
            ? parsedAreaId
            : null;
        var mode = values is not null && values.TryGetValue(ModeSettingKey, out var rawMode)
            ? NormalizeMode(rawMode)
            : null;

        return (areaId, mode);
    }

    private async Task<HomeDiscoveryDto?> LoadCompositeAsync(
        Guid? areaId,
        string? mode,
        CancellationToken cancellationToken)
    {
        try
        {
            return await apiClient.GetHomeDiscoveryAsync(
                areaId,
                NormalizeMode(mode),
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ApiException exception)
        {
            var error = exception.InnerException?.Message ?? exception.Message;
            LogApiFailure(logger, exception.StatusCode, error, exception);
            return null;
        }
        catch (Exception exception)
        {
            LogUnexpectedFailure(logger, exception);
            return null;
        }
    }

    private static string? NormalizeMode(string? mode) => mode?.Trim().ToLowerInvariant() switch
    {
        "area" => "area",
        "online" => "online",
        "all" => "all",
        _ => null
    };

    private static bool IsValidCoordinate(double latitude, double longitude) =>
        double.IsFinite(latitude) &&
        double.IsFinite(longitude) &&
        latitude is >= -90 and <= 90 &&
        longitude is >= -180 and <= 180;

    private static double HaversineCentralAngle(
        double latitude1,
        double longitude1,
        double latitude2,
        double longitude2)
    {
        var latitudeDelta = DegreesToRadians(latitude2 - latitude1);
        var longitudeDelta = DegreesToRadians(longitude2 - longitude1);
        var firstLatitude = DegreesToRadians(latitude1);
        var secondLatitude = DegreesToRadians(latitude2);
        var haversine = Math.Pow(Math.Sin(latitudeDelta / 2), 2) +
                        Math.Cos(firstLatitude) * Math.Cos(secondLatitude) *
                        Math.Pow(Math.Sin(longitudeDelta / 2), 2);
        return 2 * Math.Asin(Math.Min(1, Math.Sqrt(haversine)));
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180;

    [LoggerMessage(LogLevel.Warning, "Home discovery API request failed with status {StatusCode}: {Error}")]
    private static partial void LogApiFailure(ILogger logger, int statusCode, string error, Exception exception);

    [LoggerMessage(LogLevel.Warning, "Home discovery API request failed unexpectedly.")]
    private static partial void LogUnexpectedFailure(ILogger logger, Exception exception);
}
