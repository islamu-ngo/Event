namespace Explore.Application.DTOs.Location;

public sealed record LocationDto
{
    public Guid Id { get; init; }
    public required string FullName { get; init; }
    public required string Address { get; init; }
    public required string Postcode { get; init; }
    public required string Country { get; init; }
    public required string City { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public string? Timezone { get; init; }
    public Guid TenantId { get; init; }

    /// <summary>
    /// Descriptive venue kind. It never grants disclosure — it only tells management surfaces whether the
    /// consent-backed private home workflow applies to this venue.
    /// </summary>
    public int LocationKindId { get; init; }

    public Guid ConcurrencyStamp { get; init; }
}
