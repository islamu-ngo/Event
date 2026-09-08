using System.Security.Cryptography;
using Explore.Application.Contracts.Infrastructure.Geocoding;
using Explore.Infrastructure.Geocoding;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Tests.Geocoding;

internal sealed class PhotonSelectionTokenContractHost : IDisposable
{
    private static readonly Guid ActorId =
        Guid.Parse("019d2f3b-a22f-745d-b4e0-cdb2c6befa70");
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly PhotonManualTimeProvider _timeProvider;
    private readonly PhotonObservabilityCapture _observability;
    private readonly string _datasetVersion;
    private DataProtectionAddressSelectionProtector _protector;

    public PhotonSelectionTokenContractHost(
        IDataProtectionProvider dataProtectionProvider,
        PhotonManualTimeProvider timeProvider,
        PhotonObservabilityCapture observability,
        string datasetVersion = "dataset-canary-v1")
    {
        _dataProtectionProvider = dataProtectionProvider;
        _timeProvider = timeProvider;
        _observability = observability;
        _datasetVersion = datasetVersion;
        _protector = CreateProtector(300);
    }

    public int CurrentVersion => _protector.CurrentVersion;

    public object CreateSelection(
        string displayName = "Display Canary",
        string address = "Address Canary 30",
        string postcode = "PII-1040",
        double latitude = 50.8503,
        double longitude = 4.3517,
        string providerRecordId = "provider-record-canary") =>
        new ProtectedAddressSelection
        {
            DisplayName = displayName,
            Address = address,
            Postcode = postcode,
            City = "Brussels",
            Country = "Belgium",
            Latitude = latitude,
            Longitude = longitude,
            Attribution = "OpenStreetMap",
            Provenance = new ProtectedAddressProvenance
            {
                Provider = "Photon",
                ProviderRecordId = providerRecordId,
                DatasetVersion = "dataset-canary-v1"
            }
        };

    public string Protect(object selection, Guid tenantId, TimeSpan lifetime)
    {
        _protector = CreateProtector(checked((int)lifetime.TotalSeconds));
        AddressSelectionToken token = _protector.ProtectAsync(
                (ProtectedAddressSelection)selection,
                CreateContext(tenantId),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        return token.Value;
    }

    public object Unprotect(string token, Guid tenantId)
    {
        AddressSelectionUnprotectResult result = _protector.UnprotectAsync(
                token,
                CreateContext(tenantId),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        if (!result.IsSuccess || result.Selection is null)
        {
            throw new CryptographicException("The address selection token is invalid.");
        }

        return result.Selection;
    }

    public void Dispose() => _observability.Dispose();

    private DataProtectionAddressSelectionProtector CreateProtector(
        int selectionLifetimeSeconds) =>
        new(
            _dataProtectionProvider,
            _timeProvider,
            Options.Create(new PhotonGeocodingOptions
            {
                Provider = PhotonGeocodingOptions.PhotonProvider,
                Endpoint = new Uri("https://photon.operator.test/"),
                Language = "en",
                CountryCodes = ["BE"],
                DatasetVersion = _datasetVersion,
                SelectionLifetimeSeconds = selectionLifetimeSeconds
            }));

    private AddressSelectionContext CreateContext(Guid tenantId) => new()
    {
        TenantId = tenantId,
        ActorId = ActorId,
        Purpose = AddressSelectionPurpose.CreateLocation,
        Target = new AddressSelectionTarget(),
        ConfigurationFingerprint = _protector.ConfigurationFingerprint
    };
}
