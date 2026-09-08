using Explore.Application.Models.Common;

namespace Explore.Application.DTOs.Organization;

public sealed record UpdateOrganizationDto
{
    public UpdateOrganizationFullNameDto? FullName { get; init; }
    public UpdateOrganizationWebsiteUrlDto? WebsiteUrl { get; init; }
    public UpdateOrganizationEmailDto? Email { get; init; }
    public UpdateOrganizationCountryDto? Country { get; init; }
    public UpdateOrganizationCityDto? City { get; init; }
    public UpdateOrganizationPostcodeDto? Postcode { get; init; }
    public UpdateOrganizationAddressDto? Address { get; init; }
}

public sealed record UpdateOrganizationFullNameDto
{
    public required string Value { get; init; }
}

public sealed record UpdateOrganizationWebsiteUrlDto
{
    public OptionalUpdate<string?> Value { get; init; } = OptionalUpdate<string?>.Unspecified();
}

public sealed record UpdateOrganizationEmailDto
{
    public required string Value { get; init; }
}

public sealed record UpdateOrganizationCountryDto
{
    public required string Value { get; init; }
}

public sealed record UpdateOrganizationCityDto
{
    public required string Value { get; init; }
}

public sealed record UpdateOrganizationPostcodeDto
{
    public int Value { get; init; }
}

public sealed record UpdateOrganizationAddressDto
{
    public required string Value { get; init; }
}
