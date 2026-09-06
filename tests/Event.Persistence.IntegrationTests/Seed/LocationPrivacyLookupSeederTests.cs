// ABOUTME: Verifies EF model and runtime-seeder parity for event-location privacy lookups.
// ABOUTME: Covers exact IDs/codes, missing-row repair, idempotency, and the model HasData prohibition.

using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Seed;

[Category("EventLocationPrivacyLookup")]
public sealed class LocationPrivacyLookupSeederTests
{
    [Test]
    public async Task EfModelUsesNormalizedLookupTablesWithoutModelSeedData()
    {
        await using var context = CreateModelContext();

        await AssertLookupModelAsync<LocationKind>(context, "location_kinds");
        await AssertLookupModelAsync<LocationPrivacyState>(context, "location_privacy_states");
        await AssertLookupModelAsync<LocationDisclosureAudience>(context, "location_disclosure_audiences");
        await AssertLookupModelAsync<LocationAddressSource>(context, "location_address_sources");
        await AssertLookupModelAsync<LocationAddressVisibility>(context, "location_address_visibilities");
    }

    [Test]
    public async Task PublicRuntimeSeederSeedsExactLocationPrivacyRows()
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseTestInMemoryDatabase($"event-location-privacy-public-seeder-{Guid.NewGuid():N}")
            .Options;

        await using var context = new ExploreDbContext(options);
        await LookupTableSeeder.SeedAsync(context);

        await AssertExactRowsAsync(context);
    }

    [Test]
    public async Task RuntimeSeederRepairsMissingRowsAndRemainsIdempotent()
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseTestInMemoryDatabase($"event-location-privacy-lookups-{Guid.NewGuid():N}")
            .Options;

        await using var context = new ExploreDbContext(options);
        await LookupTableSeeder.SeedLocationPrivacyLookupsAsync(context, default);
        await LookupTableSeeder.SeedLocationAddressGovernanceLookupsAsync(context, default);

        await AssertExactRowsAsync(context);

        context.LocationKinds.Remove(await context.LocationKinds.SingleAsync(row =>
            row.Id == (int)LocationKindEnum.CommercialVenue));
        context.LocationPrivacyStates.Remove(await context.LocationPrivacyStates.SingleAsync(row =>
            row.Id == (int)LocationPrivacyStateEnum.Active));
        context.LocationDisclosureAudiences.Remove(await context.LocationDisclosureAudiences.SingleAsync(row =>
            row.Id == (int)LocationDisclosureAudienceEnum.ConfirmedParticipant));
        context.LocationAddressSources.Remove(await context.LocationAddressSources.SingleAsync(row =>
            row.Id == (int)LocationAddressSourceEnum.Manual));
        context.LocationAddressVisibilities.Remove(await context.LocationAddressVisibilities.SingleAsync(row =>
            row.Id == (int)LocationAddressVisibilityEnum.OrganizationScoped));
        await context.SaveChangesAsync();

        await LookupTableSeeder.SeedLocationPrivacyLookupsAsync(context, default);
        await LookupTableSeeder.SeedLocationAddressGovernanceLookupsAsync(context, default);
        await LookupTableSeeder.SeedLocationPrivacyLookupsAsync(context, default);
        await LookupTableSeeder.SeedLocationAddressGovernanceLookupsAsync(context, default);

        await AssertExactRowsAsync(context);
    }

    private static ExploreDbContext CreateModelContext()
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql("Host=localhost;Database=location_privacy_model;Username=unused;Password=unused")
            .UseSnakeCaseNamingConvention()
            .Options;

        return new ExploreDbContext(options);
    }

    private static async Task AssertLookupModelAsync<TLookup>(ExploreDbContext context, string tableName)
        where TLookup : class
    {
        var entityType = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(TLookup))!;

        await Assert.That(entityType.GetTableName()).IsEqualTo(tableName);
        await Assert.That(entityType.FindPrimaryKey()!.Properties.Single().ClrType).IsEqualTo(typeof(int));
        await Assert.That(entityType.FindProperty("Id")!.ValueGenerated).IsEqualTo(ValueGenerated.Never);
        await Assert.That(entityType.FindProperty("MasterCode")!.GetMaxLength()).IsEqualTo(100);
        await Assert.That(entityType.FindProperty("FullName")!.GetMaxLength()).IsEqualTo(200);
        await Assert.That(entityType.FindProperty("Description")!.GetMaxLength()).IsEqualTo(500);
        await Assert.That(entityType.GetIndexes().Any(index =>
            index.IsUnique && index.Properties.Select(property => property.Name).SequenceEqual(["MasterCode"]))).IsTrue();
        await Assert.That(entityType.GetSeedData().Count).IsEqualTo(0);
    }

    private static async Task AssertExactRowsAsync(ExploreDbContext context)
    {
        var kinds = await context.LocationKinds
            .OrderBy(row => row.Id)
            .Select(row => new { row.Id, row.MasterCode })
            .ToArrayAsync();
        var states = await context.LocationPrivacyStates
            .OrderBy(row => row.Id)
            .Select(row => new { row.Id, row.MasterCode })
            .ToArrayAsync();
        var audiences = await context.LocationDisclosureAudiences
            .OrderBy(row => row.Id)
            .Select(row => new { row.Id, row.MasterCode })
            .ToArrayAsync();
        var sources = await context.LocationAddressSources
            .OrderBy(row => row.Id)
            .Select(row => new { row.Id, row.MasterCode })
            .ToArrayAsync();
        var visibilities = await context.LocationAddressVisibilities
            .OrderBy(row => row.Id)
            .Select(row => new { row.Id, row.MasterCode })
            .ToArrayAsync();

        await Assert.That(kinds.Select(row => (row.Id, row.MasterCode)).SequenceEqual(
        [
            (1, "UNCLASSIFIED"),
            (2, "COMMERCIAL_VENUE"),
            (3, "PUBLIC_SPACE"),
            (4, "COMMUNITY_VENUE"),
            (5, "PRIVATE_HOME")
        ])).IsTrue();
        await Assert.That(states.Select(row => (row.Id, row.MasterCode)).SequenceEqual(
        [
            (1, "NOT_PROVIDED"),
            (2, "ACTIVE"),
            (3, "ERASED")
        ])).IsTrue();
        await Assert.That(audiences.Select(row => (row.Id, row.MasterCode)).SequenceEqual(
        [
            (1, "NEVER"),
            (2, "ANY_CURRENT_REGISTRANT"),
            (3, "CONFIRMED_PARTICIPANT")
        ])).IsTrue();
        await Assert.That(sources.Select(row => (row.Id, row.MasterCode)).SequenceEqual(
        [
            (1, "UNKNOWN_LEGACY"),
            (2, "MANUAL"),
            (3, "PROVIDER_SELECTION")
        ])).IsTrue();
        await Assert.That(visibilities.Select(row => (row.Id, row.MasterCode)).SequenceEqual(
        [
            (1, "QUARANTINED"),
            (2, "CREATOR_PRIVATE"),
            (3, "ORGANIZATION_SCOPED"),
            (4, "TENANT_APPROVED")
        ])).IsTrue();
    }
}
