using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Federation;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Schema;
using Explore.Persistence.Seed;
using Explore.Secrets.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using TUnit.Assertions.Enums;

namespace Event.Persistence.IntegrationTests.Migrations;

public sealed class EventDiscoveryTraversalLifecycleTests
{
    [Test]
    public async Task GeneratedTraversal_UpDownUp_BackfillsExistingLocalAndSuppressedRemoteSources()
    {
        string path = Path.Combine(Path.GetTempPath(), $"discovery-traversal-{Guid.CreateVersion7():N}.db");
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, new PrimaryDatabaseConnectionOptions
        {
            Role = PrimaryDatabaseRole.Migrator,
            Provider = PrimaryDatabaseProvider.Sqlite,
            Database = path
        });
        var context = new ExploreDbContext(options.Options);
        var connection = (SqliteConnection)context.Database.GetDbConnection();

        try
        {
            IMigrator migrator = context.GetService<IMigrator>();
            string[] migrations = context.Database.GetMigrations().ToArray();
            string traversal = migrations.Single(id => id.EndsWith("_EventDiscoveryTraversal", StringComparison.Ordinal));
            int traversalIndex = Array.IndexOf(migrations, traversal);
            await Assert.That(traversalIndex).IsGreaterThan(0);
            string predecessor = migrations[traversalIndex - 1];

            await migrator.MigrateAsync(traversal);
            await Assert.That(await context.Database.GetAppliedMigrationsAsync())
                .IsEquivalentTo(migrations.Take(traversalIndex + 1), CollectionOrdering.Matching);
            await AssertTraversalSchemaAsync(context, present: true);
            await LookupTableSeeder.SeedAsync(context);

            Guid tenantId = Guid.CreateVersion7();
            Guid userId = Guid.CreateVersion7();
            Guid actorId = Guid.CreateVersion7();
            context.TenantContext = new FixedTenantContext(tenantId);
            context.AddRange(
                new Tenant
                {
                    Id = tenantId, FullName = "Traversal migration tenant", Slug = $"traversal-{tenantId:N}",
                    TenantStatusId = (int)TenantStatusEnum.Active, TenantStatus = null!
                },
                new User
                {
                    Id = userId,
                    Pii = new UserPii { Email = string.Empty, FirstName = "Migration", LastName = "Owner" }
                },
                new Actor
                {
                    Id = actorId, UserId = userId, ActorTypeId = (int)ActorTypeEnum.User, ActorType = null!,
                    Pii = new ActorPii { DisplayName = "Migration owner" }
                });
            await context.SaveChangesAsync();

            var now = new DateTimeOffset(2028, 6, 15, 12, 0, 0, TimeSpan.Zero);
            foreach (bool suppressed in new[] { false, true })
            {
                context.Events.Add(new Explore.Domain.Event(EventStatusEnum.Draft)
                {
                    Id = Guid.CreateVersion7(), TenantId = tenantId, Tenant = null!,
                    Title = suppressed ? "\u00e9 " : "a", IsDeleted = suppressed,
                    ActorId = actorId, Actor = null!, OrganizerActorId = actorId,
                    EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
                    VisibilityTypeId = (int)VisibilityTypeEnum.Public, VisibilityType = null!,
                    EventFormatId = (int)EventFormatEnum.Local, EventFormat = null!, EventStatus = null!,
                    PublicCode = Guid.CreateVersion7().ToString("N"), Timezone = "UTC", CreatedAt = now.UtcDateTime
                });
                Guid recordId = Guid.CreateVersion7();
                string did = $"did:plc:{recordId:N}";
                context.AddRange(new AtprotoRecord
                {
                    Id = recordId, Did = did, Collection = "community.lexicon.calendar.event",
                    RecordKey = recordId.ToString("N"), Cid = "bafy-source-test",
                    Uri = $"at://{did}/community.lexicon.calendar.event/{recordId:N}",
                    Direction = AtprotoRecordDirection.Inbound, Provenance = AtprotoRecordProvenance.Jetstream,
                    SourceVersion = 1, RecordJson = "{}", RecordHash = new string('a', 64),
                    IndexedAt = now.UtcDateTime, UpdatedAt = now.UtcDateTime,
                    TombstonedAt = suppressed ? now.UtcDateTime : null
                }, new AtprotoEventProjection
                {
                    AtprotoRecordId = recordId, Name = suppressed ? "z" : "remote",
                    CreatedAt = now, StartsAt = now, EndsAt = now.AddHours(1),
                    SourceVersion = 1, MaterializedAt = now.UtcDateTime
                });
            }
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            // Capture persisted IDs, titles and expected ranks before Down drops the columns.
            RankRow[] expectedLocal = await ReadLocalRanksAsync(context);
            RankRow[] expectedRemote = await ReadRemoteRanksAsync(context);
            await Assert.That(expectedLocal.Length).IsEqualTo(2);
            await Assert.That(expectedRemote.Length).IsEqualTo(2);
            await Assert.That(expectedLocal.Single(row => row.Title == "a").TitleKey).IsEqualTo("0041");
            await Assert.That(expectedLocal.Single(row => row.Title == "\u00e9 ").TitleKey).IsEqualTo("00C90020");
            await Assert.That(expectedRemote.Single(row => row.Title == "remote").TitleKey)
                .IsEqualTo("00520045004D004F00540045");
            await Assert.That(expectedRemote.Single(row => row.Title == "z").TitleKey).IsEqualTo("005A");
            foreach (RankRow row in expectedLocal.Concat(expectedRemote))
                await Assert.That(row.SourceKey).IsEqualTo(row.Id.ToString("N"));
            Guid deletedEventId = expectedLocal.Single(row => row.Title == "\u00e9 ").Id;
            Guid tombstonedRecordId = expectedRemote.Single(row => row.Title == "z").Id;

            await migrator.MigrateAsync(predecessor);
            await Assert.That(await context.Database.GetAppliedMigrationsAsync())
                .IsEquivalentTo(migrations.Take(traversalIndex), CollectionOrdering.Matching);
            await AssertTraversalSchemaAsync(context, present: false);
            await Assert.That(await context.Database.SqlQueryRaw<int>(
                """SELECT COUNT(*) AS "Value" FROM ie_events""").SingleAsync()).IsEqualTo(2);
            await Assert.That(await context.Database.SqlQueryRaw<int>(
                """SELECT COUNT(*) AS "Value" FROM ie_atproto_event_projections""").SingleAsync()).IsEqualTo(2);

            await migrator.MigrateAsync(traversal);
            await Assert.That(await context.Database.GetAppliedMigrationsAsync())
                .IsEquivalentTo(migrations.Take(traversalIndex + 1), CollectionOrdering.Matching);
            await AssertTraversalSchemaAsync(context, present: true);
            RankRow[] unfilledLocal = await ReadLocalRanksAsync(context);
            RankRow[] unfilledRemote = await ReadRemoteRanksAsync(context);
            await Assert.That(unfilledLocal.Length).IsEqualTo(expectedLocal.Length);
            await Assert.That(unfilledRemote.Length).IsEqualTo(expectedRemote.Length);
            await Assert.That(unfilledLocal.Concat(unfilledRemote)
                .All(row => row.TitleKey == string.Empty && row.SourceKey == string.Empty)).IsTrue();

            // Scalar projections never invoke Title/Name or ID setters, which could mask
            // missing database backfill by computing correct keys during materialization.
            var configuration = new ConfigurationManager();
            await ExploreDatabaseMigrator.MigrateAsync(context, configuration);
            await Assert.That(await ReadLocalRanksAsync(context)).IsEquivalentTo(expectedLocal);
            await Assert.That(await ReadRemoteRanksAsync(context)).IsEquivalentTo(expectedRemote);
            await Assert.That(await context.Events.IgnoreQueryFilters([QueryFilterNames.SoftDelete])
                .Where(row => row.Id == deletedEventId).Select(row => row.IsDeleted).SingleAsync()).IsTrue();
            await Assert.That(await context.AtprotoRecords.Where(row => row.Id == tombstonedRecordId)
                .Select(row => row.TombstonedAt).SingleAsync()).IsEqualTo(now.UtcDateTime);
            await Assert.That(await context.Database.GetAppliedMigrationsAsync())
                .IsEquivalentTo(migrations, CollectionOrdering.Matching);

            await ExploreDatabaseMigrator.MigrateAsync(context, configuration);
            await Assert.That(await ReadLocalRanksAsync(context)).IsEquivalentTo(expectedLocal);
            await Assert.That(await ReadRemoteRanksAsync(context)).IsEquivalentTo(expectedRemote);
        }
        finally
        {
            await context.DisposeAsync();
            SqliteConnection.ClearPool(connection);
            File.Delete(path);
            File.Delete(path + "-wal");
            File.Delete(path + "-shm");
            File.Delete(path + "-journal");
        }
    }

    private static Task<RankRow[]> ReadLocalRanksAsync(ExploreDbContext context) =>
        context.Events.IgnoreQueryFilters([QueryFilterNames.SoftDelete]).AsNoTracking()
            .Select(row => new RankRow(row.Id, row.Title, row.DiscoveryTitleSortKey, row.DiscoverySourceSortKey))
            .ToArrayAsync();

    private static Task<RankRow[]> ReadRemoteRanksAsync(ExploreDbContext context) =>
        context.AtprotoEventProjections.AsNoTracking()
            .Select(row => new RankRow(row.AtprotoRecordId, row.Name,
                row.DiscoveryTitleSortKey, row.DiscoverySourceSortKey))
            .ToArrayAsync();

    private static async Task AssertTraversalSchemaAsync(ExploreDbContext context, bool present)
    {
        int tableCount = await context.Database.SqlQueryRaw<int>(
            """
            SELECT COUNT(*) AS "Value" FROM sqlite_schema
            WHERE type = 'table' AND name IN (
                'ie_event_discovery_snapshot_reservations',
                'ie_event_discovery_snapshots',
                'ie_event_discovery_snapshot_items')
            """).SingleAsync();
        await Assert.That(tableCount).IsEqualTo(present ? 3 : 0);
        int columnCount = await context.Database.SqlQueryRaw<int>(
            """
            SELECT (
                (SELECT COUNT(*) FROM pragma_table_info('ie_events')
                 WHERE name IN ('discovery_title_sort_key', 'discovery_source_sort_key'))
                +
                (SELECT COUNT(*) FROM pragma_table_info('ie_atproto_event_projections')
                 WHERE name IN ('discovery_title_sort_key', 'discovery_source_sort_key'))
            ) AS "Value"
            """).SingleAsync();
        await Assert.That(columnCount).IsEqualTo(present ? 4 : 0);
    }

    private sealed record RankRow(Guid Id, string Title, string TitleKey, string SourceKey);
    private sealed record FixedTenantContext(Guid TenantId) : ITenantContext;
}
