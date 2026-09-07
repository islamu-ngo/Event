using Explore.Application.DTOs.Geocoding;

namespace Explore.Application.Contracts.Infrastructure.Geocoding;

public interface IAddressSuggestionProviderGateway
{
    Task<AddressGeocoderResult> SearchAsync(
        AddressGeocoderRequest request,
        CancellationToken cancellationToken);
}

public sealed record AddressGeocoderRequest(string SearchText, int Limit);

public sealed record AddressGeocoderResult(
    IReadOnlyList<ProtectedAddressSelection> Selections,
    AddressProviderOutcome Outcome)
{
    public static AddressGeocoderResult None { get; } = new([], AddressProviderOutcome.None);
}
