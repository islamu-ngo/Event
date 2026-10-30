using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;

namespace Event.Domain.UnitTests;

[Category("EventLocationPrivacy")]
public sealed class LocationCoordinateInvariantTests
{
    [Test]
    public async Task AddressTransitionsAcceptAbsentOrCompleteExactCoordinates()
    {
        GeoCoordinate coordinate = GeoCoordinate.Create(50.850_300_000_000_01, 4.351_700_000_000_001);

        Location manual = CreateLocation();
        manual.SetManualAddress("Rue Manual 20", "1000");
        Location provider = CreateLocation();
        provider.SetProviderAddress("Rue Provider 30", "1000", coordinate);
        LocationPii absent = manual.Pii!;
        LocationPii exact = provider.Pii!;

        await Assert.That(absent.GetCoordinate()).IsNull();
        await Assert.That(absent.Latitude).IsNull();
        await Assert.That(absent.Longitude).IsNull();
        await Assert.That(exact.GetCoordinate()).IsEqualTo(coordinate);
        await Assert.That(exact.Latitude).IsEqualTo(coordinate.Latitude);
        await Assert.That(exact.Longitude).IsEqualTo(coordinate.Longitude);
        await Assert.That(() => provider.SetProviderAddress("Incomplete", "2000", null!))
            .Throws<ArgumentNullException>();
        await Assert.That(provider.Address).IsEqualTo("Rue Provider 30");
        await Assert.That(provider.Postcode).IsEqualTo("1000");
        await Assert.That(provider.GetCoordinate()).IsEqualTo(coordinate);
    }

    [Test]
    public async Task ProviderAddressTransitionPreservesExactValidatedCoordinateWithoutClamping()
    {
        var location = CreateLocation();
        GeoCoordinate coordinate = GeoCoordinate.Create(50.850_300_000_000_01, 4.351_700_000_000_001);

        location.SetProviderAddress("Rue Provider 30", "1000", coordinate);

        await Assert.That(location.GetCoordinate()).IsEqualTo(coordinate);
    }

    [Test]
    public async Task InvalidCoordinateCannotEnterOwnerTransition()
    {
        var location = CreateLocation();
        location.SetProviderAddress("Existing address", "1000", GeoCoordinate.Create(1.25, 2.5));

        await Assert.That(() => GeoCoordinate.Create(double.NaN, 2.5))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => GeoCoordinate.Create(91, 2.5))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(location.GetCoordinate()).IsEqualTo(GeoCoordinate.Create(1.25, 2.5));
    }

    [Test]
    public async Task ManualAddressReplacementClearsStaleCoordinates()
    {
        var location = CreateLocation();
        location.SetProviderAddress("Provider address", "1000", GeoCoordinate.Create(50.8503, 4.3517));

        location.SetManualAddress("Manual address", "2000");

        await Assert.That(location.Address).IsEqualTo("Manual address");
        await Assert.That(location.Postcode).IsEqualTo("2000");
        await Assert.That(location.GetCoordinate()).IsNull();
    }

    [Test]
    public async Task ErasureNullsCoordinatesAndBlocksTheirResurrection()
    {
        var location = CreateLocation();
        location.ClassifyAsPrivateHome(Guid.CreateVersion7());
        location.SetProviderAddress("Private address", "1000", GeoCoordinate.Create(50.8503, 4.3517));

        location.EraseOwnedPii(DomainTestClock.UtcNow, LocationPrivacyErasureReasonEnum.OwnerErasureRequest);

        await Assert.That(location.Pii).IsNull();
        await Assert.That(location.GetCoordinate()).IsNull();
        await Assert.That(() => location.SetProviderAddress(
                "Resurrected address",
                "9999",
                GeoCoordinate.Create(1, 2)))
            .Throws<InvalidOperationException>();
        await Assert.That(location.Pii).IsNull();
    }

    [Test]
    public async Task PiiFormattingRedactsAddressPostcodeAndCoordinate()
    {
        Location location = CreateLocation();
        location.SetProviderAddress(
            "17 Confidential Crescent",
            "SECRET-1040",
            GeoCoordinate.Create(50.84673, 4.35247));

        string formatted = location.Pii!.ToString();

        await Assert.That(formatted).IsEqualTo("LocationPii[redacted]");
        await Assert.That(formatted).DoesNotContain("Confidential");
        await Assert.That(formatted).DoesNotContain("1040");
        await Assert.That(formatted).DoesNotContain("50.84673");
    }

    private static Location CreateLocation() => new()
    {
        Id = Guid.CreateVersion7(),
        Tenant = null!,
        FullName = "Venue",
        Country = "BE",
        City = "Brussels"
    };
}
