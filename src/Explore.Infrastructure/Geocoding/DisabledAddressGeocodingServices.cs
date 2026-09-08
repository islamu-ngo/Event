using Explore.Application.Contracts.Infrastructure.Geocoding;

namespace Explore.Infrastructure.Geocoding;

public sealed class DisabledAddressSuggestionProviderGateway
    : IAddressSuggestionProviderGateway
{
    public Task<AddressGeocoderResult> SearchAsync(
        AddressGeocoderRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(AddressGeocoderResult.None);
    }
}

public sealed class DisabledAddressSelectionProtector
    : IAddressSelectionProtector
{
    public string ConfigurationFingerprint => "disabled";

    public Task<AddressSelectionToken> ProtectAsync(
        ProtectedAddressSelection selection,
        AddressSelectionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException(
            "Address selection protection is unavailable while geocoding is disabled.");
    }

    public Task<AddressSelectionUnprotectResult> UnprotectAsync(
        string token,
        AddressSelectionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            AddressSelectionUnprotectResult.Failure(
                AddressSelectionFailureCode.Invalid));
    }
}
