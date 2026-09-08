namespace Explore.Application.DTOs.PublicExperience;

public sealed record PublicDiscoveryAreaDto
{
    public Guid Id { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string City { get; init; } = string.Empty;
    public string CountryCode { get; init; } = string.Empty;
    public decimal? CentroidLatitude { get; init; }
    public decimal? CentroidLongitude { get; init; }
    public bool IsDefault { get; init; }
    public int SortOrder { get; init; }
}
