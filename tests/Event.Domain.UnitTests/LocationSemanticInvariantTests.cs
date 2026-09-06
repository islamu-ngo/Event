// ABOUTME: Distinguishes Location aggregate invariants across materialization, governance, and privacy transitions.
// ABOUTME: Verifies state and typed failures without coupling tests to exception prose.

using System.Reflection;
using System.Runtime.ExceptionServices;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;

namespace Event.Domain.UnitTests;

public sealed class LocationSemanticInvariantTests
{
    private static readonly DateTime ChangedAtUtc =
        new(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task ScalarPropertiesNormalizeUnmaterializedValuesAndRetainAssignments()
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

        SetField<string?>(location, "_fullName", null);
        SetField<string?>(location, "_country", null);
        SetField<string?>(location, "_city", null);

        await Assert.That(location.FullName).IsEqualTo(string.Empty);
        await Assert.That(location.Country).IsEqualTo(string.Empty);
        await Assert.That(location.City).IsEqualTo(string.Empty);
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
    public async Task PiiMaterializationAcceptsOnlyConsistentAggregateIdentitiesAndTransitionsPrivacyState()
    {
        Guid locationId = Guid.CreateVersion7();
        var location = NewLocation(locationId);
        LocationPii sameIdentity = LocationPii.Create("Same", "1000", null);
        sameIdentity.AssociateWith(NewLocation(locationId));

        SetPii(location, sameIdentity);

        await Assert.That(location.Pii).IsSameReferenceAs(sameIdentity);
        await Assert.That(location.LocationPrivacyStateId)
            .IsEqualTo((int)LocationPrivacyStateEnum.Active);

        var unpersistedLocation = NewLocation(Guid.Empty);
        LocationPii persistedPii = LocationPii.Create("Persisted", "1000", null);
        persistedPii.AssociateWith(NewLocation(Guid.CreateVersion7()));
        SetPii(unpersistedLocation, persistedPii);

        await Assert.That(unpersistedLocation.Pii).IsSameReferenceAs(persistedPii);
        await Assert.That(unpersistedLocation.LocationPrivacyStateId)
            .IsEqualTo((int)LocationPrivacyStateEnum.Active);

        var persistedLocation = NewLocation(Guid.CreateVersion7());
        LocationPii unpersistedPii = LocationPii.Create("New", "1000", null);
        SetPii(persistedLocation, unpersistedPii);

        await Assert.That(persistedLocation.Pii).IsSameReferenceAs(unpersistedPii);
        await Assert.That(persistedLocation.LocationPrivacyStateId)
            .IsEqualTo((int)LocationPrivacyStateEnum.Active);
    }

    [Test]
    public async Task ForeignPiiMaterializationIsRejectedWithoutPartialState()
    {
        var location = NewLocation(Guid.CreateVersion7());
        LocationPii foreignPii = LocationPii.Create("Foreign", "1000", null);
        foreignPii.AssociateWith(NewLocation(Guid.CreateVersion7()));

        await Assert.That(() => SetPii(location, foreignPii))
            .Throws<InvalidOperationException>();
        await Assert.That(location.Pii).IsNull();
        await Assert.That(location.LocationPrivacyStateId)
            .IsEqualTo((int)LocationPrivacyStateEnum.NotProvided);
    }

    [Test]
    public async Task PiiSetterEnforcesErasureOwnershipAndActiveClearInvariants()
    {
        LocationPii pii = LocationPii.Create("Address", "1000", null);
        var erased = NewLocation(Guid.CreateVersion7());
        SetPrivateProperty(erased, nameof(Location.LocationPrivacyStateId), (int)LocationPrivacyStateEnum.Erased);
        var ownerlessHome = NewLocation(Guid.CreateVersion7());
        SetPrivateProperty(ownerlessHome, nameof(Location.LocationKindId), (int)LocationKindEnum.PrivateHome);
        var active = NewLocation(Guid.CreateVersion7());
        SetPii(active, LocationPii.Create("Active", "1000", null));

        await Assert.That(() => SetPii(erased, pii)).Throws<InvalidOperationException>();
        await Assert.That(() => SetPii(ownerlessHome, pii)).Throws<InvalidOperationException>();
        await Assert.That(() => SetPii(active, null)).Throws<InvalidOperationException>();

        await Assert.That(erased.Pii).IsNull();
        await Assert.That(ownerlessHome.Pii).IsNull();
        await Assert.That(active.Pii).IsNotNull();
        await Assert.That(active.LocationPrivacyStateId)
            .IsEqualTo((int)LocationPrivacyStateEnum.Active);
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
    public async Task ChangedManualBundleInvalidatesProviderApprovalScopeCoordinateAndLookupState()
    {
        Guid actorId = Guid.CreateVersion7();
        Guid organizationId = Guid.CreateVersion7();
        var location = NewLocation(Guid.CreateVersion7());
        location.SetProviderAddress("Provider", "1000", GeoCoordinate.Create(50.8503, 4.3517));
        location.ApplyAddressGovernance(
            actorId,
            LocationAddressSourceEnum.ProviderSelection,
            LocationAddressVisibilityEnum.TenantApproved,
            organizationId);
        SetPrivateProperty(location, nameof(Location.AddressSourceLookup), new LocationAddressSource
        {
            MasterCode = "PROVIDER_SELECTION",
            FullName = "Provider selection"
        });
        SetPrivateProperty(location, nameof(Location.AddressVisibilityLookup), new LocationAddressVisibility
        {
            MasterCode = "TENANT_APPROVED",
            FullName = "Tenant approved"
        });
        SetPrivateProperty(location, nameof(Location.AddressOrganizationTenant), new OrganizationTenant
        {
            Tenant = null!,
            Organization = null!,
            ApprovalStatus = null!
        });
        Guid priorStamp = location.ConcurrencyStamp;

        location.SetManualAddress("Manual replacement", "2000");

        await Assert.That(location.Address).IsEqualTo("Manual replacement");
        await Assert.That(location.Postcode).IsEqualTo("2000");
        await Assert.That(location.GetCoordinate()).IsNull();
        await Assert.That(location.AddressSource).IsEqualTo(LocationAddressSourceEnum.Manual);
        await Assert.That(location.AddressVisibility).IsEqualTo(LocationAddressVisibilityEnum.Quarantined);
        await Assert.That(location.AddressOrganizationId).IsNull();
        await Assert.That(location.AddressSourceLookup).IsNull();
        await Assert.That(location.AddressVisibilityLookup).IsNull();
        await Assert.That(location.AddressOrganizationTenant).IsNull();
        await Assert.That(location.ConcurrencyStamp).IsNotEqualTo(priorStamp);
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
    public async Task PiiMaterializationAllowsOnlyTheNonBlockingClearAndReplacementCombinations()
    {
        var activeWithoutPii = NewLocation(Guid.CreateVersion7());
        SetPrivateProperty(activeWithoutPii, nameof(Location.LocationPrivacyStateId),
            (int)LocationPrivacyStateEnum.Active);
        SetPii(activeWithoutPii, null);

        var activeWithPii = NewLocation(Guid.CreateVersion7());
        LocationPii first = LocationPii.Create("First", "1000", null);
        LocationPii replacement = LocationPii.Create("Replacement", "2000", null);
        SetPii(activeWithPii, first);
        SetPii(activeWithPii, replacement);

        await Assert.That(activeWithoutPii.Pii).IsNull();
        await Assert.That(activeWithoutPii.LocationPrivacyStateId)
            .IsEqualTo((int)LocationPrivacyStateEnum.NotProvided);
        await Assert.That(activeWithPii.Pii).IsSameReferenceAs(replacement);
        await Assert.That(activeWithPii.LocationPrivacyStateId)
            .IsEqualTo((int)LocationPrivacyStateEnum.Active);
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
            LocationPii.Create(string.Empty, "1000", null));
        ArgumentException emptyPostcode = Assert.Throws<ArgumentException>(() =>
            LocationPii.Create("Address", "   ", null));
        LocationPii pii = LocationPii.Create("Address", "1000", null);
        ArgumentNullException nullLocation = Assert.Throws<ArgumentNullException>(() =>
            pii.AssociateWith(null!));
        ArgumentNullException nullPii = Assert.Throws<ArgumentNullException>(() =>
            AttachPii(location, null!));

        await Assert.That(nullAddress.ParamName).IsEqualTo("address");
        await Assert.That(nullPostcode.ParamName).IsEqualTo("postcode");
        await Assert.That(emptyAddress.ParamName).IsEqualTo("address");
        await Assert.That(emptyPostcode.ParamName).IsEqualTo("postcode");
        await Assert.That(nullLocation.ParamName).IsEqualTo("location");
        await Assert.That(nullPii.ParamName).IsEqualTo("pii");
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
    public async Task PromotionRequiresBothActiveStateAndAttachedPiiWithoutMutation()
    {
        Guid actorId = Guid.CreateVersion7();
        var activeWithoutPii = NewLocation(Guid.CreateVersion7());
        SetPrivateProperty(activeWithoutPii, nameof(Location.LocationPrivacyStateId), (int)LocationPrivacyStateEnum.Active);
        var piiWithoutActiveState = NewLocation(Guid.CreateVersion7());
        SetField(piiWithoutActiveState, "_pii", LocationPii.Create("Materialized", "1000", null));

        await Assert.That(() => activeWithoutPii.PromoteAddressToTenantApproved(actorId, ChangedAtUtc))
            .Throws<InvalidOperationException>();
        await Assert.That(() => piiWithoutActiveState.PromoteAddressToTenantApproved(actorId, ChangedAtUtc))
            .Throws<InvalidOperationException>();

        await Assert.That(activeWithoutPii.AddressVisibility)
            .IsEqualTo(LocationAddressVisibilityEnum.Quarantined);
        await Assert.That(piiWithoutActiveState.AddressVisibility)
            .IsEqualTo(LocationAddressVisibilityEnum.Quarantined);
        await Assert.That(activeWithoutPii.UpdatedAt).IsNull();
        await Assert.That(piiWithoutActiveState.UpdatedAt).IsNull();
    }

    [Test]
    public async Task PromotionRejectsUnsupportedPersistedVisibilityWithoutMutation()
    {
        Guid actorId = Guid.CreateVersion7();
        var location = NewLocation(Guid.CreateVersion7());
        location.SetManualAddress("Address", "1000");
        SetPrivateProperty(location, nameof(Location.AddressVisibilityId), 999);
        Guid concurrencyStamp = Guid.CreateVersion7();
        location.ConcurrencyStamp = concurrencyStamp;

        await Assert.That(() => location.PromoteAddressToTenantApproved(actorId, ChangedAtUtc))
            .Throws<InvalidOperationException>();

        await Assert.That(location.AddressVisibilityId).IsEqualTo(999);
        await Assert.That(location.UpdatedAt).IsNull();
        await Assert.That(location.UpdatedBy).IsNull();
        await Assert.That(location.ConcurrencyStamp).IsEqualTo(concurrencyStamp);
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

    private static void SetPii(Location location, LocationPii? pii) =>
        InvokeAndUnwrap(typeof(Location).GetProperty(nameof(Location.Pii))!
            .GetSetMethod(nonPublic: true)!, location, pii);

    private static void AttachPii(Location location, LocationPii pii) =>
        InvokeAndUnwrap(typeof(Location).GetMethod("AttachPii", BindingFlags.Instance | BindingFlags.NonPublic)!,
            location, pii);

    private static void InvokeAndUnwrap(MethodInfo method, Location location, LocationPii? pii)
    {
        try
        {
            method.Invoke(location, [pii]);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
        }
    }

    private static void SetPrivateProperty<T>(Location location, string propertyName, T value) =>
        typeof(Location).GetProperty(propertyName)!.SetValue(location, value);

    private static void SetField<T>(Location location, string fieldName, T value) =>
        typeof(Location).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(location, value);
}
