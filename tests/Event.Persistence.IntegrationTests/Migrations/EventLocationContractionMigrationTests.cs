#nullable enable

using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Scheduling;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Schema;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Migrations;

[ClassDataSource<RecipientDeliveryMigrationContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("RecipientDeliveryMigrationDb")]
[Property("Category", "EventLocationPrivacy")]
public sealed class EventLocationContractionMigrationTests(RecipientDeliveryMigrationContainerFixture fixture)
{
    private const string ContractionSql = "location_id IS NULL OR event_location_id IS NOT NULL";

    /// <summary>Carrier types whose finalized relational metadata owns the physical constraint identity.</summary>
    private static readonly Type[] Carriers =
    [
        typeof(EventSession),
        typeof(EventSessionGroup),
        typeof(EventAgendaItem),
        typeof(EventSessionAgendaItem)
    ];

    [Test]
    public async Task ContractionMigration_IsPartOfTheCurrentMigrationSet()
    {
        await WithMigratedDatabaseAsync(async (context, _) =>
        {
            string[] applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();

            await Assert.That(applied).IsNotEmpty();
            await Assert.That(applied).IsEquivalentTo(context.Database.GetMigrations());
        });
    }

    [Test]
    public async Task EveryCarrierCarriesTheMediationConstraintAfterMigration()
    {
        await WithMigratedDatabaseAsync(async (context, connectionString) =>
        {
            foreach (Type carrier in Carriers)
            {
                (string table, string constraint) = GetPhysicalConstraint(context, carrier);
                bool exists = await ConstraintExistsAsync(connectionString, table, constraint);
                await Assert.That(exists).IsTrue();
            }
        });
    }

    [Test]
    public async Task ZeroGapHolds_NoExistingRowReferencesAPhysicalVenueWithoutAnEventLocation()
    {
        await WithMigratedDatabaseAsync(async (context, connectionString) =>
        {
            await EventLocationContractionAssertions.AssertStoreAsync(context, CreateDatabaseOptions(connectionString));
            foreach (Type carrier in Carriers)
            {
                (string table, _) = GetPhysicalConstraint(context, carrier);
                long orphans = await ScalarAsync(
                    connectionString,
                    $"""
                     SELECT COUNT(*) FROM islamu_event."{table}"
                     WHERE location_id IS NOT NULL AND event_location_id IS NULL
                     """);

                await Assert.That(orphans).IsEqualTo(0L);
            }
        });
    }

    [Test]
    public async Task ContractionMigration_IsReversibleInDevelopment()
    {
        await WithMigratedDatabaseAsync(async (context, connectionString) =>
        {
            string[] migrations = context.Database.GetMigrations().ToArray();
            await Assert.That(migrations).IsNotEmpty();
            await EventLocationContractionAssertions.AssertStoreAsync(context, CreateDatabaseOptions(connectionString));

            await context.Database
                .GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>()
                .MigrateAsync("0");
            await Assert.That(await context.Database.GetAppliedMigrationsAsync()).IsEmpty();

            foreach (Type carrier in Carriers)
            {
                (string table, string constraint) = GetPhysicalConstraint(context, carrier);
                await Assert.That(await ConstraintExistsAsync(connectionString, table, constraint)).IsFalse();
            }

            // Re-applying restores the guarantee, so a rollback is never a one-way door.
            await context.Database.MigrateAsync();
            await Assert.That(await context.Database.GetAppliedMigrationsAsync()).IsEquivalentTo(migrations);
            await EventLocationContractionAssertions.AssertStoreAsync(context, CreateDatabaseOptions(connectionString));
            foreach (Type carrier in Carriers)
            {
                (string table, string constraint) = GetPhysicalConstraint(context, carrier);
                await Assert.That(await ConstraintExistsAsync(connectionString, table, constraint)).IsTrue();
            }
        });
    }

    private async Task WithMigratedDatabaseAsync(Func<ExploreDbContext, string, Task> action)
    {
        string databaseName = $"elp_contraction_{Guid.NewGuid():N}";
        string connectionString = await CreateDatabaseAsync(databaseName);
        try
        {
            await using ExploreDbContext context = CreateContext(connectionString);
            await context.Database.MigrateAsync();
            await action(context, connectionString);
        }
        finally
        {
            await DropDatabaseAsync(databaseName);
        }
    }

    private static async Task<bool> ConstraintExistsAsync(
        string connectionString,
        string table,
        string constraint)
    {
        long count = await ScalarAsync(
            connectionString,
            $"""
             SELECT COUNT(*)
             FROM pg_constraint c
             JOIN pg_class t ON t.oid = c.conrelid
             JOIN pg_namespace n ON n.oid = t.relnamespace
             WHERE c.contype = 'c'
               AND n.nspname = 'islamu_event'
               AND t.relname = '{table}'
               AND c.conname = '{constraint}'
             """);

        return count == 1;
    }

    private static (string Table, string Constraint) GetPhysicalConstraint(
        ExploreDbContext context,
        Type carrier)
    {
        var model = context
            .GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>()
            .Model;
        var entityType = model.FindEntityType(carrier)
            ?? throw new InvalidOperationException($"No EF metadata exists for {carrier.Name}.");
        var constraint = entityType.GetCheckConstraints()
            .Single(candidate => string.Equals(candidate.Sql, ContractionSql, StringComparison.Ordinal));
        return (entityType.GetTableName()!, constraint.Name
            ?? throw new InvalidOperationException("The physical reference CHECK must have a relational name."));
    }

    private static async Task<long> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static ExploreDbContext CreateContext(string connectionString)
    {
        var builder = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder, CreateDatabaseOptions(connectionString));
        return new ExploreDbContext(builder.Options);
    }

    private static PrimaryDatabaseConnectionOptions CreateDatabaseOptions(string connectionString)
    {
        var connection = new NpgsqlConnectionStringBuilder(connectionString);
        return new PrimaryDatabaseConnectionOptions
        {
            Role = PrimaryDatabaseRole.Migrator,
            Provider = PrimaryDatabaseProvider.PostgreSql,
            Host = connection.Host,
            Port = connection.Port,
            Database = connection.Database,
            Schema = RelationalModelNamespace.DefaultSchema,
            Username = connection.Username,
            Password = connection.Password,
            TlsMode = PrimaryDatabaseTlsMode.Disabled,
        };
    }

    private async Task<string> CreateDatabaseAsync(string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            Database = "postgres",
            Pooling = false
        };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
        await command.ExecuteNonQueryAsync();
        builder.Database = databaseName;
        return builder.ConnectionString;
    }

    private async Task DropDatabaseAsync(string databaseName)
    {
        NpgsqlConnection.ClearAllPools();
        var builder = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            Database = "postgres",
            Pooling = false
        };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var terminate = new NpgsqlCommand(
            $"""
             SELECT pg_terminate_backend(pid) FROM pg_stat_activity
             WHERE datname = '{databaseName}' AND pid <> pg_backend_pid()
             """,
            connection);
        await terminate.ExecuteNonQueryAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\"", connection);
        await drop.ExecuteNonQueryAsync();
    }
}

internal static class EventLocationContractionAssertions
{
    internal sealed record Carrier(Type Type, string LogicalName, string PostgreSqlName, string MySqlName)
    {
        internal string PhysicalName(string provider) => provider switch
        {
            "PostgreSql" => PostgreSqlName,
            "MariaDb" or "MySql" => MySqlName,
            _ => LogicalName
        };
    }

    internal static readonly Carrier[] Carriers =
    [
        new(typeof(EventSession),
            "ck_event_session_physical_location_requires_event_location",
            "ck_event_session_physical_location_requires_event_location",
            "ck_event_session_physical_location_requires_event_location"),
        new(typeof(EventSessionGroup),
            "ck_event_session_group_physical_location_requires_event_location",
            "ck_event_session_group_physical_location_requires__00aee3b75cbc",
            "ck_event_session_group_physical_location_requires_event_location"),
        new(typeof(EventAgendaItem),
            "ck_event_agenda_item_physical_location_requires_event_location",
            "ck_event_agenda_item_physical_location_requires_event_location",
            "ck_event_agenda_item_physical_location_requires_event_location"),
        new(typeof(EventSessionAgendaItem),
            "ck_event_session_agenda_item_physical_location_requires_event_location",
            "ck_event_session_agenda_item_physical_location_req_ed2c19b42b1b",
            "ck_event_session_agenda_item_physical_location_requ_5f3382b974de")
    ];

    internal static async Task AssertStoreAsync(
        ExploreDbContext migratedContext, PrimaryDatabaseConnectionOptions databaseOptions)
    {
        await Assert.That(databaseOptions.Role).IsEqualTo(PrimaryDatabaseRole.Migrator);
        // Migrator-only storage setup uses real domain values and the final model, without claiming Runtime custody or RLS coverage.
        var options = TestDbContextOptions.Create();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, databaseOptions);
        options.UseModel(migratedContext.Model);
        await using var storage = new DbContext(options.Options);
        await SeedLookupsAsync(storage);
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(), FullName = "Contraction tenant",
            Slug = $"contraction-{Guid.CreateVersion7():N}", TenantStatusId = 2, TenantStatus = null!
        };
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Pii = new UserPii
            {
                Email = $"contraction-{Guid.CreateVersion7():N}@example.com", FirstName = "Schema", LastName = "Control"
            }
        };
        var actor = new Actor
        {
            Id = Guid.CreateVersion7(), UserId = user.Id, User = user,
            ActorTypeId = 1, ActorType = null!, Pii = new ActorPii { DisplayName = "Contraction actor" }
        };
        var @event = new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(), Title = "Contraction event", TenantId = tenant.Id, Tenant = tenant,
            ActorId = actor.Id, Actor = actor, EventStatus = null!, EventProvenanceTypeId = 1,
            EventFormatId = 1, EventFormat = null!, VisibilityTypeId = 2, VisibilityType = null!,
            PublicCode = Guid.CreateVersion7().ToString("N")[^12..]
        };
        var location = new Location
        {
            Id = Guid.CreateVersion7(), TenantId = tenant.Id, Tenant = tenant,
            FullName = "Contraction venue", Country = "BE", City = "Brussels"
        };
        EventLocation mediation = EventLocation.CreatePhysical(
            tenant.Id, @event.Id, location.Id, user.Id, DateTime.UtcNow);
        storage.AddRange(tenant, user, actor, @event, location, mediation);

        var carriers = new List<object>();
        DateTimeOffset start = new(2026, 7, 19, 11, 0, 0, TimeSpan.Zero);
        foreach (bool physical in new[] { false, true })
        {
            var session = new EventSession(EventSessionStatusEnum.Draft)
            {
                Id = Guid.CreateVersion7(), TenantId = tenant.Id, Tenant = tenant,
                EventId = @event.Id, Event = @event, Title = "Contraction session"
            };
            var group = new EventSessionGroup
            {
                Id = Guid.CreateVersion7(), TenantId = tenant.Id, Tenant = tenant,
                EventId = @event.Id, Event = @event, Name = "Contraction group"
            };
            var agenda = new EventAgendaItem
            {
                Id = Guid.CreateVersion7(), TenantId = tenant.Id, Tenant = tenant,
                EventId = @event.Id, Event = @event, Title = "Contraction agenda"
            };
            agenda.Reschedule(UtcInstantRange.Create(start, start.AddHours(1)), "UTC", new EventScheduleProjectionCalculator());
            var sessionAgenda = new EventSessionAgendaItem
            {
                Id = Guid.CreateVersion7(), TenantId = tenant.Id, Tenant = tenant,
                EventSessionId = session.Id, EventSession = session, Title = "Contraction session agenda",
                StartTime = start, EndTime = start.AddMinutes(30)
            };
            if (physical)
            {
                session.AssignEventLocation(mediation);
                group.AssignEventLocation(mediation);
                agenda.AssignEventLocation(mediation);
                sessionAgenda.AssignEventLocation(mediation);
            }
            carriers.AddRange([session, group, agenda, sessionAgenda]);
        }
        storage.AddRange(carriers);
        await storage.SaveChangesAsync();

        var before = new List<object?[]>();
        foreach (object carrier in carriers)
        {
            var values = (await storage.Entry(carrier).GetDatabaseValuesAsync())!;
            bool physical = before.Count >= Carriers.Length;
            await Assert.That(values[nameof(EventSession.LocationId)]).IsEqualTo(physical ? (object)location.Id : null);
            await Assert.That(values[nameof(EventSession.EventLocationId)]).IsEqualTo(physical ? (object)mediation.Id : null);
            before.Add(values.Properties.Select(property => values[property]).ToArray());
        }
        for (int index = 0; index < Carriers.Length; index++)
        {
            var entry = storage.Entry(carriers[index + Carriers.Length]);
            IEntityType entity = entry.Metadata;
            var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            ISqlGenerationHelper sql = storage.GetService<ISqlGenerationHelper>();
            string table = sql.DelimitIdentifier(store.Name, store.Schema);
            string column = sql.DelimitIdentifier(entity.FindProperty(nameof(EventSession.EventLocationId))!.GetColumnName(store)!);
            string key = sql.DelimitIdentifier(entity.FindProperty(nameof(EventSession.Id))!.GetColumnName(store)!);
            string update = $"UPDATE {table} SET {column} = NULL WHERE {key} = {{0}}";
            object id = entry.Property(nameof(EventSession.Id)).CurrentValue!;
            string expectedConstraint = Carriers[index].PhysicalName(databaseOptions.Provider.ToString());
            Exception? failure = null;
            await using (var transaction = await storage.Database.BeginTransactionAsync())
            {
                try
                {
                    // The valid row already references a real venue; clearing mediation attacks only the storage CHECK.
                    await storage.Database.ExecuteSqlRawAsync(update, id);
                }
                catch (Exception exception) when (exception is PostgresException or SqliteException or SqlException or MySqlException)
                {
                    failure = exception;
                }
                await transaction.RollbackAsync();
            }
            await Assert.That(failure).IsNotNull().Because($"{databaseOptions.Provider} must reject {expectedConstraint}");
            switch (failure)
            {
                case PostgresException postgres:
                    await Assert.That(postgres.SqlState).IsEqualTo(PostgresErrorCodes.CheckViolation);
                    await Assert.That(postgres.ConstraintName).IsEqualTo(expectedConstraint);
                    break;
                case SqliteException sqlite:
                    await Assert.That(sqlite.SqliteExtendedErrorCode).IsEqualTo(275);
                    await Assert.That(sqlite.Message).Contains(expectedConstraint);
                    break;
                case SqlException sqlServer:
                    await Assert.That(sqlServer.Number).IsEqualTo(547);
                    await Assert.That(sqlServer.Message).Contains(expectedConstraint);
                    break;
                case MySqlException mySql:
                    await Assert.That(mySql.Number).IsEqualTo(databaseOptions.Provider == PrimaryDatabaseProvider.MariaDb ? 4025 : 3819);
                    await Assert.That(mySql.Message).Contains(expectedConstraint);
                    break;
            }
            for (int row = 0; row < carriers.Count; row++)
            {
                var values = (await storage.Entry(carriers[row]).GetDatabaseValuesAsync())!;
                await Assert.That(values.Properties.Select(property => values[property]))
                    .IsEquivalentTo(before[row], TUnit.Assertions.Enums.CollectionOrdering.Matching);
            }
        }
    }

    private static async Task SeedLookupsAsync(DbContext storage)
    {
        await EnsureAsync(new TenantStatus { Id = 2, MasterCode = "active", FullName = "Active", IsActiveState = true }, 2);
        await EnsureAsync(new ActorType { Id = 1, MasterCode = "user", FullName = "User" }, 1);
        await EnsureAsync(new EventStatus { Id = 1, MasterCode = "draft", FullName = "Draft" }, 1);
        await EnsureAsync(new EventSessionStatus { Id = 1, MasterCode = "draft", FullName = "Draft" }, 1);
        await EnsureAsync(new EventFormat { Id = 1, MasterCode = "local", FullName = "Local" }, 1);
        await EnsureAsync(new VisibilityType { Id = 2, MasterCode = "private", FullName = "Private" }, 2);
        await EnsureAsync(new EventProvenanceType { Id = 1, MasterCode = "organizer_created", FullName = "Organizer created" }, 1);
        await EnsureAsync(new LocationKind { Id = 1, MasterCode = "unclassified", FullName = "Unclassified" }, 1);
        await EnsureAsync(new LocationPrivacyState { Id = 1, MasterCode = "not_provided", FullName = "Not provided" }, 1);
        await EnsureAsync(new LocationAddressSource { Id = 1, MasterCode = "unknown_legacy", FullName = "Unknown legacy" }, 1);
        await EnsureAsync(new LocationAddressVisibility { Id = 1, MasterCode = "quarantined", FullName = "Quarantined" }, 1);
        await EnsureAsync(new LocationDisclosureAudience { Id = 1, MasterCode = "never", FullName = "Never" }, 1);
        await storage.SaveChangesAsync();

        async Task EnsureAsync<T>(T value, int id) where T : class
        {
            if (await storage.Set<T>().FindAsync(id) is null)
                storage.Add(value);
        }
    }
}
