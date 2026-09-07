namespace Explore.Application.Models.PublicExperience;

public static class PublicDiscoveryAreasConfigValidator
{
    private const int SupportedSchemaVersion = 1;
    private const int MaximumAreas = 50;
    private const int MaximumLocationReferences = 200;

    public static IReadOnlyList<string> Validate(
        PublicDiscoveryAreasConfig config,
        IReadOnlySet<Guid> tenantLocationIds)
    {
        var errors = new List<string>();
        var areas = config.Areas ?? [];

        if (config.SchemaVersion != SupportedSchemaVersion)
            errors.Add($"Discovery-area schema version must be {SupportedSchemaVersion}.");

        if (areas.Count > MaximumAreas)
            errors.Add($"Discovery-area configuration cannot contain more than {MaximumAreas} areas.");

        if (areas.Select(area => area.Id).Distinct().Count() != areas.Count)
            errors.Add("Discovery area IDs must be unique.");

        if (areas.Count(area => area.IsDefault) > 1)
            errors.Add("Only one discovery area can be the default.");

        var allLocationIds = areas
            .SelectMany(area => area.LocationIds ?? [])
            .ToArray();

        if (allLocationIds.Length > MaximumLocationReferences)
            errors.Add($"Discovery-area configuration cannot contain more than {MaximumLocationReferences} location references.");

        if (allLocationIds.Distinct().Count() != allLocationIds.Length)
            errors.Add("A tenant location can belong to only one discovery area.");

        foreach (var area in areas)
            ValidateArea(area, tenantLocationIds, errors);

        return errors;
    }

    private static void ValidateArea(
        PublicDiscoveryAreaConfig area,
        IReadOnlySet<Guid> tenantLocationIds,
        List<string> errors)
    {
        var areaName = string.IsNullOrWhiteSpace(area.DisplayName) ? area.Id.ToString("D") : area.DisplayName;

        if (area.Id == Guid.Empty)
            errors.Add($"Discovery area '{areaName}' must have a non-empty stable ID.");

        if (string.IsNullOrWhiteSpace(area.DisplayName) || area.DisplayName.Length > 100)
            errors.Add($"Discovery area '{areaName}' must have a display name of at most 100 characters.");

        if (string.IsNullOrWhiteSpace(area.City) || area.City.Length > 100)
            errors.Add($"Discovery area '{areaName}' must have a city of at most 100 characters.");

        if (string.IsNullOrWhiteSpace(area.CountryCode) ||
            area.CountryCode.Length != 2 ||
            !area.CountryCode.All(char.IsLetter))
            errors.Add($"Discovery area '{areaName}' must have a two-letter country code.");

        if (!HasValidCentroid(area))
            errors.Add($"Discovery area '{areaName}' has an invalid centroid; provide both coordinates at no more than two decimal places.");

        if (area.IsDefault && !area.IsActive)
            errors.Add($"Discovery area '{areaName}' is invalid because the default area must be active.");

        foreach (var locationId in area.LocationIds ?? [])
        {
            if (locationId == Guid.Empty || !tenantLocationIds.Contains(locationId))
                errors.Add($"Discovery area '{areaName}' references a location outside the current tenant.");
        }
    }

    private static bool HasValidCentroid(PublicDiscoveryAreaConfig area)
    {
        if (!area.CentroidLatitude.HasValue && !area.CentroidLongitude.HasValue)
            return true;

        if (!area.CentroidLatitude.HasValue || !area.CentroidLongitude.HasValue)
            return false;

        var latitude = area.CentroidLatitude.Value;
        var longitude = area.CentroidLongitude.Value;
        return latitude is >= -90 and <= 90 &&
               longitude is >= -180 and <= 180 &&
               decimal.Round(latitude, 2) == latitude &&
               decimal.Round(longitude, 2) == longitude;
    }
}
