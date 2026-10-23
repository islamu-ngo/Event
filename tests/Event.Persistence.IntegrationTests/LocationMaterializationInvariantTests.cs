using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests;

[Category("EventLocationPrivacy")]
public sealed class LocationMaterializationInvariantTests
{
    [Test]
    public async Task MaterializedAddressPreservesIdentityExactCoordinatesAndTrackedReplacement()
    {
        await using var connection = await SqliteTestDatabaseFactory.CreateOpenIsolatedConnectionAsync(Token);
        await LocalIdentitySqliteTemplate.CopySeededApplicationToAsync(connection, Token);
        await using var context = CreateContext(connection);
        Location location = await SeedAsync(context);
        Guid id = location.Id;
        GeoCoordinate coordinate = GeoCoordinate.Create(50.850_300_000_000_01, 4.351_700_000_000_001);
        location.SetProviderAddress("Provider address", "1000", coordinate);
        await context.SaveChangesAsync(Token);
        context.ChangeTracker.Clear();

        Location loaded = await context.Locations.SingleAsync(item => item.Id == id, Token);
        LocationPii pii = loaded.Pii!;
        await Assert.That(loaded.FullName).IsEqualTo("Materialized venue");
        await Assert.That(loaded.Country).IsEqualTo("BE");
        await Assert.That(loaded.City).IsEqualTo("Brussels");
        await Assert.That(pii.LocationId).IsEqualTo(id);
        await Assert.That(pii.Location).IsSameReferenceAs(loaded);
        await Assert.That(loaded.LocationPrivacyStateId).IsEqualTo((int)LocationPrivacyStateEnum.Active);
        await Assert.That(loaded.GetCoordinate()).IsEqualTo(coordinate);
        await Assert.That(context.Entry(pii).State).IsEqualTo(EntityState.Unchanged);

        loaded.SetManualAddress("Manual replacement", "2000");
        await Assert.That(loaded.Pii).IsSameReferenceAs(pii);
        await context.SaveChangesAsync(Token);
        context.ChangeTracker.Clear();

        Location replaced = await context.Locations.SingleAsync(item => item.Id == id, Token);
        await Assert.That(replaced.Address).IsEqualTo("Manual replacement");
        await Assert.That(replaced.Postcode).IsEqualTo("2000");
        await Assert.That(replaced.GetCoordinate()).IsNull();
        await Assert.That(replaced.Pii!.Latitude).IsNull();
        await Assert.That(replaced.Pii.Longitude).IsNull();
        await Assert.That(replaced.Pii.LocationId).IsEqualTo(id);
    }

    [Test]
    public async Task MaterializedErasureRemovesPiiAndRejectsResurrectionWithoutTrackedChanges()
    {
        await using var connection = await SqliteTestDatabaseFactory.CreateOpenIsolatedConnectionAsync(Token);
        await LocalIdentitySqliteTemplate.CopySeededApplicationToAsync(connection, Token);
        await using var context = CreateContext(connection);
        Location location = await SeedAsync(context, privateHome: true);
        context.ChangeTracker.Clear();
        Location active = await context.Locations.SingleAsync(item => item.Id == location.Id, Token);
        LocationPii priorPii = active.Pii!;
        await Assert.That(priorPii.Location).IsSameReferenceAs(active);
        await Assert.That(active.LocationPrivacyStateId).IsEqualTo((int)LocationPrivacyStateEnum.Active);
        active.EraseOwnedPii(DateTime.UnixEpoch, LocationPrivacyErasureReasonEnum.OwnerErasureRequest);
        context.ChangeTracker.DetectChanges();
        await Assert.That(context.Entry(priorPii).State).IsEqualTo(EntityState.Deleted);
        await context.SaveChangesAsync(Token);
        context.ChangeTracker.Clear();

        Location loaded = await context.Locations.SingleAsync(item => item.Id == location.Id, Token);
        await Assert.That(loaded.Pii).IsNull();
        await Assert.That(loaded.GetCoordinate()).IsNull();
        await Assert.That(loaded.FullName).IsEqualTo(Location.ErasedPrivateVenueLabel);
        await Assert.That(loaded.Country).IsEqualTo("BE");
        await Assert.That(loaded.City).IsEqualTo(string.Empty);
        await Assert.That(loaded.DisplaySortKey).IsEqualTo("PRIVATE VENUE");
        await Assert.That(loaded.LocationPrivacyStateId).IsEqualTo((int)LocationPrivacyStateEnum.Erased);
        await Assert.That(() => loaded.SetManualAddress("Resurrected", "2000"))
            .Throws<InvalidOperationException>();
        await Assert.That(() => loaded.SetProviderAddress("Resurrected", "2000", GeoCoordinate.Create(1, 2)))
            .Throws<InvalidOperationException>();
        await Assert.That(loaded.Pii).IsNull();
        await Assert.That(context.ChangeTracker.HasChanges()).IsFalse();
    }

    [Test]
    public async Task TrackedNavigationRejectsForeignPiiWithoutChangingEitherAggregate()
    {
        await using var connection = await SqliteTestDatabaseFactory.CreateOpenIsolatedConnectionAsync(Token);
        await LocalIdentitySqliteTemplate.CopySeededApplicationToAsync(connection, Token);
        await using var context = CreateContext(connection);
        Location location = await SeedAsync(context);
        var foreign = NewLocation(location.Tenant!);
        foreign.SetManualAddress("Foreign address", "2000");
        context.Locations.Add(foreign);
        await context.SaveChangesAsync(Token);
        context.ChangeTracker.Clear();
        Location loaded = await context.Locations.SingleAsync(item => item.Id == location.Id, Token);
        Location other = await context.Locations.SingleAsync(item => item.Id == foreign.Id, Token);
        LocationPii rightfulPii = other.Pii!;

        await Assert.That(() => context.Entry(loaded).Reference(item => item.Pii).CurrentValue = other.Pii)
            .Throws<InvalidOperationException>();
        await Assert.That(loaded.Pii).IsNull();
        await Assert.That(loaded.LocationPrivacyStateId).IsEqualTo((int)LocationPrivacyStateEnum.NotProvided);
        await Assert.That(other.Pii!.LocationId).IsEqualTo(other.Id);
        await Assert.That(other.Pii).IsSameReferenceAs(rightfulPii);
        await Assert.That(rightfulPii.Location).IsSameReferenceAs(other);
        await Assert.That(other.Address).IsEqualTo("Foreign address");
        await Assert.That(other.Postcode).IsEqualTo("2000");
        await Assert.That(other.LocationPrivacyStateId).IsEqualTo((int)LocationPrivacyStateEnum.Active);
        await Assert.That(context.ChangeTracker.HasChanges()).IsFalse();
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task TrackedNavigationRejectsErasedAndOwnerlessPrivateHomeAttachments(bool erased)
    {
        await using var connection = await SqliteTestDatabaseFactory.CreateOpenIsolatedConnectionAsync(Token);
        await LocalIdentitySqliteTemplate.CopySeededApplicationToAsync(connection, Token);
        await using var context = CreateContext(connection);
        Location location = await SeedAsync(context, privateHome: erased);
        if (erased)
        {
            location.EraseOwnedPii(DateTime.UnixEpoch, LocationPrivacyErasureReasonEnum.OwnerErasureRequest);
            await context.SaveChangesAsync(Token);
        }
        context.ChangeTracker.Clear();
        Location loaded = await context.Locations.SingleAsync(item => item.Id == location.Id, Token);
        if (!erased)
            context.Entry(loaded).Property(item => item.LocationKindId).CurrentValue = (int)LocationKindEnum.PrivateHome;
        Location candidate = NewLocation(location.Tenant!);
        candidate.Id = loaded.Id;
        candidate.SetManualAddress("Rejected address", "2000");
        var before = context.Entry(loaded).CurrentValues.Clone();

        await Assert.That(() => context.Entry(loaded).Reference(item => item.Pii).CurrentValue = candidate.Pii)
            .Throws<InvalidOperationException>();

        await Assert.That(loaded.Pii).IsNull();
        await Assert.That(before.Properties.Where(property =>
            !Equals(before[property], context.Entry(loaded).CurrentValues[property]))).IsEmpty();
        await Assert.That(context.ChangeTracker.Entries<LocationPii>()).IsEmpty();
    }

    [Test]
    public async Task TrackedActivePiiCannotBeClearedWithoutErasure()
    {
        await using var connection = await SqliteTestDatabaseFactory.CreateOpenIsolatedConnectionAsync(Token);
        await LocalIdentitySqliteTemplate.CopySeededApplicationToAsync(connection, Token);
        await using var context = CreateContext(connection);
        Location location = await SeedAsync(context, privateHome: true);
        context.ChangeTracker.Clear();
        Location loaded = await context.Locations.SingleAsync(item => item.Id == location.Id, Token);
        LocationPii pii = loaded.Pii!;

        await Assert.That(() => context.Entry(loaded).Reference(item => item.Pii).CurrentValue = null)
            .Throws<InvalidOperationException>();

        await Assert.That(loaded.Pii).IsSameReferenceAs(pii);
        await Assert.That(loaded.LocationPrivacyStateId).IsEqualTo((int)LocationPrivacyStateEnum.Active);
        await Assert.That(pii.Location).IsSameReferenceAs(loaded);
        await Assert.That(pii.LocationId).IsEqualTo(loaded.Id);
        await Assert.That(context.ChangeTracker.HasChanges()).IsFalse();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task PromotionRejectsInconsistentTrackedPrivacyOrVisibilityWithoutMutation(int scenario)
    {
        await using var connection = await SqliteTestDatabaseFactory.CreateOpenIsolatedConnectionAsync(Token);
        await LocalIdentitySqliteTemplate.CopySeededApplicationToAsync(connection, Token);
        await using var context = CreateContext(connection);
        Location location = await SeedAsync(context);
        if (scenario != 0)
        {
            location.SetManualAddress("Address", "1000");
            await context.SaveChangesAsync(Token);
        }
        context.ChangeTracker.Clear();
        Location loaded = await context.Locations.SingleAsync(item => item.Id == location.Id, Token);
        if (scenario == 2)
            context.Entry(loaded).Property(item => item.AddressVisibilityId).CurrentValue = 999;
        else
            context.Entry(loaded).Property(item => item.LocationPrivacyStateId).CurrentValue =
                (int)(scenario == 0 ? LocationPrivacyStateEnum.Active : LocationPrivacyStateEnum.NotProvided);
        var before = context.Entry(loaded).CurrentValues.Clone();
        LocationPii? pii = loaded.Pii;

        await Assert.That(() => loaded.PromoteAddressToTenantApproved(Guid.CreateVersion7(), DateTime.UnixEpoch))
            .Throws<InvalidOperationException>();

        await Assert.That(before.Properties.Where(property =>
            !Equals(before[property], context.Entry(loaded).CurrentValues[property]))).IsEmpty();
        await Assert.That(loaded.Pii).IsSameReferenceAs(pii);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task NewPiiUsesItsOwnersAssignedOrGeneratedIdentity(bool assignedIdentity)
    {
        await using var connection = await SqliteTestDatabaseFactory.CreateOpenIsolatedConnectionAsync(Token);
        await LocalIdentitySqliteTemplate.CopySeededApplicationToAsync(connection, Token);
        await using var context = CreateContext(connection);
        Location existing = await SeedAsync(context);
        Location location = NewLocation(existing.Tenant!);
        if (!assignedIdentity)
            location.Id = Guid.Empty;
        location.SetManualAddress("New address", "1000");
        LocationPii pii = location.Pii!;
        context.Locations.Add(location);
        await context.SaveChangesAsync(Token);
        await Assert.That(location.Id).IsNotEqualTo(Guid.Empty);
        await Assert.That(pii.LocationId).IsEqualTo(location.Id);
        await Assert.That(pii.Location).IsSameReferenceAs(location);
        context.ChangeTracker.Clear();

        Location loaded = await context.Locations.SingleAsync(item => item.Id == location.Id, Token);
        await Assert.That(loaded.Pii!.LocationId).IsEqualTo(loaded.Id);
        await Assert.That(loaded.Pii.Location).IsSameReferenceAs(loaded);
        await Assert.That(loaded.LocationPrivacyStateId).IsEqualTo((int)LocationPrivacyStateEnum.Active);
    }

    [Test]
    public async Task ManualReplacementInvalidatesMaterializedApprovalAndOrganizationNavigations()
    {
        await using var connection = await SqliteTestDatabaseFactory.CreateOpenIsolatedConnectionAsync(Token);
        await LocalIdentitySqliteTemplate.CopySeededApplicationToAsync(connection, Token);
        await using var context = CreateContext(connection);
        Location location = await SeedAsync(context);
        var organization = new Organization
        {
            Id = Guid.CreateVersion7(),
            Pii = new OrganizationPii { FullName = "Location organization" }
        };
        context.Set<OrganizationTenant>().Add(new OrganizationTenant
        {
            Id = Guid.CreateVersion7(),
            TenantId = location.TenantId,
            Tenant = location.Tenant!,
            OrganizationId = organization.Id,
            Organization = organization,
            ApprovalStatusId = (int)ApprovalStatusEnum.Approved,
            ApprovalStatus = null!
        });
        location.SetProviderAddress("Provider", "1000", GeoCoordinate.Create(50, 4));
        location.ApplyAddressGovernance(Guid.CreateVersion7(), LocationAddressSourceEnum.ProviderSelection,
            LocationAddressVisibilityEnum.TenantApproved, organization.Id);
        await context.SaveChangesAsync(Token);
        context.ChangeTracker.Clear();
        Location loaded = await context.Locations
            .Include(item => item.AddressSourceLookup)
            .Include(item => item.AddressVisibilityLookup)
            .Include(item => item.AddressOrganizationTenant)
            .SingleAsync(item => item.Id == location.Id, Token);
        await Assert.That(loaded.AddressSourceLookup).IsNotNull();
        await Assert.That(loaded.AddressVisibilityLookup).IsNotNull();
        await Assert.That(loaded.AddressOrganizationTenant).IsNotNull();
        Guid stamp = loaded.ConcurrencyStamp;

        loaded.SetManualAddress("Manual replacement", "2000");

        await Assert.That(loaded.Address).IsEqualTo("Manual replacement");
        await Assert.That(loaded.Postcode).IsEqualTo("2000");
        await Assert.That(loaded.GetCoordinate()).IsNull();
        await Assert.That(loaded.AddressSource).IsEqualTo(LocationAddressSourceEnum.Manual);
        await Assert.That(loaded.AddressVisibility).IsEqualTo(LocationAddressVisibilityEnum.Quarantined);
        await Assert.That(loaded.AddressOrganizationId).IsNull();
        await Assert.That(loaded.AddressSourceLookup).IsNull();
        await Assert.That(loaded.AddressVisibilityLookup).IsNull();
        await Assert.That(loaded.AddressOrganizationTenant).IsNull();
        await Assert.That(loaded.ConcurrencyStamp).IsNotEqualTo(stamp);
        await context.SaveChangesAsync(Token);
        context.ChangeTracker.Clear();
        Location persisted = await context.Locations.SingleAsync(item => item.Id == location.Id, Token);
        await Assert.That(persisted.AddressVisibility).IsEqualTo(LocationAddressVisibilityEnum.Quarantined);
        await Assert.That(persisted.AddressOrganizationId).IsNull();
        await Assert.That(persisted.GetCoordinate()).IsNull();
    }

    [Test]
    [Arguments(nameof(Location.FullName))]
    [Arguments(nameof(Location.Country))]
    [Arguments(nameof(Location.City))]
    public async Task RequiredScalarStorageRejectsNullWithoutChangingPersistedLocation(string propertyName)
    {
        await using var connection = await SqliteTestDatabaseFactory.CreateOpenIsolatedConnectionAsync(Token);
        await LocalIdentitySqliteTemplate.CopySeededApplicationToAsync(connection, Token);
        await using var context = CreateContext(connection);
        Location location = await SeedAsync(context);
        string original = (string)context.Entry(location).Property(propertyName).CurrentValue!;
        context.Entry(location).Property(propertyName).CurrentValue = null;

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(Token));

        context.ChangeTracker.Clear();
        Location loaded = await context.Locations.SingleAsync(item => item.Id == location.Id, Token);
        await Assert.That((string)context.Entry(loaded).Property(propertyName).CurrentValue!).IsEqualTo(original);
        await Assert.That(loaded.Pii).IsNull();
        await Assert.That(loaded.LocationPrivacyStateId).IsEqualTo((int)LocationPrivacyStateEnum.NotProvided);
    }

    private static CancellationToken Token => TestContext.Current!.Execution.CancellationToken;

    private static ExploreDbContext CreateContext(Microsoft.Data.Sqlite.SqliteConnection connection)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(connection).UseSnakeCaseNamingConvention().Options;
        var context = new ExploreDbContext(options);
        context.EnableTenantFilterBypass("Owned isolated Location materialization fixture.");
        context.BindTenantLease();
        return context;
    }

    private static async Task<Location> SeedAsync(ExploreDbContext context, bool privateHome = false)
    {
        await using var humanGate = new HumanSecuritySourceGate(context);
        await using var human = await humanGate.AcquireAsync(Token);
        await using var discoveryGate = new EventDiscoveryEnrollmentGate(context);
        await using var discovery = await discoveryGate.AcquireAsync(Token);
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = "Location materialization tenant",
            Slug = $"location-{Guid.CreateVersion7():N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        Location location = NewLocation(tenant);
        if (privateHome)
        {
            var owner = new User
            {
                Id = Guid.CreateVersion7(),
                Pii = new UserPii { Email = "location-owner@example.test", FirstName = "Location", LastName = "Owner" }
            };
            context.Users.Add(owner);
            location.ClassifyAsPrivateHome(owner.Id);
            location.SetProviderAddress("Private address", "1000", GeoCoordinate.Create(50, 4));
        }
        context.Locations.Add(location);
        await context.SaveChangesAsync(Token);
        return location;
    }

    private static Location NewLocation(Tenant tenant) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenant.Id,
        Tenant = tenant,
        FullName = "Materialized venue",
        Country = "BE",
        City = "Brussels"
    };
}
