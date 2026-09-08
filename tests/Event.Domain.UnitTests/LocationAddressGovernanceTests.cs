using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;

namespace Event.Domain.UnitTests;

public sealed class LocationAddressGovernanceTests
{
    [Test]
    public async Task ApplyAddressGovernanceAcceptsEveryValidScopeWithoutChangingAddressData()
    {
        Guid actorId = Guid.CreateVersion7();
        Guid organizationId = Guid.CreateVersion7();
        var location = new Location
        {
            Id = Guid.CreateVersion7(),
            FullName = "Governed venue",
            Country = "BE",
            City = "Brussels"
        };
        location.SetProviderAddress(
            "1 Governed Street",
            "1000",
            GeoCoordinate.Create(50.8503, 4.3517));
        GeoCoordinate? coordinate = location.GetCoordinate();

        location.ApplyAddressGovernance(
            actorId,
            LocationAddressSourceEnum.ProviderSelection,
            LocationAddressVisibilityEnum.OrganizationScoped,
            organizationId);

        await Assert.That(location.AddressSource).IsEqualTo(LocationAddressSourceEnum.ProviderSelection);
        await Assert.That(location.AddressVisibility).IsEqualTo(LocationAddressVisibilityEnum.OrganizationScoped);
        await Assert.That(location.AddressOrganizationId).IsEqualTo(organizationId);
        await Assert.That(location.CreatedBy).IsEqualTo(actorId);
        await Assert.That(location.Address).IsEqualTo("1 Governed Street");
        await Assert.That(location.Postcode).IsEqualTo("1000");
        await Assert.That(location.GetCoordinate()).IsEqualTo(coordinate);

        location.ApplyAddressGovernance(
            actorId,
            LocationAddressSourceEnum.ProviderSelection,
            LocationAddressVisibilityEnum.TenantApproved,
            organizationId);

        await Assert.That(location.AddressSource).IsEqualTo(LocationAddressSourceEnum.ProviderSelection);
        await Assert.That(location.AddressOrganizationId).IsEqualTo(organizationId);
    }

    [Test]
    public async Task ApplyAddressGovernanceRejectsInvalidActorEnumsAndScopes()
    {
        Guid actorId = Guid.CreateVersion7();
        Guid organizationId = Guid.CreateVersion7();
        var location = new Location
        {
            Id = Guid.CreateVersion7(),
            FullName = "Governed venue",
            Country = "BE",
            City = "Brussels"
        };

        await Assert.That(() => location.ApplyAddressGovernance(
                Guid.Empty,
                LocationAddressSourceEnum.Manual,
                LocationAddressVisibilityEnum.CreatorPrivate,
                null))
            .Throws<ArgumentException>();
        await Assert.That(() => location.ApplyAddressGovernance(
                actorId,
                (LocationAddressSourceEnum)99,
                LocationAddressVisibilityEnum.CreatorPrivate,
                null))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => location.ApplyAddressGovernance(
                actorId,
                LocationAddressSourceEnum.Manual,
                (LocationAddressVisibilityEnum)99,
                null))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => location.ApplyAddressGovernance(
                actorId,
                LocationAddressSourceEnum.Manual,
                LocationAddressVisibilityEnum.Quarantined,
                organizationId))
            .Throws<ArgumentException>();
        await Assert.That(() => location.ApplyAddressGovernance(
                actorId,
                LocationAddressSourceEnum.Manual,
                LocationAddressVisibilityEnum.CreatorPrivate,
                organizationId))
            .Throws<ArgumentException>();
        await Assert.That(() => location.ApplyAddressGovernance(
                actorId,
                LocationAddressSourceEnum.Manual,
                LocationAddressVisibilityEnum.OrganizationScoped,
                null))
            .Throws<ArgumentException>();

        location.ClassifyAsPrivateHome(actorId);
        await Assert.That(() => location.ApplyAddressGovernance(
                actorId,
                LocationAddressSourceEnum.Manual,
                LocationAddressVisibilityEnum.TenantApproved,
                null))
            .Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments(LocationAddressVisibilityEnum.OrganizationScoped)]
    [Arguments(LocationAddressVisibilityEnum.TenantApproved)]
    public async Task ApplyAddressGovernanceRejectsEmptyOrganizationWithoutMutatingAggregate(
        LocationAddressVisibilityEnum visibility)
    {
        Guid actorId = Guid.CreateVersion7();
        var location = new Location
        {
            Id = Guid.CreateVersion7(),
            FullName = "Governed venue",
            Country = "BE",
            City = "Brussels",
            Timezone = "Europe/Brussels",
            CreatedAt = new DateTime(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc),
            ConcurrencyStamp = Guid.CreateVersion7(),
        };
        location.SetProviderAddress(
            "1 Governed Street",
            "1000",
            GeoCoordinate.Create(50.8503, 4.3517));
        location.ApplyAddressGovernance(
            actorId,
            LocationAddressSourceEnum.Manual,
            LocationAddressVisibilityEnum.CreatorPrivate,
            null);
        LocationState before = Capture(location);

        await Assert.That(() => location.ApplyAddressGovernance(
                Guid.CreateVersion7(),
                LocationAddressSourceEnum.ProviderSelection,
                visibility,
                Guid.Empty))
            .Throws<ArgumentException>();

        await Assert.That(Capture(location)).IsEqualTo(before);
    }

    [Test]
    public async Task AuditedGovernanceRequiresUtcAndRotatesOnlyOnAcceptedTransition()
    {
        Guid actorId = Guid.CreateVersion7();
        DateTime changedAtUtc = new(2026, 8, 26, 16, 0, 0, DateTimeKind.Utc);
        var location = NewPromotableLocation(actorId);
        Guid beforeStamp = location.ConcurrencyStamp;

        location.ApplyAddressGovernanceWithAudit(
            actorId,
            LocationAddressSourceEnum.ProviderSelection,
            LocationAddressVisibilityEnum.OrganizationScoped,
            Guid.CreateVersion7(),
            changedAtUtc);

        await Assert.That(location.UpdatedAt).IsEqualTo(changedAtUtc);
        await Assert.That(location.UpdatedBy).IsEqualTo(actorId);
        await Assert.That(location.ConcurrencyStamp).IsNotEqualTo(beforeStamp);

        LocationState accepted = Capture(location);
        ArgumentException defaultTime = Assert.Throws<ArgumentException>(() =>
            location.ApplyAddressGovernanceWithAudit(
                actorId,
                LocationAddressSourceEnum.Manual,
                LocationAddressVisibilityEnum.CreatorPrivate,
                null,
                default));
        ArgumentException localTime = Assert.Throws<ArgumentException>(() =>
            location.ApplyAddressGovernanceWithAudit(
                actorId,
                LocationAddressSourceEnum.Manual,
                LocationAddressVisibilityEnum.CreatorPrivate,
                null,
                DateTime.SpecifyKind(changedAtUtc, DateTimeKind.Local)));

        await Assert.That(defaultTime.ParamName).IsEqualTo("changedAtUtc");
        await Assert.That(localTime.ParamName).IsEqualTo("changedAtUtc");
        await Assert.That(Capture(location)).IsEqualTo(accepted);
    }

    [Test]
    public async Task ErasureQuarantinesReuseAndPreservesSourceProvenance()
    {
        Guid actorId = Guid.CreateVersion7();
        Guid organizationId = Guid.CreateVersion7();
        var location = new Location
        {
            Id = Guid.CreateVersion7(),
            FullName = "Private home",
            Country = "BE",
            City = "Brussels"
        };
        location.ClassifyAsPrivateHome(actorId);
        location.SetManualAddress("Private address", "1000");
        location.ApplyAddressGovernance(
            actorId,
            LocationAddressSourceEnum.Manual,
            LocationAddressVisibilityEnum.OrganizationScoped,
            organizationId);

        location.EraseOwnedPii(
            new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc),
            LocationPrivacyErasureReasonEnum.OwnerErasureRequest);

        await Assert.That(location.AddressSource).IsEqualTo(LocationAddressSourceEnum.Manual);
        await Assert.That(location.AddressVisibility).IsEqualTo(LocationAddressVisibilityEnum.Quarantined);
        await Assert.That(location.AddressOrganizationId).IsNull();
        await Assert.That(() => location.ApplyAddressGovernance(
                actorId,
                LocationAddressSourceEnum.Manual,
                LocationAddressVisibilityEnum.CreatorPrivate,
                null))
            .Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments(LocationAddressVisibilityEnum.Quarantined)]
    [Arguments(LocationAddressVisibilityEnum.CreatorPrivate)]
    [Arguments(LocationAddressVisibilityEnum.OrganizationScoped)]
    public async Task PromoteAddressToTenantApprovedChangesOnlyVisibilityAuditAndConcurrency(
        LocationAddressVisibilityEnum initialVisibility)
    {
        Guid actorId = Guid.CreateVersion7();
        Guid creatorId = Guid.CreateVersion7();
        Guid organizationId = Guid.CreateVersion7();
        DateTime changedAtUtc = new(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);
        var location = new Location
        {
            Id = Guid.CreateVersion7(),
            TenantId = Guid.CreateVersion7(),
            FullName = "Governed venue",
            Country = "BE",
            City = "Brussels",
            Timezone = "Europe/Brussels",
            CreatedAt = DateTime.UnixEpoch,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        location.SetProviderAddress(
            "1 Governed Street",
            "1000",
            GeoCoordinate.Create(50.8503, 4.3517));
        location.ApplyAddressGovernance(
            creatorId,
            LocationAddressSourceEnum.ProviderSelection,
            initialVisibility,
            initialVisibility == LocationAddressVisibilityEnum.OrganizationScoped ? organizationId : null);
        LocationState before = Capture(location);

        bool changed = location.PromoteAddressToTenantApproved(actorId, changedAtUtc);
        LocationState after = Capture(location);

        await Assert.That(changed).IsTrue();
        await Assert.That(after).IsEqualTo(before with
        {
            AddressVisibilityId = (int)LocationAddressVisibilityEnum.TenantApproved,
            AddressVisibilityLookup = null,
            UpdatedAt = changedAtUtc,
            UpdatedBy = actorId,
            ConcurrencyStamp = after.ConcurrencyStamp
        });
        await Assert.That(after.ConcurrencyStamp).IsNotEqualTo(before.ConcurrencyStamp);
        await Assert.That(after.ConcurrencyStamp.Version).IsEqualTo(7);
    }

    [Test]
    public async Task PromoteAddressToTenantApprovedSameTargetIsExactNoOp()
    {
        Guid actorId = Guid.CreateVersion7();
        var location = NewPromotableLocation(actorId);
        location.ApplyAddressGovernance(
            actorId,
            LocationAddressSourceEnum.Manual,
            LocationAddressVisibilityEnum.TenantApproved,
            null);
        LocationState before = Capture(location);

        bool changed = location.PromoteAddressToTenantApproved(
            Guid.CreateVersion7(),
            new DateTime(2026, 8, 26, 13, 0, 0, DateTimeKind.Utc));

        await Assert.That(changed).IsFalse();
        await Assert.That(Capture(location)).IsEqualTo(before);
    }

    [Test]
    public async Task PromoteAddressToTenantApprovedRejectsInvalidActorAndTimestampWithoutMutation()
    {
        Guid actorId = Guid.CreateVersion7();
        var location = NewPromotableLocation(actorId);
        LocationState before = Capture(location);

        await Assert.That(() => location.PromoteAddressToTenantApproved(
                Guid.Empty,
                new DateTime(2026, 8, 26, 13, 0, 0, DateTimeKind.Utc)))
            .Throws<ArgumentException>();
        await Assert.That(() => location.PromoteAddressToTenantApproved(actorId, default))
            .Throws<ArgumentException>();
        await Assert.That(() => location.PromoteAddressToTenantApproved(
                actorId,
                new DateTime(2026, 8, 26, 13, 0, 0, DateTimeKind.Local)))
            .Throws<ArgumentException>();
        await Assert.That(Capture(location)).IsEqualTo(before);
    }

    [Test]
    public async Task PromoteAddressToTenantApprovedRejectsMissingActivePiiPrivateHomeAndErasure()
    {
        Guid actorId = Guid.CreateVersion7();
        DateTime changedAtUtc = new(2026, 8, 26, 14, 0, 0, DateTimeKind.Utc);
        var missingPii = new Location
        {
            Id = Guid.CreateVersion7(),
            TenantId = Guid.CreateVersion7(),
            FullName = "No address",
            Country = "BE",
            City = "Brussels",
            CreatedAt = DateTime.UnixEpoch,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        missingPii.ApplyAddressGovernance(
            actorId,
            LocationAddressSourceEnum.UnknownLegacy,
            LocationAddressVisibilityEnum.Quarantined,
            null);
        var privateHome = NewPromotableLocation(actorId);
        privateHome.ClassifyAsPrivateHome(actorId);
        var erased = NewPromotableLocation(actorId);
        erased.ClassifyAsPrivateHome(actorId);
        erased.EraseOwnedPii(changedAtUtc.AddHours(-1), LocationPrivacyErasureReasonEnum.OwnerErasureRequest);
        LocationState missingBefore = Capture(missingPii);
        LocationState privateBefore = Capture(privateHome);
        LocationState erasedBefore = Capture(erased);

        await Assert.That(() => missingPii.PromoteAddressToTenantApproved(actorId, changedAtUtc))
            .Throws<InvalidOperationException>();
        await Assert.That(() => privateHome.PromoteAddressToTenantApproved(actorId, changedAtUtc))
            .Throws<InvalidOperationException>();
        await Assert.That(() => erased.PromoteAddressToTenantApproved(actorId, changedAtUtc))
            .Throws<InvalidOperationException>();
        await Assert.That(Capture(missingPii)).IsEqualTo(missingBefore);
        await Assert.That(Capture(privateHome)).IsEqualTo(privateBefore);
        await Assert.That(Capture(erased)).IsEqualTo(erasedBefore);
    }

    private static Location NewPromotableLocation(Guid actorId)
    {
        var location = new Location
        {
            Id = Guid.CreateVersion7(),
            TenantId = Guid.CreateVersion7(),
            FullName = "Promotable venue",
            Country = "BE",
            City = "Brussels",
            CreatedAt = DateTime.UnixEpoch,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        location.SetManualAddress("1 Safe Street", "1000");
        location.ApplyAddressGovernance(
            actorId,
            LocationAddressSourceEnum.Manual,
            LocationAddressVisibilityEnum.CreatorPrivate,
            null);
        return location;
    }

    private static LocationState Capture(Location location) => new(
        location.Id,
        location.FullName,
        location.Country,
        location.City,
        location.Timezone,
        location.LocationKindId,
        location.LocationPrivacyStateId,
        location.AddressSourceId,
        location.AddressVisibilityId,
        location.AddressOrganizationId,
        location.OwnerUserId,
        location.PiiErasedAtUtc,
        location.PiiErasureReason,
        location.Address,
        location.Postcode,
        location.GetCoordinate(),
        location.Pii,
        location.LocationKind,
        location.LocationPrivacyState,
        location.AddressSourceLookup,
        location.AddressVisibilityLookup,
        location.AddressOrganizationTenant,
        location.OwnerUser,
        location.CreatedAt,
        location.CreatedBy,
        location.UpdatedAt,
        location.UpdatedBy,
        location.ConcurrencyStamp,
        location.Rooms.Count);

    private sealed record LocationState(
        Guid Id,
        string FullName,
        string Country,
        string City,
        string? Timezone,
        int LocationKindId,
        int LocationPrivacyStateId,
        int AddressSourceId,
        int AddressVisibilityId,
        Guid? AddressOrganizationId,
        Guid? OwnerUserId,
        DateTime? PiiErasedAtUtc,
        LocationPrivacyErasureReasonEnum? PiiErasureReason,
        string? Address,
        string? Postcode,
        GeoCoordinate? Coordinate,
        LocationPii? Pii,
        LocationKind? LocationKind,
        LocationPrivacyState? LocationPrivacyState,
        LocationAddressSource? AddressSourceLookup,
        LocationAddressVisibility? AddressVisibilityLookup,
        OrganizationTenant? AddressOrganizationTenant,
        User? OwnerUser,
        DateTime CreatedAt,
        Guid? CreatedBy,
        DateTime? UpdatedAt,
        Guid? UpdatedBy,
        Guid ConcurrencyStamp,
        int RoomCount);
}
