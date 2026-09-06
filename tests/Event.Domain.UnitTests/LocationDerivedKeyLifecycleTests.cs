// ABOUTME: Verifies address and display derived keys follow aggregate writes, governance, promotion, and erasure.
// ABOUTME: Covers stale-key repair and proves rejected transitions cannot partially materialize derived PII.

using System.Reflection;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;

namespace Event.Domain.UnitTests;

public sealed class LocationDerivedKeyLifecycleTests
{
    private static readonly DateTime ChangedAtUtc = new(2026, 8, 26, 15, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task NewManualProviderReplacementAndFullNameWritesGenerateIndependentCurrentKeys()
    {
        Location location = NewLocation();

        location.SetManualAddress("Cafe\u0301 North", "1000");
        await AssertCurrentKeys(location, "CAFÉ NORTH");

        location.SetProviderAddress("Hall 😀", "2000", GeoCoordinate.Create(50.8503, 4.3517));
        await AssertCurrentKeys(location, "HALL 😀");

        location.FullName = "École 😀";
        await Assert.That(location.DisplaySortKey)
            .IsEqualTo("ÉCOLE 😀");
        await Assert.That(location.DisplaySortKeyVersion).IsEqualTo(LocationTextNormalization.CurrentRevision);
    }

    [Test]
    public async Task ExactBundleRepairsOnlyStaleAddressKeyAndReportsMutation()
    {
        Location location = NewLocation();
        location.SetManualAddress("Exact address", "1000");
        Guid stamp = location.ConcurrencyStamp;
        SetPrivateProperty(location.Pii!, nameof(LocationPii.AddressSubstringKey), string.Empty);
        SetPrivateProperty(location.Pii!, nameof(LocationPii.AddressSubstringKeyVersion), (short)0);

        bool changed = location.SetManualAddress("Exact address", "1000");

        await Assert.That(changed).IsTrue();
        await Assert.That(location.Pii!.AddressSubstringKeyVersion).IsEqualTo(LocationTextNormalization.CurrentRevision);
        await Assert.That(location.Pii.AddressSubstringKey).IsEqualTo("EXACT ADDRESS");
        await Assert.That(location.ConcurrencyStamp).IsEqualTo(stamp);
        await Assert.That(location.AddressVisibility).IsEqualTo(LocationAddressVisibilityEnum.Quarantined);
    }

    [Test]
    public async Task ErasureReplacesDisplayKeyAndRemovesAddressKeyWithPii()
    {
        Location location = NewLocation();
        Guid ownerId = Guid.CreateVersion7();
        location.ClassifyAsPrivateHome(ownerId);
        location.SetManualAddress("Private address", "1000");

        location.EraseOwnedPii(ChangedAtUtc, LocationPrivacyErasureReasonEnum.OwnerErasureRequest);

        await Assert.That(location.Pii).IsNull();
        await Assert.That(location.FullName).IsEqualTo(Location.ErasedPrivateVenueLabel);
        await Assert.That(location.DisplaySortKey)
            .IsEqualTo("PRIVATE VENUE");
        await Assert.That(location.DisplaySortKeyVersion).IsEqualTo(LocationTextNormalization.CurrentRevision);
    }

    [Test]
    public async Task PromotionMaterializesLegacyKeysBeforeVisibilityAndRotatesAuditStamp()
    {
        Location location = PromotableLocation();
        SetLegacyKeys(location);
        Guid beforeStamp = location.ConcurrencyStamp;
        Guid actorId = Guid.CreateVersion7();

        bool changed = location.PromoteAddressToTenantApproved(actorId, ChangedAtUtc);

        await Assert.That(changed).IsTrue();
        await Assert.That(location.AddressVisibility).IsEqualTo(LocationAddressVisibilityEnum.TenantApproved);
        await Assert.That(location.DisplaySortKeyVersion).IsEqualTo(LocationTextNormalization.CurrentRevision);
        await Assert.That(location.Pii!.AddressSubstringKeyVersion).IsEqualTo(LocationTextNormalization.CurrentRevision);
        await Assert.That(location.UpdatedBy).IsEqualTo(actorId);
        await Assert.That(location.UpdatedAt).IsEqualTo(ChangedAtUtc);
        await Assert.That(location.ConcurrencyStamp).IsNotEqualTo(beforeStamp);
    }

    [Test]
    public async Task InvalidPromotionDoesNotPartiallyRepairLegacyKeys()
    {
        Location location = PromotableLocation();
        SetLegacyKeys(location);
        SetPrivateProperty(location, nameof(Location.AddressVisibilityId), 999);

        await Assert.That(() => location.PromoteAddressToTenantApproved(Guid.CreateVersion7(), ChangedAtUtc))
            .Throws<InvalidOperationException>();
        await Assert.That(location.DisplaySortKeyVersion).IsEqualTo((short)0);
        await Assert.That(location.DisplaySortKey).IsEqualTo(string.Empty);
        await Assert.That(location.Pii!.AddressSubstringKeyVersion).IsEqualTo((short)0);
        await Assert.That(location.Pii.AddressSubstringKey).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ApprovedCurrentIsExactNoOpButApprovedStaleKeysAreAuditedMutation()
    {
        Location current = PromotableLocation();
        current.ApplyAddressGovernance(
            Guid.CreateVersion7(), LocationAddressSourceEnum.Manual,
            LocationAddressVisibilityEnum.TenantApproved, null);
        Guid currentStamp = current.ConcurrencyStamp;

        bool currentChanged = current.PromoteAddressToTenantApproved(Guid.CreateVersion7(), ChangedAtUtc);

        await Assert.That(currentChanged).IsFalse();
        await Assert.That(current.ConcurrencyStamp).IsEqualTo(currentStamp);

        Location stale = PromotableLocation();
        stale.ApplyAddressGovernance(
            Guid.CreateVersion7(), LocationAddressSourceEnum.Manual,
            LocationAddressVisibilityEnum.TenantApproved, null);
        SetLegacyKeys(stale);
        Guid staleStamp = stale.ConcurrencyStamp;

        bool staleChanged = stale.PromoteAddressToTenantApproved(Guid.CreateVersion7(), ChangedAtUtc);

        await Assert.That(staleChanged).IsTrue();
        await Assert.That(stale.ConcurrencyStamp).IsNotEqualTo(staleStamp);
        await Assert.That(stale.DisplaySortKeyVersion).IsEqualTo(LocationTextNormalization.CurrentRevision);
        await Assert.That(stale.Pii!.AddressSubstringKeyVersion).IsEqualTo(LocationTextNormalization.CurrentRevision);
    }

    [Test]
    [Arguments(true, true, true, true)]
    [Arguments(false, true, true, true)]
    [Arguments(true, false, true, true)]
    [Arguments(true, true, false, true)]
    [Arguments(true, true, true, false)]
    [Arguments(false, false, false, false)]
    public async Task ApprovedPromotionRepairsExactlyWhenAnyDerivedKeyComponentIsStale(
        bool displayVersionCurrent,
        bool displayValueCurrent,
        bool addressVersionCurrent,
        bool addressValueCurrent)
    {
        Guid creatorId = Guid.CreateVersion7();
        Guid promoterId = Guid.CreateVersion7();
        Guid organizationId = Guid.CreateVersion7();
        Location location = NewLocation();
        location.SetManualAddress("Matrix address", "1000");
        location.ApplyAddressGovernance(
            creatorId,
            LocationAddressSourceEnum.Manual,
            LocationAddressVisibilityEnum.TenantApproved,
            organizationId);
        LocationPii pii = location.Pii!;
        const string expectedDisplayKey = "VENUE";
        const string expectedAddressKey = "MATRIX ADDRESS";
        SetPrivateProperty(location, nameof(Location.DisplaySortKeyVersion),
            displayVersionCurrent ? LocationTextNormalization.CurrentRevision : (short)0);
        SetPrivateProperty(location, nameof(Location.DisplaySortKey),
            displayValueCurrent ? expectedDisplayKey : "STALE NAME");
        SetPrivateProperty(pii, nameof(LocationPii.AddressSubstringKeyVersion),
            addressVersionCurrent ? LocationTextNormalization.CurrentRevision : (short)0);
        SetPrivateProperty(pii, nameof(LocationPii.AddressSubstringKey),
            addressValueCurrent ? expectedAddressKey : "STALE ADDRESS");
        bool expectedChanged = !(displayVersionCurrent && displayValueCurrent
            && addressVersionCurrent && addressValueCurrent);
        Guid beforeStamp = location.ConcurrencyStamp;
        DateTime? beforeUpdatedAt = location.UpdatedAt;
        Guid? beforeUpdatedBy = location.UpdatedBy;

        await Assert.That(location.HasCurrentDerivedKeys()).IsEqualTo(!expectedChanged);
        bool changed = location.PromoteAddressToTenantApproved(promoterId, ChangedAtUtc);

        await Assert.That(changed).IsEqualTo(expectedChanged);
        await Assert.That(location.HasCurrentDerivedKeys()).IsTrue();
        await Assert.That(location.DisplaySortKey).IsEqualTo(expectedDisplayKey);
        await Assert.That(location.DisplaySortKeyVersion).IsEqualTo(LocationTextNormalization.CurrentRevision);
        await Assert.That(location.Pii).IsSameReferenceAs(pii);
        await Assert.That(pii.AddressSubstringKey).IsEqualTo(expectedAddressKey);
        await Assert.That(pii.AddressSubstringKeyVersion).IsEqualTo(LocationTextNormalization.CurrentRevision);
        await Assert.That(location.Address).IsEqualTo("Matrix address");
        await Assert.That(location.Postcode).IsEqualTo("1000");
        await Assert.That(location.AddressSource).IsEqualTo(LocationAddressSourceEnum.Manual);
        await Assert.That(location.AddressVisibility).IsEqualTo(LocationAddressVisibilityEnum.TenantApproved);
        await Assert.That(location.AddressOrganizationId).IsEqualTo(organizationId);
        await Assert.That(location.CreatedBy).IsEqualTo(creatorId);
        await Assert.That(location.ConcurrencyStamp == beforeStamp).IsEqualTo(!expectedChanged);
        await Assert.That(location.UpdatedAt).IsEqualTo(expectedChanged ? ChangedAtUtc : beforeUpdatedAt);
        await Assert.That(location.UpdatedBy).IsEqualTo(expectedChanged ? promoterId : beforeUpdatedBy);
    }

    [Test]
    [Arguments(true, true)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(false, false)]
    public async Task PiiCurrentKeyRequiresBothVersionAndOrdinalValue(
        bool versionCurrent,
        bool valueCurrent)
    {
        LocationPii pii = LocationPii.Create("Key address", "1000", null);
        const string expectedKey = "KEY ADDRESS";
        SetPrivateProperty(pii, nameof(LocationPii.AddressSubstringKeyVersion),
            versionCurrent ? LocationTextNormalization.CurrentRevision : (short)0);
        SetPrivateProperty(pii, nameof(LocationPii.AddressSubstringKey),
            valueCurrent ? expectedKey : "STALE ADDRESS");
        bool expectedChanged = !(versionCurrent && valueCurrent);

        await Assert.That(pii.HasCurrentAddressSubstringKey(expectedKey)).IsEqualTo(!expectedChanged);
        bool changed = pii.EnsureCurrentAddressSubstringKey();

        await Assert.That(changed).IsEqualTo(expectedChanged);
        await Assert.That(pii.HasCurrentAddressSubstringKey(expectedKey)).IsTrue();
        await Assert.That(pii.AddressSubstringKey).IsEqualTo(expectedKey);
        await Assert.That(pii.AddressSubstringKeyVersion).IsEqualTo(LocationTextNormalization.CurrentRevision);
    }

    [Test]
    public async Task TenantApprovalGovernanceRepairsBothStaleKeySidesBeforePublishing()
    {
        Location location = PromotableLocation();
        SetLegacyKeys(location);

        location.ApplyAddressGovernance(
            Guid.CreateVersion7(),
            LocationAddressSourceEnum.Manual,
            LocationAddressVisibilityEnum.TenantApproved,
            null);

        await Assert.That(location.AddressVisibility).IsEqualTo(LocationAddressVisibilityEnum.TenantApproved);
        await Assert.That(location.HasCurrentDerivedKeys()).IsTrue();
        await Assert.That(location.DisplaySortKey)
            .IsEqualTo("VENUE");
        await Assert.That(location.Pii!.AddressSubstringKey)
            .IsEqualTo("LEGACY ADDRESS");
    }

    [Test]
    public async Task InvalidPromotionStatesRejectBeforeRepairingStaleKeys()
    {
        Guid actorId = Guid.CreateVersion7();
        Location privateHome = PromotableLocation();
        SetLegacyKeys(privateHome);
        privateHome.ClassifyAsPrivateHome(actorId);
        Location nonActive = PromotableLocation();
        SetLegacyKeys(nonActive);
        SetPrivateProperty(nonActive, nameof(Location.LocationPrivacyStateId),
            (int)LocationPrivacyStateEnum.NotProvided);

        await Assert.That(() => privateHome.PromoteAddressToTenantApproved(actorId, ChangedAtUtc))
            .Throws<InvalidOperationException>();
        await Assert.That(() => nonActive.PromoteAddressToTenantApproved(actorId, ChangedAtUtc))
            .Throws<InvalidOperationException>();

        await Assert.That(privateHome.DisplaySortKeyVersion).IsEqualTo((short)0);
        await Assert.That(privateHome.Pii!.AddressSubstringKeyVersion).IsEqualTo((short)0);
        await Assert.That(nonActive.DisplaySortKeyVersion).IsEqualTo((short)0);
        await Assert.That(nonActive.Pii!.AddressSubstringKeyVersion).IsEqualTo((short)0);
    }

    private static Location NewLocation() => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = Guid.CreateVersion7(),
        FullName = "Venue",
        Country = "BE",
        City = "Brussels",
        CreatedAt = DateTime.UnixEpoch,
        ConcurrencyStamp = Guid.CreateVersion7()
    };

    private static Location PromotableLocation()
    {
        Location location = NewLocation();
        location.SetManualAddress("Legacy address", "1000");
        location.ApplyAddressGovernance(
            Guid.CreateVersion7(), LocationAddressSourceEnum.Manual,
            LocationAddressVisibilityEnum.CreatorPrivate, null);
        return location;
    }

    private static void SetLegacyKeys(Location location)
    {
        SetPrivateProperty(location, nameof(Location.DisplaySortKey), string.Empty);
        SetPrivateProperty(location, nameof(Location.DisplaySortKeyVersion), (short)0);
        SetPrivateProperty(location.Pii!, nameof(LocationPii.AddressSubstringKey), string.Empty);
        SetPrivateProperty(location.Pii!, nameof(LocationPii.AddressSubstringKeyVersion), (short)0);
    }

    private static async Task AssertCurrentKeys(Location location, string expectedAddressKey)
    {
        await Assert.That(location.Pii!.AddressSubstringKey).IsEqualTo(expectedAddressKey);
        await Assert.That(location.Pii.AddressSubstringKeyVersion).IsEqualTo(LocationTextNormalization.CurrentRevision);
        await Assert.That(location.DisplaySortKeyVersion).IsEqualTo(LocationTextNormalization.CurrentRevision);
    }

    private static void SetPrivateProperty(object target, string propertyName, object value) =>
        target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)!.SetValue(target, value);
}
