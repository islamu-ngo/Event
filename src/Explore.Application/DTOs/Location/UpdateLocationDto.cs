using Explore.Application.Models.Common;

namespace Explore.Application.DTOs.Location;

public sealed record UpdateLocationDto
{
    public UpdateLocationFullNameDto? FullName { get; init; }
    public UpdateLocationAddressDto? Address { get; init; }
    public UpdateLocationPostcodeDto? Postcode { get; init; }
    public UpdateLocationCountryDto? Country { get; init; }
    public UpdateLocationCityDto? City { get; init; }
    public UpdateLocationTimezoneDto? Timezone { get; init; }
    public Guid? OrganizationId { get; init; }
    public string? AddressSelectionToken { get; init; }
}

public sealed record UpdateLocationFullNameDto
{
    public required string Value { get; init; }
}

public sealed record UpdateLocationAddressDto
{
    public required string Value { get; init; }
}

public sealed record UpdateLocationPostcodeDto
{
    public required string Value { get; init; }
}

public sealed record UpdateLocationCountryDto
{
    public required string Value { get; init; }
}

public sealed record UpdateLocationCityDto
{
    public required string Value { get; init; }
}

public sealed record UpdateLocationTimezoneDto
{
    public OptionalUpdate<string?> Value { get; init; } = OptionalUpdate<string?>.Unspecified();
}
