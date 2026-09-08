namespace Explore.Application.Models.PublicExperience;

public sealed record PublicDiscoveryAreasConfig(
    int SchemaVersion = 1,
    IReadOnlyList<PublicDiscoveryAreaConfig>? Areas = null);

public sealed record PublicDiscoveryAreaConfig(
    Guid Id,
    string DisplayName,
    string City,
    string CountryCode,
    decimal? CentroidLatitude = null,
    decimal? CentroidLongitude = null,
    IReadOnlyList<Guid>? LocationIds = null,
    bool IsActive = true,
    bool IsDefault = false,
    int SortOrder = 0);
