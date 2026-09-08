using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Explore.Infrastructure.Tests.Geocoding;

[NotInParallel("PhotonContract")]
public sealed class PhotonSelectionTokenProtectionContractTests
{
    private static readonly Guid TenantA = Guid.Parse("019d2f3b-a22f-7c74-a7c4-790796a24f31");
    private static readonly Guid TenantB = Guid.Parse("019d2f3b-a22f-77f7-93c4-c6778af4c652");
    private static readonly DateTimeOffset InitialTime =
        new(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task ProtectAndUnprotect_RoundTripsNormalizedSelectionWithPurposeVersionAndTenantBinding()
    {
        var recordingProvider = new PhotonRecordingDataProtectionProvider(new EphemeralDataProtectionProvider());
        using var host = CreateHost(recordingProvider, new PhotonManualTimeProvider(InitialTime));
        object selection = host.CreateSelection();

        string token = host.Protect(selection, TenantA, TimeSpan.FromMinutes(5));
        PhotonSuggestionView roundTrip = PhotonSuggestionView.From(host.Unprotect(token, TenantA));

        await Assert.That(host.CurrentVersion).IsEqualTo(1);
        await Assert.That(roundTrip.DisplayName).IsEqualTo("Display Canary");
        await Assert.That(roundTrip.Address).IsEqualTo("Address Canary 30");
        await Assert.That(roundTrip.Latitude).IsEqualTo(50.8503);
        await Assert.That(roundTrip.Longitude).IsEqualTo(4.3517);
        string purposeChain = string.Join('|', recordingProvider.Purposes);
        await Assert.That(purposeChain).Contains("ISLAMU.Geocoding.AddressSelection");
        await Assert.That(purposeChain).Contains("v1");
        await Assert.That(purposeChain).Contains(TenantA.ToString("D"));
    }

    [Test]
    public async Task Unprotect_WhenTokenIsTampered_FailsClosed()
    {
        using var host = CreateHost(
            new EphemeralDataProtectionProvider(),
            new PhotonManualTimeProvider(InitialTime));
        string token = host.Protect(host.CreateSelection(), TenantA, TimeSpan.FromMinutes(5));
        int index = token.Length / 2;
        char[] tamperedCharacters = token.ToCharArray();
        tamperedCharacters[index] = tamperedCharacters[index] == 'A' ? 'B' : 'A';
        string tampered = new(tamperedCharacters);

        await Assert.That(() => host.Unprotect(tampered, TenantA)).Throws<CryptographicException>();
    }

    [Test]
    public async Task Unprotect_WhenTrustedTenantDiffers_FailsClosed()
    {
        using var host = CreateHost(
            new EphemeralDataProtectionProvider(),
            new PhotonManualTimeProvider(InitialTime));
        string token = host.Protect(host.CreateSelection(), TenantA, TimeSpan.FromMinutes(5));

        await Assert.That(() => host.Unprotect(token, TenantB)).Throws<CryptographicException>();
    }

    [Test]
    public async Task Unprotect_WhenLifetimeExpiresByInjectedClock_FailsClosedWithoutRealTime()
    {
        var time = new PhotonManualTimeProvider(InitialTime);
        using var host = CreateHost(new EphemeralDataProtectionProvider(), time);
        string token = host.Protect(host.CreateSelection(), TenantA, TimeSpan.FromMinutes(5));

        time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromTicks(1));

        await Assert.That(() => host.Unprotect(token, TenantA)).Throws<CryptographicException>();
    }

    [Test]
    public async Task Unprotect_TokenFromDifferentVersionPurpose_FailsClosed()
    {
        var provider = new EphemeralDataProtectionProvider();
        using var host = CreateHost(provider, new PhotonManualTimeProvider(InitialTime));
        string incompatibleToken = provider
            .CreateProtector("ISLAMU.Geocoding.AddressSelection", "v2", TenantA.ToString("D"))
            .Protect("incompatible-envelope");

        await Assert.That(() => host.Unprotect(incompatibleToken, TenantA)).Throws<CryptographicException>();
    }

    [Test]
    public async Task PersistedKeyRing_FreshProtectorUnprotectsExistingSelectionToken()
    {
        DirectoryInfo keyDirectory = Directory.CreateTempSubdirectory("photon-selection-keys-");
        try
        {
            string token;
            var firstProvider = DataProtectionProvider.Create(
                keyDirectory,
                builder => builder.SetApplicationName("islamu-event-photon-contract"));
            using (var first = CreateHost(firstProvider, new PhotonManualTimeProvider(InitialTime)))
            {
                token = first.Protect(first.CreateSelection(), TenantA, TimeSpan.FromMinutes(10));
            }

            var freshProvider = DataProtectionProvider.Create(
                keyDirectory,
                builder => builder.SetApplicationName("islamu-event-photon-contract"));
            using var fresh = CreateHost(freshProvider, new PhotonManualTimeProvider(InitialTime));
            PhotonSuggestionView roundTrip = PhotonSuggestionView.From(fresh.Unprotect(token, TenantA));

            await Assert.That(roundTrip.ProviderRecordId).IsEqualTo("provider-record-canary");
            await Assert.That(roundTrip.Provider).IsEqualTo("Photon");
        }
        finally
        {
            keyDirectory.Delete(recursive: true);
        }
    }

    [Test]
    public async Task DatasetVersionChangeInvalidatesExistingSelectionToken()
    {
        var provider = new EphemeralDataProtectionProvider();
        string token;
        using (var first = new PhotonSelectionTokenContractHost(
            provider,
            new PhotonManualTimeProvider(InitialTime),
            new PhotonObservabilityCapture(),
            "dataset-v1"))
        {
            token = first.Protect(
                first.CreateSelection(),
                TenantA,
                TimeSpan.FromMinutes(5));
        }

        using var refreshed = new PhotonSelectionTokenContractHost(
            provider,
            new PhotonManualTimeProvider(InitialTime),
            new PhotonObservabilityCapture(),
            "dataset-v2");

        await Assert.That(() => refreshed.Unprotect(token, TenantA))
            .Throws<CryptographicException>();
    }

    [Test]
    public async Task ProtectAndUnprotect_EmitNoSelectionTokenOrIdentityTelemetry()
    {
        var observability = new PhotonObservabilityCapture();
        using var host = new PhotonSelectionTokenContractHost(
            new EphemeralDataProtectionProvider(),
            new PhotonManualTimeProvider(InitialTime),
            observability);
        object selection = host.CreateSelection();

        string token = host.Protect(selection, TenantA, TimeSpan.FromMinutes(5));
        _ = host.Unprotect(token, TenantA);

        string observable = observability.ObservableText();
        string[] forbidden =
        [
            "Display Canary", "Address Canary 30", "PII-1040", "50.8503", "4.3517",
            "provider-record-canary", TenantA.ToString("D"), token
        ];
        foreach (string value in forbidden)
        {
            await Assert.That(observable).DoesNotContain(value);
        }
    }

    private static PhotonSelectionTokenContractHost CreateHost(
        IDataProtectionProvider provider,
        PhotonManualTimeProvider time) =>
        new(provider, time, new PhotonObservabilityCapture());
}
