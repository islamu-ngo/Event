// ABOUTME: Verifies canonical EventLocation placement invariants and fail-closed disclosure defaults.
// ABOUTME: Covers TBA publication, XOR, soft-delete freshness, carrier consistency, and policy identity.

using Explore.Domain.Enums;

namespace Event.Domain.UnitTests;

[Category("EventLocationPrivacy")]
public sealed class EventLocationTests
{
    [Test]
    public async Task PhysicalPlacementHasPrivateLocationIdentityAndFailClosedDefaults()
    {
        var actorId = Guid.CreateVersion7();
        var createdAt = new DateTime(2026, 7, 16, 10, 30, 0, DateTimeKind.Utc);
        var placement = EventLocation.CreatePhysical(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            actorId,
            createdAt);

        await Assert.That(placement.Id.Version).IsEqualTo(7);
        await Assert.That(placement.ConcurrencyStamp.Version).IsEqualTo(7);
        await Assert.That(placement.LocationId).IsNotNull();
        await Assert.That(placement.IsToBeAnnounced).IsFalse();
        await Assert.That(placement.ShowVenueName).IsFalse();
        await Assert.That(placement.ShowCity).IsFalse();
        await Assert.That(placement.ShowCountry).IsFalse();
        await Assert.That(placement.ShowRoomName).IsFalse();
        await Assert.That(placement.ShowStreetAddress).IsFalse();
        await Assert.That(placement.ShowPostcode).IsFalse();
        await Assert.That(placement.ShowCoordinates).IsFalse();
        await Assert.That(placement.FullDetailsAudienceId)
            .IsEqualTo((int)LocationDisclosureAudienceEnum.Never);
        await Assert.That(placement.RevealFullDetailsFromUtc).IsNull();
        await Assert.That(placement.NeedsPrivacyReview).IsTrue();
        await Assert.That(placement.PolicyVersion).IsEqualTo(1);
        await Assert.That(placement.LastPolicyActorUserId).IsEqualTo(actorId);
        await Assert.That(placement.LastPolicyChangedAtUtc).IsEqualTo(createdAt);
        await Assert.That(placement.CreatedAt).IsEqualTo(createdAt);
        await Assert.That(placement.CreatedBy).IsEqualTo(actorId);
        await Assert.That(placement.UpdatedAt).IsNull();
        await Assert.That(placement.UpdatedBy).IsNull();
        await Assert.That(placement.IsDeleted).IsFalse();
        await Assert.That(placement.DeletedAt).IsNull();
        await Assert.That(placement.DeletedBy).IsNull();
    }

    [Test]
    public async Task TenantIdentityCannotBeRetargetedThroughItsInterface()
    {
        var tenantId = Guid.CreateVersion7();
        var placement = EventLocation.CreateToBeAnnounced(
            tenantId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            DomainTestClock.UtcNow);

        await Assert.That(typeof(EventLocation).GetProperty(nameof(EventLocation.TenantId))!.SetMethod!.IsPrivate)
            .IsTrue();
        await Assert.That(() => ((ITenantEntity)placement).TenantId = Guid.CreateVersion7())
            .Throws<InvalidOperationException>();
        await Assert.That(placement.TenantId).IsEqualTo(tenantId);
    }

    [Test]
    public async Task TbaPlacementSatisfiesExclusiveOrWithoutPhysicalLocation()
    {
        var placement = EventLocation.CreateToBeAnnounced(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            DomainTestClock.UtcNow);

        await Assert.That(placement.LocationId).IsNull();
        await Assert.That(placement.IsToBeAnnounced).IsTrue();
        await Assert.That(placement.HasValidLocationOrTbaShape).IsTrue();
        await Assert.That(typeof(EventLocation).GetProperty(nameof(EventLocation.IsToBeAnnounced))!.SetMethod!.IsPrivate)
            .IsTrue();
    }

    [Test]
    public async Task ExplicitTbaSatisfiesPublicationWithoutPhysicalDataAndSuppressesEveryField()
    {
        var placement = EventLocation.CreateToBeAnnounced(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            DomainTestClock.UtcNow);

        await Assert.That(placement.SatisfiesPublicationVenueRequirement(null)).IsTrue();
        await Assert.That(placement.ShowVenueName).IsFalse();
        await Assert.That(placement.ShowCity).IsFalse();
        await Assert.That(placement.ShowCountry).IsFalse();
        await Assert.That(placement.ShowRoomName).IsFalse();
        await Assert.That(placement.ShowStreetAddress).IsFalse();
        await Assert.That(placement.ShowPostcode).IsFalse();
        await Assert.That(placement.ShowCoordinates).IsFalse();
    }

    [Test]
    public async Task ActiveMatchingPhysicalLocationSatisfiesPublicationVenueRequirement()
    {
        var tenantId = Guid.CreateVersion7();
        var location = CreatePhysicalLocation(tenantId);
        location.SetManualAddress("1 Main Street", "1000");
        var placement = EventLocation.CreatePhysical(
            tenantId,
            Guid.CreateVersion7(),
            location.Id,
            Guid.CreateVersion7(),
            DomainTestClock.UtcNow);

        await Assert.That(placement.SatisfiesPublicationVenueRequirement(location)).IsTrue();
    }

    [Test]
    public async Task EmptyOrWhitespaceLegacyPhysicalAddressPartsBlockPublication()
    {
        var tenantId = Guid.CreateVersion7();
        var location = CreatePhysicalLocation(tenantId);
        location.SetManualAddress("1 Main Street", "1000");
        var placement = EventLocation.CreatePhysical(
            tenantId,
            Guid.CreateVersion7(),
            location.Id,
            Guid.CreateVersion7(),
            DomainTestClock.UtcNow);

        MaterializeLegacyPii(location, string.Empty, "1000");
        await Assert.That(placement.SatisfiesPublicationVenueRequirement(location)).IsFalse();
        MaterializeLegacyPii(location, "   ", "1000");
        await Assert.That(placement.SatisfiesPublicationVenueRequirement(location)).IsFalse();
        MaterializeLegacyPii(location, "1 Main Street", string.Empty);
        await Assert.That(placement.SatisfiesPublicationVenueRequirement(location)).IsFalse();
        MaterializeLegacyPii(location, "1 Main Street", "   ");
        await Assert.That(placement.SatisfiesPublicationVenueRequirement(location)).IsFalse();
    }

    [Test]
    public async Task MissingNotProvidedAndMismatchedPhysicalLocationsBlockPublication()
    {
        var tenantId = Guid.CreateVersion7();
        var location = CreatePhysicalLocation(tenantId);
        var placement = EventLocation.CreatePhysical(
            tenantId,
            Guid.CreateVersion7(),
            location.Id,
            Guid.CreateVersion7(),
            DomainTestClock.UtcNow);

        await Assert.That(placement.SatisfiesPublicationVenueRequirement(null)).IsFalse();
        await Assert.That(placement.SatisfiesPublicationVenueRequirement(location)).IsFalse();

        location.SetManualAddress("1 Main Street", "1000");
        var anotherLocation = CreatePhysicalLocation(tenantId);
        anotherLocation.SetManualAddress("2 Main Street", "1000");
        await Assert.That(placement.SatisfiesPublicationVenueRequirement(anotherLocation)).IsFalse();

        location.TenantId = Guid.CreateVersion7();
        await Assert.That(placement.SatisfiesPublicationVenueRequirement(location)).IsFalse();
    }

    [Test]
    public async Task ErasureBlocksPhysicalPublicationAndNeverInfersTba()
    {
        var tenantId = Guid.CreateVersion7();
        var location = CreatePhysicalLocation(tenantId);
        location.ClassifyAsPrivateHome(Guid.CreateVersion7());
        location.SetManualAddress("9 Household Lane", "1000");
        var placement = EventLocation.CreatePhysical(
            tenantId,
            Guid.CreateVersion7(),
            location.Id,
            Guid.CreateVersion7(),
            DomainTestClock.UtcNow);

        location.EraseOwnedPii(DomainTestClock.UtcNow, LocationPrivacyErasureReasonEnum.AccountDeletion);

        await Assert.That(placement.SatisfiesPublicationVenueRequirement(location)).IsFalse();
        await Assert.That(placement.IsToBeAnnounced).IsFalse();
        await Assert.That(placement.LocationId).IsEqualTo(location.Id);
        await Assert.That(placement.HasValidLocationOrTbaShape).IsTrue();
    }

    [Test]
    public async Task LocationKindNeverChangesDisclosureDefaults()
    {
        var location = new Location
        {
            Id = Guid.CreateVersion7(),
            FullName = "Venue",
            Country = "BE",
            City = "Brussels",
            Tenant = null!
        };
        location.ClassifyAsPrivateHome(Guid.CreateVersion7());

        var placement = EventLocation.CreatePhysical(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            location.Id,
            Guid.CreateVersion7(),
            DomainTestClock.UtcNow);

        await Assert.That(location.LocationKindId).IsEqualTo((int)LocationKindEnum.PrivateHome);
        await Assert.That(placement.FullDetailsAudienceId)
            .IsEqualTo((int)LocationDisclosureAudienceEnum.Never);
        await Assert.That(placement.ShowCountry).IsFalse();
    }

    [Test]
    public async Task FinalDetachIsTerminalAndReattachCreatesFreshFailClosedPlacement()
    {
        var tenantId = Guid.CreateVersion7();
        var eventId = Guid.CreateVersion7();
        var locationId = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7();
        var first = EventLocation.CreatePhysical(tenantId, eventId, locationId, actorId, DomainTestClock.UtcNow);

        first.DetachFinalReference(actorId, DomainTestClock.UtcNow);
        var replacement = EventLocation.CreatePhysical(tenantId, eventId, locationId, actorId, DomainTestClock.UtcNow);

        await Assert.That(first.IsDeleted).IsTrue();
        await Assert.That(replacement.Id).IsNotEqualTo(first.Id);
        await Assert.That(replacement.IsDeleted).IsFalse();
        await Assert.That(replacement.FullDetailsAudienceId)
            .IsEqualTo((int)LocationDisclosureAudienceEnum.Never);
        await Assert.That(typeof(EventLocation).GetProperty(nameof(EventLocation.LocationId))!.SetMethod!.IsPrivate)
            .IsTrue();
        await Assert.That(() => ((ISoftDeletable)first).IsDeleted = false)
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task SamePhysicalRoomAcrossEventsUsesDistinctEventLocationAuthority()
    {
        var tenantId = Guid.CreateVersion7();
        var locationId = Guid.CreateVersion7();
        var roomId = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7();
        var eventOneId = Guid.CreateVersion7();
        var eventTwoId = Guid.CreateVersion7();
        var placementOne = EventLocation.CreatePhysical(tenantId, eventOneId, locationId, actorId, DomainTestClock.UtcNow);
        var placementTwo = EventLocation.CreatePhysical(tenantId, eventTwoId, locationId, actorId, DomainTestClock.UtcNow);
        var sessionOne = CreateSession(tenantId, eventOneId, roomId);
        var sessionTwo = CreateSession(tenantId, eventTwoId, roomId);
        sessionOne.LocationId = locationId;
        sessionTwo.LocationId = locationId;

        sessionOne.AssignEventLocation(placementOne);
        sessionTwo.AssignEventLocation(placementTwo);

        await Assert.That(sessionOne.LocationId).IsEqualTo(locationId);
        await Assert.That(sessionTwo.LocationId).IsEqualTo(locationId);
        await Assert.That(sessionOne.RoomId).IsEqualTo(roomId);
        await Assert.That(sessionTwo.RoomId).IsEqualTo(roomId);
        await Assert.That(sessionOne.EventLocationId).IsNotEqualTo(sessionTwo.EventLocationId);
    }

    [Test]
    public async Task SwitchingPhysicalLocationsClearsAStaleRoomReference()
    {
        var tenantId = Guid.CreateVersion7();
        var eventId = Guid.CreateVersion7();
        var priorLocationId = Guid.CreateVersion7();
        var session = CreateSession(tenantId, eventId, Guid.CreateVersion7());
        session.LocationId = priorLocationId;
        var replacement = EventLocation.CreatePhysical(
            tenantId,
            eventId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            DomainTestClock.UtcNow);

        session.AssignEventLocation(replacement);

        await Assert.That(session.LocationId).IsEqualTo(replacement.LocationId);
        await Assert.That(session.RoomId).IsNull();
        await Assert.That(session.Room).IsNull();
    }

    [Test]
    public async Task EveryCarrierDerivesInternalPhysicalIdFromMatchingEventLocation()
    {
        var tenantId = Guid.CreateVersion7();
        var eventId = Guid.CreateVersion7();
        var locationId = Guid.CreateVersion7();
        var placement = EventLocation.CreatePhysical(
            tenantId,
            eventId,
            locationId,
            Guid.CreateVersion7(),
            DomainTestClock.UtcNow);
        var session = CreateSession(tenantId, eventId, null);
        var group = new EventSessionGroup { EventId = eventId, Event = null!, TenantId = tenantId, Tenant = null!, Name = "Track" };
        var agenda = new EventAgendaItem { EventId = eventId, Event = null!, TenantId = tenantId, Tenant = null!, Title = "Break" };
        var sessionAgenda = new EventSessionAgendaItem
        {
            EventSessionId = session.Id,
            EventSession = session,
            TenantId = tenantId,
            Tenant = null!,
            Title = "Segment"
        };

        session.AssignEventLocation(placement);
        group.AssignEventLocation(placement);
        agenda.AssignEventLocation(placement);
        sessionAgenda.AssignEventLocation(placement);

        await Assert.That(session.EventLocationId).IsEqualTo(placement.Id);
        await Assert.That(group.EventLocationId).IsEqualTo(placement.Id);
        await Assert.That(agenda.EventLocationId).IsEqualTo(placement.Id);
        await Assert.That(sessionAgenda.EventLocationId).IsEqualTo(placement.Id);
        await Assert.That(session.LocationId).IsEqualTo(locationId);
        await Assert.That(group.LocationId).IsEqualTo(locationId);
        await Assert.That(agenda.LocationId).IsEqualTo(locationId);
        await Assert.That(sessionAgenda.LocationId).IsEqualTo(locationId);
    }

    [Test]
    [Arguments("Session")]
    [Arguments("Group")]
    [Arguments("Agenda")]
    public async Task DeletionDetachClearsEveryPhysicalReferenceWithoutChangingIdentity(string carrier)
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        Location location = CreatePhysicalLocation(tenantId);
        var room = new LocationRoom { Id = Guid.CreateVersion7(), Name = "Room", LocationId = location.Id, Location = location, TenantId = tenantId, Tenant = null! };
        EventLocation placement = EventLocation.CreatePhysical(tenantId, eventId, location.Id,
            Guid.CreateVersion7(), DomainTestClock.UtcNow);
        object?[] references;
        Guid retainedEventId;
        Guid retainedTenantId;
        switch (carrier)
        {
            case "Session":
                EventSession session = CreateSession(tenantId, eventId, null);
                session.AssignEventLocation(placement);
                session.Location = location;
                session.RoomId = room.Id;
                session.Room = room;
                session.DetachEventLocationForDeletion();
                session.DetachEventLocationForDeletion();
                references = [session.EventLocationId, session.EventLocation, session.LocationId, session.Location, session.RoomId, session.Room];
                (retainedEventId, retainedTenantId) = (session.EventId, session.TenantId);
                break;
            case "Group":
                var group = new EventSessionGroup { EventId = eventId, Event = null!, TenantId = tenantId, Tenant = null!, Name = "Track" };
                group.AssignEventLocation(placement);
                group.Location = location;
                group.RoomId = room.Id;
                group.Room = room;
                group.DetachEventLocationForDeletion();
                group.DetachEventLocationForDeletion();
                references = [group.EventLocationId, group.EventLocation, group.LocationId, group.Location, group.RoomId, group.Room];
                (retainedEventId, retainedTenantId) = (group.EventId, group.TenantId);
                break;
            default:
                var agenda = new EventAgendaItem { EventId = eventId, Event = null!, TenantId = tenantId, Tenant = null!, Title = "Break" };
                agenda.AssignEventLocation(placement);
                agenda.Location = location;
                agenda.RoomId = room.Id;
                agenda.Room = room;
                agenda.DetachEventLocationForDeletion();
                agenda.DetachEventLocationForDeletion();
                references = [agenda.EventLocationId, agenda.EventLocation, agenda.LocationId, agenda.Location, agenda.RoomId, agenda.Room];
                (retainedEventId, retainedTenantId) = (agenda.EventId, agenda.TenantId);
                break;
        }

        await Assert.That(references).IsEquivalentTo(new object?[6]);
        await Assert.That(retainedEventId).IsEqualTo(eventId);
        await Assert.That(retainedTenantId).IsEqualTo(tenantId);
        await Assert.That(placement.IsDeleted).IsFalse();
    }

    [Test]
    public async Task CarrierRejectsCrossEventOrCrossTenantPlacement()
    {
        var tenantId = Guid.CreateVersion7();
        var eventId = Guid.CreateVersion7();
        var session = CreateSession(tenantId, eventId, null);
        var crossEvent = EventLocation.CreatePhysical(
            tenantId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            DomainTestClock.UtcNow);
        var crossTenant = EventLocation.CreatePhysical(
            Guid.CreateVersion7(),
            eventId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            DomainTestClock.UtcNow);

        await Assert.That(() => session.AssignEventLocation(crossEvent)).Throws<InvalidOperationException>();
        await Assert.That(() => session.AssignEventLocation(crossTenant)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task TbaAssignmentClearsLegacyPhysicalRoomKeys()
    {
        var tenantId = Guid.CreateVersion7();
        var eventId = Guid.CreateVersion7();
        var session = CreateSession(tenantId, eventId, Guid.CreateVersion7());
        session.LocationId = Guid.CreateVersion7();
        var tba = EventLocation.CreateToBeAnnounced(
            tenantId,
            eventId,
            Guid.CreateVersion7(),
            DomainTestClock.UtcNow);

        session.AssignEventLocation(tba);

        await Assert.That(session.EventLocationId).IsEqualTo(tba.Id);
        await Assert.That(session.LocationId).IsNull();
        await Assert.That(session.RoomId).IsNull();
    }

    private static EventSession CreateSession(Guid tenantId, Guid eventId, Guid? roomId) => new()
    {
        Id = Guid.CreateVersion7(),
        EventId = eventId,
        Event = null!,
        TenantId = tenantId,
        Tenant = null!,
        RoomId = roomId
    };

    private static void MaterializeLegacyPii(Location location, string address, string postcode)
    {
        LocationPii pii = location.Pii
            ?? throw new InvalidOperationException("The fixture requires an attached PII row.");
        typeof(LocationPii).GetProperty(nameof(LocationPii.Address))?.SetValue(pii, address);
        typeof(LocationPii).GetProperty(nameof(LocationPii.Postcode))?.SetValue(pii, postcode);
    }

    private static Location CreatePhysicalLocation(Guid tenantId) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenantId,
        Tenant = null!,
        FullName = "Venue",
        Country = "BE",
        City = "Brussels"
    };
}
