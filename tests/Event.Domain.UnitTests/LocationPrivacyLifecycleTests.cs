using Explore.Domain.Enums;

namespace Event.Domain.UnitTests;

[Category("EventLocationPrivacy")]
public sealed class LocationPrivacyLifecycleTests
{
    [Test]
    public async Task LocationWithoutPiiIsNullSafeAndNotProvided()
    {
        var location = CreateLocation();

        await Assert.That(location.Pii).IsNull();
        await Assert.That(location.Address).IsNull();
        await Assert.That(location.Postcode).IsNull();
        await Assert.That(location.GetCoordinate()).IsNull();
        await Assert.That(location.LocationPrivacyStateId)
            .IsEqualTo((int)LocationPrivacyStateEnum.NotProvided);
    }

    [Test]
    public async Task ManualAddressTransitionChangesNotProvidedLocationToActive()
    {
        var location = CreateLocation();

        location.SetManualAddress("1 Main Street", "1000");

        await Assert.That(location.Pii).IsNotNull();
        await Assert.That(location.Address).IsEqualTo("1 Main Street");
        await Assert.That(location.LocationPrivacyStateId)
            .IsEqualTo((int)LocationPrivacyStateEnum.Active);
    }

    [Test]
    public async Task ActivePiiCannotBeSilentlyClearedOrDeclassified()
    {
        var location = CreateLocation();
        location.ClassifyAsPrivateHome(Guid.CreateVersion7());
        location.SetManualAddress("1 Main Street", "1000");

        await Assert.That(typeof(Location).GetProperty(nameof(Location.Pii))?.SetMethod?.IsPublic == true)
            .IsFalse();
        await Assert.That(() => location.ClassifyAs(LocationKindEnum.CommercialVenue))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task PrivateHomeDefaultsOwnershipToCurrentUserAndRequiresConsentToTransfer()
    {
        var currentUserId = Guid.CreateVersion7();
        var newOwnerId = Guid.CreateVersion7();
        var location = CreateLocation();
        location.ClassifyAsPrivateHome(currentUserId);

        await Assert.That(location.LocationKindId).IsEqualTo((int)LocationKindEnum.PrivateHome);
        await Assert.That(location.OwnerUserId).IsEqualTo(currentUserId);

        await Assert.That(() => location.TransferPrivateHomeOwnership(
                new LocationOwnershipConsent(newOwnerId, currentUserId, DomainTestClock.UtcNow, "owner-transfer-v1")))
            .Throws<ArgumentException>();

        location.TransferPrivateHomeOwnership(
            new LocationOwnershipConsent(newOwnerId, newOwnerId, DomainTestClock.UtcNow, "owner-transfer-v1"));

        await Assert.That(location.OwnerUserId).IsEqualTo(newOwnerId);
    }

    [Test]
    public async Task ReclassifyingPrivateHomeCannotSilentlyReplaceItsOwner()
    {
        var ownerId = Guid.CreateVersion7();
        var attemptedOwnerId = Guid.CreateVersion7();
        var location = CreateLocation();
        location.ClassifyAsPrivateHome(ownerId);

        location.ClassifyAsPrivateHome(ownerId);

        await Assert.That(location.OwnerUserId).IsEqualTo(ownerId);
        await Assert.That(() => location.ClassifyAsPrivateHome(attemptedOwnerId))
            .Throws<InvalidOperationException>();
        await Assert.That(location.OwnerUserId).IsEqualTo(ownerId);
    }

    [Test]
    public async Task PublicPiiAttachmentBypassIsUnavailableAndAggregateAssociatesItsChild()
    {
        var location = CreateLocation();

        await Assert.That(typeof(Location).GetMethod("AttachPii", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
            .IsNull();
        location.SetManualAddress("1 Main Street", "1000");
        await Assert.That(location.Pii?.LocationId).IsEqualTo(location.Id);
        await Assert.That(location.Pii?.Location).IsSameReferenceAs(location);
    }

    [Test]
    public async Task NonHomeClassificationNeverRetainsAnOwner()
    {
        var location = CreateLocation();
        location.ClassifyAsPrivateHome(Guid.CreateVersion7());

        location.ClassifyAs(LocationKindEnum.CommercialVenue);

        await Assert.That(location.OwnerUserId).IsNull();
    }

    [Test]
    public async Task EraseOwnedPiiIsIrreversibleAndTombstonesIdentifyingRoomDataUniquely()
    {
        var ownerId = Guid.CreateVersion7();
        var erasedAt = new DateTime(2026, 7, 16, 12, 0, 0, DateTimeKind.Utc);
        var location = CreateLocation();
        location.ClassifyAsPrivateHome(ownerId);
        location.SetManualAddress("9 Household Lane", "1000");
        var roomOne = CreateRoom(location, "Amir's office");
        var roomTwo = CreateRoom(location, "Family bedroom");
        location.Rooms.Add(roomOne);
        location.Rooms.Add(roomTwo);

        location.EraseOwnedPii(erasedAt, LocationPrivacyErasureReasonEnum.AccountDeletion);

        await Assert.That(location.LocationPrivacyStateId)
            .IsEqualTo((int)LocationPrivacyStateEnum.Erased);
        await Assert.That(location.Pii).IsNull();
        await Assert.That(location.OwnerUserId).IsNull();
        await Assert.That(location.PiiErasedAtUtc).IsEqualTo(erasedAt);
        await Assert.That(location.PiiErasureReason).IsEqualTo(LocationPrivacyErasureReasonEnum.AccountDeletion);
        await Assert.That(location.FullName).IsEqualTo(Location.ErasedPrivateVenueLabel);
        await Assert.That(location.City).IsEqualTo(string.Empty);
        await Assert.That(roomOne.Name).IsNotEqualTo(roomTwo.Name);
        await Assert.That(roomOne.Name).DoesNotContain("Amir");
        await Assert.That(roomTwo.Name).DoesNotContain("Family");
        await Assert.That(roomOne.Description).IsNull();
        await Assert.That(roomTwo.Description).IsNull();
        await Assert.That(roomOne.IsDeleted).IsTrue();
        await Assert.That(roomTwo.IsDeleted).IsTrue();

        await Assert.That(() => location.SetManualAddress("10 Replacement Lane", "1000"))
            .Throws<InvalidOperationException>();
        await Assert.That(() => location.ClassifyAs(LocationKindEnum.CommercialVenue))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ErasedLocationAndRoomRejectDirectPropertyResurrection()
    {
        var location = CreateLocation();
        location.ClassifyAsPrivateHome(Guid.CreateVersion7());
        location.SetManualAddress("9 Household Lane", "1000");
        var room = CreateRoom(location, "Family bedroom");
        room.Slug = "family-bedroom";
        location.Rooms.Add(room);
        location.EraseOwnedPii(DomainTestClock.UtcNow, LocationPrivacyErasureReasonEnum.AccountDeletion);
        var tombstoneName = room.Name;

        await Assert.That(() => location.FullName = "Resurrected home")
            .Throws<InvalidOperationException>();
        await Assert.That(() => location.Country = "NL")
            .Throws<InvalidOperationException>();
        await Assert.That(() => location.City = "Amsterdam")
            .Throws<InvalidOperationException>();
        await Assert.That(() => room.Name = "Family bedroom")
            .Throws<InvalidOperationException>();
        await Assert.That(() => room.Slug = "family-bedroom")
            .Throws<InvalidOperationException>();
        await Assert.That(() => room.Description = "Restored description")
            .Throws<InvalidOperationException>();
        await Assert.That(() => room.IsDeleted = false)
            .Throws<InvalidOperationException>();

        await Assert.That(location.FullName).IsEqualTo(Location.ErasedPrivateVenueLabel);
        await Assert.That(location.Country).IsEqualTo("BE");
        await Assert.That(location.City).IsEqualTo(string.Empty);
        await Assert.That(room.Name).IsEqualTo(tombstoneName);
        await Assert.That(room.Slug).IsNull();
        await Assert.That(room.Description).IsNull();
        await Assert.That(room.IsDeleted).IsTrue();
    }

    [Test]
    public async Task ReplacementAddressUsesFreshLocationAndFreshRoomRecords()
    {
        var ownerId = Guid.CreateVersion7();
        var erased = CreateLocation();
        erased.ClassifyAsPrivateHome(ownerId);
        erased.SetManualAddress("Old address", "1000");
        var erasedRoom = CreateRoom(erased, "Study");
        erased.Rooms.Add(erasedRoom);
        erased.EraseOwnedPii(DomainTestClock.UtcNow, LocationPrivacyErasureReasonEnum.OwnerErasureRequest);

        var replacement = CreateLocation();
        replacement.ClassifyAsPrivateHome(ownerId);
        replacement.SetManualAddress("New address", "1000");
        var replacementRoom = CreateRoom(replacement, "Study");
        replacement.Rooms.Add(replacementRoom);

        await Assert.That(replacement.Id).IsNotEqualTo(erased.Id);
        await Assert.That(replacementRoom.Id).IsNotEqualTo(erasedRoom.Id);
        await Assert.That(replacementRoom.Name).IsEqualTo("Study");
        await Assert.That(replacement.LocationPrivacyStateId)
            .IsEqualTo((int)LocationPrivacyStateEnum.Active);
    }

    private static Location CreateLocation()
    {
        var tenant = new Tenant
        {
            FullName = "Privacy test tenant",
            Slug = $"privacy-test-{Guid.CreateVersion7():N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = new TenantStatus
            {
                Id = (int)TenantStatusEnum.Active,
                MasterCode = "ACTIVE",
                FullName = "Active",
                IsActiveState = true
            }
        };
        return new Location
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            FullName = "Venue",
            Country = "BE",
            City = "Brussels",
            Tenant = tenant
        };
    }

    private static LocationRoom CreateRoom(Location location, string name) => new()
    {
        Id = Guid.CreateVersion7(),
        LocationId = location.Id,
        Location = location,
        Name = name,
        Description = $"Description for {name}",
        Tenant = location.Tenant ?? throw new InvalidOperationException("The Location fixture requires a tenant.")
    };
}
