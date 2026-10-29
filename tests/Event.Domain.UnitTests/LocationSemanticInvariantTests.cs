using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;

namespace Event.Domain.UnitTests;

public sealed class LocationSemanticInvariantTests
{
    private static readonly DateTime ChangedAtUtc =
        new(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task ScalarPropertiesRetainValidAssignments()
    {
        var location = new Location
        {
            FullName = "Venue",
            Country = "BE",
            City = "Brussels"
        };

        location.FullName = "Updated venue";
        location.Country = "NL";
        location.City = "Amsterdam";

        await Assert.That(location.FullName).IsEqualTo("Updated venue");
        await Assert.That(location.Country).IsEqualTo("NL");
        await Assert.That(location.City).IsEqualTo("Amsterdam");

    }

    [Test]
    public async Task ScalarSettersRejectNullButAcceptExactErasureTombstoneValues()
    {
        var mutable = NewLocation(Guid.CreateVersion7());
        ArgumentNullException fullNameNull = Assert.Throws<ArgumentNullException>(() => mutable.FullName = null!);
        ArgumentNullException countryNull = Assert.Throws<ArgumentNullException>(() => mutable.Country = null!);
        ArgumentNullException cityNull = Assert.Throws<ArgumentNullException>(() => mutable.City = null!);
        await Assert.That(fullNameNull.ParamName).IsEqualTo("value");
        await Assert.That(countryNull.ParamName).IsEqualTo("value");
        await Assert.That(cityNull.ParamName).IsEqualTo("value");

        Location erased = NewOwnedHome();
        erased.EraseOwnedPii(ChangedAtUtc, LocationPrivacyErasureReasonEnum.OwnerErasureRequest);
        erased.FullName = Location.ErasedPrivateVenueLabel;
        erased.Country = "BE";
        erased.City = string.Empty;

        await Assert.That(erased.FullName).IsEqualTo(Location.ErasedPrivateVenueLabel);
        await Assert.That(erased.Country).IsEqualTo("BE");
        await Assert.That(erased.City).IsEqualTo(string.Empty);
        await Assert.That(erased.DisplaySortKey)
            .IsEqualTo("PRIVATE VENUE");
    }

    [Test]
    public async Task ManualAndProviderUpdatesReplaceAddressAtomicallyAndControlCoordinatePresence()
    {
        var location = NewLocation(Guid.CreateVersion7());
        GeoCoordinate first = GeoCoordinate.Create(50.8503, 4.3517);
        GeoCoordinate second = GeoCoordinate.Create(51.2194, 4.4025);

        bool created = location.SetProviderAddress("Provider one", "1000", first);
        LocationPii attached = location.Pii!;
        bool providerChanged = location.SetProviderAddress("Provider two", "2000", second);

        await Assert.That(created).IsTrue();
        await Assert.That(providerChanged).IsTrue();
        await Assert.That(location.Pii).IsSameReferenceAs(attached);
        await Assert.That(location.Address).IsEqualTo("Provider two");
        await Assert.That(location.Postcode).IsEqualTo("2000");
        await Assert.That(location.GetCoordinate()).IsEqualTo(second);

        bool manualChanged = location.SetManualAddress("Manual", "3000");

        await Assert.That(manualChanged).IsTrue();
        await Assert.That(location.Pii).IsSameReferenceAs(attached);
        await Assert.That(location.Address).IsEqualTo("Manual");
        await Assert.That(location.Postcode).IsEqualTo("3000");
        await Assert.That(location.GetCoordinate()).IsNull();
        await Assert.That(location.LocationPrivacyStateId)
            .IsEqualTo((int)LocationPrivacyStateEnum.Active);
    }

    [Test]
    public async Task SafeAddressDefaultsInvalidateChangedBundlesButExactBundlesAreTrueNoOps()
    {
        Guid actorId = Guid.CreateVersion7();
        Guid organizationId = Guid.CreateVersion7();
        var manual = NewLocation(Guid.CreateVersion7());
        manual.SetManualAddress("Manual", "1000");
        manual.ApplyAddressGovernance(
            actorId,
            LocationAddressSourceEnum.Manual,
            LocationAddressVisibilityEnum.OrganizationScoped,
            organizationId);
        Guid manualStamp = manual.ConcurrencyStamp;
        DateTime? manualUpdatedAt = manual.UpdatedAt;
        Guid? manualUpdatedBy = manual.UpdatedBy;

        manual.SetManualAddress("Manual", "1000");

        await Assert.That(manual.AddressVisibility).IsEqualTo(LocationAddressVisibilityEnum.OrganizationScoped);
        await Assert.That(manual.AddressOrganizationId).IsEqualTo(organizationId);
        await Assert.That(manual.ConcurrencyStamp).IsEqualTo(manualStamp);
        await Assert.That(manual.UpdatedAt).IsEqualTo(manualUpdatedAt);
        await Assert.That(manual.UpdatedBy).IsEqualTo(manualUpdatedBy);

        manual.SetProviderAddress("Provider", "2000", GeoCoordinate.Create(51.2194, 4.4025));
        await Assert.That(manual.AddressSource).IsEqualTo(LocationAddressSourceEnum.ProviderSelection);
        await Assert.That(manual.AddressVisibility).IsEqualTo(LocationAddressVisibilityEnum.Quarantined);
        await Assert.That(manual.AddressOrganizationId).IsNull();

        Guid providerStamp = manual.ConcurrencyStamp;
        manual.SetProviderAddress("Provider", "2000", GeoCoordinate.Create(51.2194, 4.4025));
        await Assert.That(manual.ConcurrencyStamp).IsEqualTo(providerStamp);
    }

    [Test]
    public async Task AddressAndPiiBoundaryGuardsExposeTypedParameterIdentity()
    {
        var location = NewLocation(Guid.CreateVersion7());
        ArgumentNullException nullAddress = Assert.Throws<ArgumentNullException>(() =>
            location.SetManualAddress(null!, "1000"));
        ArgumentNullException nullPostcode = Assert.Throws<ArgumentNullException>(() =>
            location.SetManualAddress("Address", null!));
        ArgumentException emptyAddress = Assert.Throws<ArgumentException>(() =>
            location.SetManualAddress(string.Empty, "1000"));
        ArgumentException emptyPostcode = Assert.Throws<ArgumentException>(() =>
            location.SetManualAddress("Address", "   "));
        ArgumentNullException nullCoordinate = Assert.Throws<ArgumentNullException>(() =>
            location.SetProviderAddress("Address", "1000", null!));

        await Assert.That(nullAddress.ParamName).IsEqualTo("address");
        await Assert.That(nullPostcode.ParamName).IsEqualTo("postcode");
        await Assert.That(emptyAddress.ParamName).IsEqualTo("address");
        await Assert.That(emptyPostcode.ParamName).IsEqualTo("postcode");
        await Assert.That(nullCoordinate.ParamName).IsEqualTo("coordinate");
        await Assert.That(location.Pii).IsNull();
    }

    [Test]
    public async Task EveryAddressMutationEntryPointRejectsErasedLocationsWithoutMutation()
    {
        var location = NewLocation(Guid.CreateVersion7());
        location.ClassifyAsPrivateHome(Guid.CreateVersion7());
        location.SetManualAddress("Original", "1000");
        location.EraseOwnedPii(ChangedAtUtc, LocationPrivacyErasureReasonEnum.OwnerErasureRequest);

        await Assert.That(() => location.SetManualAddress("Manual", "2000"))
            .Throws<InvalidOperationException>();
        await Assert.That(() => location.SetProviderAddress(
                "Provider", "3000", GeoCoordinate.Create(50, 4)))
            .Throws<InvalidOperationException>();

        await Assert.That(location.Pii).IsNull();
        await Assert.That(location.LocationPrivacyStateId)
            .IsEqualTo((int)LocationPrivacyStateEnum.Erased);
    }

    [Test]
    public async Task AddressGovernancePreservesOriginalCreatorAcrossLaterActors()
    {
        Guid creatorId = Guid.CreateVersion7();
        Guid laterActorId = Guid.CreateVersion7();
        var location = NewLocation(Guid.CreateVersion7());

        location.ApplyAddressGovernance(
            creatorId,
            LocationAddressSourceEnum.Manual,
            LocationAddressVisibilityEnum.CreatorPrivate,
            null);
        location.ApplyAddressGovernance(
            laterActorId,
            LocationAddressSourceEnum.ProviderSelection,
            LocationAddressVisibilityEnum.Quarantined,
            null);

        await Assert.That(location.CreatedBy).IsEqualTo(creatorId);
        await Assert.That(location.AddressSource).IsEqualTo(LocationAddressSourceEnum.ProviderSelection);
        await Assert.That(location.AddressVisibility).IsEqualTo(LocationAddressVisibilityEnum.Quarantined);
    }

    [Test]
    public async Task ErasureRequiresEveryRoomToHaveANonEmptyUniqueIdentity()
    {
        var emptyRoomLocation = NewOwnedHome();
        emptyRoomLocation.Rooms.Add(NewRoom(emptyRoomLocation, Guid.Empty));
        var duplicateRoomLocation = NewOwnedHome();
        Guid duplicateId = Guid.CreateVersion7();
        duplicateRoomLocation.Rooms.Add(NewRoom(duplicateRoomLocation, duplicateId));
        duplicateRoomLocation.Rooms.Add(NewRoom(duplicateRoomLocation, duplicateId));

        await Assert.That(() => emptyRoomLocation.EraseOwnedPii(
                ChangedAtUtc, LocationPrivacyErasureReasonEnum.AccountDeletion))
            .Throws<InvalidOperationException>();
        await Assert.That(() => duplicateRoomLocation.EraseOwnedPii(
                ChangedAtUtc, LocationPrivacyErasureReasonEnum.AccountDeletion))
            .Throws<InvalidOperationException>();

        await Assert.That(emptyRoomLocation.Pii).IsNotNull();
        await Assert.That(duplicateRoomLocation.Pii).IsNotNull();
        await Assert.That(emptyRoomLocation.LocationPrivacyStateId)
            .IsEqualTo((int)LocationPrivacyStateEnum.Active);
        await Assert.That(duplicateRoomLocation.LocationPrivacyStateId)
            .IsEqualTo((int)LocationPrivacyStateEnum.Active);
    }

    private static Location NewLocation(Guid id) => new()
    {
        Id = id,
        TenantId = Guid.CreateVersion7(),
        FullName = "Venue",
        Country = "BE",
        City = "Brussels"
    };

    private static Location NewOwnedHome()
    {
        var location = NewLocation(Guid.CreateVersion7());
        location.ClassifyAsPrivateHome(Guid.CreateVersion7());
        location.SetManualAddress("Private address", "1000");
        return location;
    }

    private static LocationRoom NewRoom(Location location, Guid id) => new()
    {
        Id = id,
        LocationId = location.Id,
        Location = location,
        Name = "Room",
        TenantId = location.TenantId,
        Tenant = null!
    };

}
