using Explore.Domain.Enums;
using System.Text.Json.Serialization;

namespace Explore.Application.DTOs.Geocoding;

public sealed record AddressSuggestionsRequestDto
{
    public required string SearchText { get; init; }
    public int Limit { get; init; } = 10;
    public Guid? OrganizationId { get; init; }
    public Guid? LocationId { get; init; }
    public Guid? ExpectedConcurrencyStamp { get; init; }
}

public sealed record AddressSuggestionDto(
    Guid? LocationId,
    Guid? ConcurrencyStamp,
    string DisplayName,
    string Address,
    string Postcode,
    LocationAddressSourceEnum Source,
    LocationAddressVisibilityEnum Visibility,
    string? Attribution = null,
    string? SelectionToken = null,
    DateTimeOffset? SelectionExpiresAt = null,
    string? City = null,
    string? Country = null,
    string? Timezone = null)
{
    [JsonIgnore]
    public Guid TenantId { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<AddressProviderOutcome>))]
public enum AddressProviderOutcome
{
    None = 0,
    Ready = 1,
    Timeout = 2,
    Unavailable = 3,
    Limited = 4
}

public sealed record AddressSuggestionsResponseDto(
    IReadOnlyList<AddressSuggestionDto> Suggestions,
    AddressProviderOutcome ProviderOutcome);
