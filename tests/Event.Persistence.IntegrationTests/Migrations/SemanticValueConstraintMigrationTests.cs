#nullable enable

using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Seed;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Migrations;

[ClassDataSource<RecipientDeliveryMigrationContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("RecipientDeliveryMigrationDb")]
public sealed class SemanticValueConstraintMigrationTests(
    RecipientDeliveryMigrationContainerFixture fixture)
{
    private const string MigrationSuffix = "Init";
    private const string ProviderTablePrefix = "ie_";

    private static readonly string[] ExpectedConstraintIdentities =
    [
        "event_agenda_items.ck_event_agenda_item_local_date_range",
        "event_sessions.ck_event_session_local_date_range",
        "event_ticket_types.ck_event_ticket_type_money_nonnegative",
        "location_pii.ck_location_pii_coordinate_shape"
    ];

    private static readonly IReadOnlyDictionary<string, string> ExpectedConstraintSqlByIdentity =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["event_agenda_items.ck_event_agenda_item_local_date_range"] =
                "local_end_date >= local_start_date",
            ["event_sessions.ck_event_session_local_date_range"] =
                "local_end_date IS NULL OR local_start_date IS NULL OR local_end_date >= local_start_date",
            ["event_ticket_types.ck_event_ticket_type_money_nonnegative"] =
                """
                (fixed_price_minor IS NULL OR fixed_price_minor >= 0)
                AND (minimum_price_minor IS NULL OR minimum_price_minor >= 0)
                AND (suggested_price_minor IS NULL OR suggested_price_minor >= 0)
                """,
            ["location_pii.ck_location_pii_coordinate_shape"] =
                """
                (latitude IS NULL AND longitude IS NULL)
                OR (latitude IS NOT NULL AND longitude IS NOT NULL
                    AND latitude BETWEEN -90 AND 90
                    AND longitude BETWEEN -180 AND 180)
                """
        };

    private static readonly IReadOnlyDictionary<string, string[]> RelevantStorageColumns =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["event_ticket_types"] =
            [
                "currency_code",
                "fixed_price_minor",
                "minimum_price_minor",
                "suggested_price_minor"
            ],
            ["location_pii"] = ["latitude", "longitude"],
            ["event_agenda_items"] = ["local_start_date", "local_end_date"],
            ["event_sessions"] = ["local_start_date", "local_end_date"]
        };

    [Test]
    [Arguments(PrimaryDatabaseProvider.PostgreSql)]
    [Arguments(PrimaryDatabaseProvider.Sqlite)]
    [Arguments(PrimaryDatabaseProvider.SqlServer)]
    [Arguments(PrimaryDatabaseProvider.MariaDb)]
    [Arguments(PrimaryDatabaseProvider.MySql)]
    public async Task GeneratedCatalog_HasOneInitialOwningEverySemanticConstraint(
        PrimaryDatabaseProvider provider)
    {
        await using ExploreDbContext context = CreateCatalogContext(provider);
        SemanticMigrationCatalog catalog = await FindSemanticMigrationAsync(context);
        Migration migration = catalog.Migration;

        string[] identities = SemanticChecks(migration)
            .Select(operation => ConstraintIdentity(operation.Table, operation.Name))
            .Order(StringComparer.Ordinal)
            .ToArray();
        await Assert.That(identities.SequenceEqual(ExpectedConstraintIdentities)).IsTrue();
    }

    [Test]
    [Arguments(PrimaryDatabaseProvider.PostgreSql)]
    [Arguments(PrimaryDatabaseProvider.Sqlite)]
    [Arguments(PrimaryDatabaseProvider.SqlServer)]
    [Arguments(PrimaryDatabaseProvider.MariaDb)]
    [Arguments(PrimaryDatabaseProvider.MySql)]
    public async Task GeneratedProviderCatalog_ContainsOnlySemanticChecksAndMatchesTheModel(
        PrimaryDatabaseProvider provider)
    {
        await using ExploreDbContext context = CreateCatalogContext(provider);
        SemanticMigrationCatalog catalog = await FindSemanticMigrationAsync(context);
        Migration migration = catalog.Migration;

        AddCheckConstraintOperation[] addChecks = SemanticChecks(migration);
        await Assert.That(addChecks.Length).IsEqualTo(ExpectedConstraintIdentities.Length);
        await Assert.That(addChecks.All(operation => !string.IsNullOrWhiteSpace(operation.Sql))).IsTrue();
        string? expectedSchema = provider is PrimaryDatabaseProvider.PostgreSql
            or PrimaryDatabaseProvider.SqlServer
                ? "islamu_event"
                : null;
        await Assert.That(addChecks.All(operation =>
                string.Equals(operation.Schema, expectedSchema, StringComparison.Ordinal)))
            .IsTrue();

        string[] upIdentities = addChecks
            .Select(operation => ConstraintIdentity(operation.Table, operation.Name))
            .Order(StringComparer.Ordinal)
            .ToArray();
        await Assert.That(upIdentities.SequenceEqual(ExpectedConstraintIdentities)).IsTrue();
        foreach (AddCheckConstraintOperation addCheck in addChecks)
        {
            string identity = ConstraintIdentity(addCheck.Table, addCheck.Name);
            bool found = ExpectedConstraintSqlByIdentity.TryGetValue(identity, out string? expectedSql);
            await Assert.That(found).IsTrue();
            await Assert.That(NormalizeSql(addCheck.Sql))
                .IsEqualTo(NormalizeSql(expectedSql!));
        }

        string[] semanticTables = ExpectedConstraintIdentities
            .Select(identity => identity.Split('.')[0])
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] droppedSemanticTables = migration.DownOperations
            .OfType<DropTableOperation>()
            .Select(operation => NormalizeTableName(operation.Name))
            .Where(table => semanticTables.Contains(table, StringComparer.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();
        await Assert.That(droppedSemanticTables.SequenceEqual(semanticTables)).IsTrue();

        string script = context.GetService<IMigrator>().GenerateScript(
            fromMigration: catalog.PreviousMigrationId,
            toMigration: catalog.MigrationId,
            options: MigrationsSqlGenerationOptions.Default);
        await AssertProviderScriptPreservesStorageAsync(script);

        IModel targetModel = InitializeModel(context, migration.TargetModel);
        await AssertRelevantStorageColumnsAsync(targetModel);

        IModel snapshotModel = ReadSnapshotModel(context);
        await AssertRelevantStorageColumnsAsync(snapshotModel);
        await Assert.That(ReadPendingModelOperations(context, snapshotModel)).IsEmpty();
    }

    [Test]
    public async Task PostgreSqlInitial_AppliesRollsBackAndReappliesIdempotently()
    {
        await using ExploreDbContext context = CreatePostgreSqlContext();
        SemanticMigrationCatalog catalog = await FindSemanticMigrationAsync(context);
        IMigrator migrator = context.GetService<IMigrator>();

        await fixture.ResetAsync();
        try
        {
            await migrator.MigrateAsync(catalog.MigrationId);
            await LookupTableSeeder.SeedAsync(context);
            Guid tenantId = await SeedValidScalarRowsAsync();
            string baselineFingerprint = await ReadScalarFingerprintAsync(tenantId);
            await Assert.That(await CountSemanticConstraintsAsync()).IsEqualTo(4);

            await migrator.MigrateAsync(catalog.MigrationId);
            await Assert.That(await ReadScalarFingerprintAsync(tenantId))
                .IsEqualTo(baselineFingerprint);
            await Assert.That(await CountSemanticConstraintsAsync()).IsEqualTo(4);
            await Assert.That((await context.Database.GetAppliedMigrationsAsync())
                    .Count(migration => migration == catalog.MigrationId))
                .IsEqualTo(1);

            await migrator.MigrateAsync(Migration.InitialDatabase);
            await Assert.That(await CountSemanticConstraintsAsync()).IsEqualTo(0);
            await migrator.MigrateAsync(catalog.MigrationId);
            await Assert.That(await CountSemanticConstraintsAsync()).IsEqualTo(4);
            await Assert.That((await context.Database.GetAppliedMigrationsAsync())
                    .Count(migration => migration == catalog.MigrationId))
                .IsEqualTo(1);
        }
        finally
        {
            await fixture.ResetAsync();
        }
    }

    [Test]
    [Arguments(LegacyViolation.NegativeTicketAmount, "ck_event_ticket_type_money_nonnegative")]
    [Arguments(LegacyViolation.PartialCoordinate, "ck_location_pii_coordinate_shape")]
    [Arguments(LegacyViolation.ReversedAgendaDates, "ck_event_agenda_item_local_date_range")]
    [Arguments(LegacyViolation.ReversedSessionDates, "ck_event_session_local_date_range")]
    public async Task PostgreSqlInitialConstraint_RejectsMalformedMutationWithoutDurableChange(
        LegacyViolation violation,
        string expectedConstraint)
    {
        await using ExploreDbContext context = CreatePostgreSqlContext();
        SemanticMigrationCatalog catalog = await FindSemanticMigrationAsync(context);
        IMigrator migrator = context.GetService<IMigrator>();

        await fixture.ResetAsync();
        try
        {
            await migrator.MigrateAsync(catalog.MigrationId);
            await LookupTableSeeder.SeedAsync(context);
            Guid tenantId = await SeedValidScalarRowsAsync();

            PostgresException? exception = await Assert.That(
                    async () => await ExecuteAsync(
                        LegacyMutationSql(violation),
                        ("tenant_id", tenantId)))
                .Throws<PostgresException>();
            await Assert.That(exception!.SqlState).IsEqualTo(PostgresErrorCodes.CheckViolation);
            await Assert.That(exception.ConstraintName).IsEqualTo(expectedConstraint);
            await Assert.That(await CountLegacyViolationsAsync(tenantId, violation)).IsEqualTo(0);
            await Assert.That(await CountSemanticConstraintsAsync()).IsEqualTo(4);
            await Assert.That((await context.Database.GetAppliedMigrationsAsync())
                    .Contains(catalog.MigrationId, StringComparer.Ordinal))
                .IsTrue();

            await ExecuteAsync(LegacyRepairSql(violation), ("tenant_id", tenantId));
            await Assert.That(await CountLegacyViolationsAsync(tenantId, violation)).IsEqualTo(0);
            await migrator.MigrateAsync(catalog.MigrationId);
            await Assert.That(await CountSemanticConstraintsAsync()).IsEqualTo(4);
            await Assert.That((await context.Database.GetAppliedMigrationsAsync())
                    .Contains(catalog.MigrationId, StringComparer.Ordinal))
                .IsTrue();
        }
        finally
        {
            await fixture.ResetAsync();
        }
    }

    [Test]
    public async Task PostgreSqlMalformedCoordinateMutation_EmitsZeroPiiDiagnostics()
    {
        const string sentinelAddress = "MIGRATION-PII-ADDRESS-Q7V4";
        const string sentinelPostcode = "MIGRATION-PII-POSTCODE-X9K2";
        const double sentinelLatitude = 50.123456789;
        var diagnostics = new CompleteDiagnostics();
        await using ExploreDbContext context = CreatePostgreSqlContext(diagnostics.Capture);
        SemanticMigrationCatalog catalog = await FindSemanticMigrationAsync(context);
        IMigrator migrator = context.GetService<IMigrator>();

        await fixture.ResetAsync();
        try
        {
            await migrator.MigrateAsync(catalog.MigrationId);
            await LookupTableSeeder.SeedAsync(context);
            Guid tenantId = await SeedValidScalarRowsAsync();
            Guid locationId = await ReadPairedLocationIdAsync(tenantId);

            PostgresException? exception = await Assert.That(
                    async () => await ExecuteAsync(
                        """
                        UPDATE islamu_event.location_pii
                        SET address = @address,
                            postcode = @postcode,
                            latitude = @latitude,
                            longitude = NULL
                        WHERE location_id = @location_id
                        """,
                        ("address", sentinelAddress),
                        ("postcode", sentinelPostcode),
                        ("latitude", sentinelLatitude),
                        ("location_id", locationId)))
                .Throws<PostgresException>();
            string[] forbidden =
            [
                sentinelAddress,
                sentinelPostcode,
                sentinelLatitude.ToString(System.Globalization.CultureInfo.InvariantCulture),
                tenantId.ToString(),
                locationId.ToString()
            ];
            foreach (string sentinel in forbidden)
            {
                await Assert.That(diagnostics.Text).DoesNotContain(sentinel);
                await Assert.That(exception!.ToString()).DoesNotContain(sentinel);
            }
        }
        finally
        {
            await fixture.ResetAsync();
        }
    }

    private static async Task<SemanticMigrationCatalog> FindSemanticMigrationAsync(
        ExploreDbContext context)
    {
        IMigrationsAssembly assembly = context.GetService<IMigrationsAssembly>();
        KeyValuePair<string, System.Reflection.TypeInfo>[] matches = assembly.Migrations
            .Where(entry => entry.Key.EndsWith(MigrationSuffix, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1)
        {
            string[] pendingOperations = ReadPendingModelOperations(
                context,
                ReadSnapshotModel(context));
            throw new InvalidOperationException(
                $"Expected one {MigrationSuffix} migration; found {matches.Length}. "
                + $"Pending operations: {string.Join(", ", pendingOperations)}.");
        }

        await Assert.That(matches).HasSingleItem();
        KeyValuePair<string, System.Reflection.TypeInfo> match = matches.Single();
        string[] migrationIds = assembly.Migrations.Keys.Order(StringComparer.Ordinal).ToArray();
        int migrationIndex = Array.IndexOf(migrationIds, match.Key);
        await Assert.That(migrationIndex).IsEqualTo(0);

        Migration migration = assembly.CreateMigration(
            match.Value,
            context.Database.ProviderName
                ?? throw new InvalidOperationException("The migration catalog has no provider name."));
        return new SemanticMigrationCatalog(
            match.Key,
            Migration.InitialDatabase,
            migration);
    }

    private static AddCheckConstraintOperation[] SemanticChecks(Migration migration) =>
        migration.UpOperations
            .OfType<CreateTableOperation>()
            .SelectMany(operation => operation.CheckConstraints)
            .Where(operation => ExpectedConstraintSqlByIdentity.ContainsKey(
                ConstraintIdentity(operation.Table, operation.Name)))
            .ToArray();

    private static async Task AssertProviderScriptPreservesStorageAsync(string script)
    {
        string normalizedScript = NormalizeSql(script);
        foreach (string constraint in ExpectedConstraintIdentities.Select(identity => identity.Split('.')[1]))
        {
            await Assert.That(normalizedScript.Contains(
                    constraint.ToLowerInvariant(),
                    StringComparison.Ordinal))
                .IsTrue();
        }

        foreach ((string table, string[] columns) in RelevantStorageColumns)
        {
            await Assert.That(normalizedScript.Contains(table, StringComparison.Ordinal)).IsTrue();
            foreach (string column in columns)
            {
                await Assert.That(normalizedScript.Contains(
                        $"drop column {column}",
                        StringComparison.Ordinal))
                    .IsFalse();
                await Assert.That(normalizedScript.Contains(
                        $"alter column {column}",
                        StringComparison.Ordinal))
                    .IsFalse();
            }
        }
    }

    private static async Task AssertRelevantStorageColumnsAsync(IModel model)
    {
        foreach ((string logicalTable, string[] expectedColumns) in RelevantStorageColumns)
        {
            var tables = model.GetRelationalModel().Tables
                .Where(table => NormalizeTableName(table.Name) == logicalTable)
                .ToArray();
            await Assert.That(tables).HasSingleItem();

            HashSet<string> actualColumns = tables.Single().Columns
                .Select(column => column.Name)
                .ToHashSet(StringComparer.Ordinal);
            await Assert.That(expectedColumns.All(actualColumns.Contains)).IsTrue();
        }
    }

    private static IModel ReadSnapshotModel(ExploreDbContext context)
    {
        IMigrationsAssembly migrationsAssembly = context.GetService<IMigrationsAssembly>();
        IModel rawSnapshotModel = migrationsAssembly.ModelSnapshot?.Model
            ?? throw new InvalidOperationException("ExploreDbContext migration snapshot was not found.");
        return InitializeModel(context, rawSnapshotModel);
    }

    private static IModel InitializeModel(ExploreDbContext context, IModel model) =>
        context.GetService<IModelRuntimeInitializer>()
            .Initialize(model, designTime: true, validationLogger: null);

    private static string[] ReadPendingModelOperations(
        ExploreDbContext context,
        IModel snapshotModel)
    {
        IMigrationsModelDiffer modelDiffer = context.GetService<IMigrationsModelDiffer>();
        IModel runtimeModel = context.GetService<IDesignTimeModel>().Model;
        return modelDiffer
            .GetDifferences(snapshotModel.GetRelationalModel(), runtimeModel.GetRelationalModel())
            .Select(DescribeOperation)
            .ToArray();
    }

    private static string DescribeOperation(MigrationOperation operation) => operation switch
    {
        AddColumnOperation value => $"AddColumn:{value.Table}.{value.Name}",
        AlterColumnOperation value => $"AlterColumn:{value.Table}.{value.Name}",
        DropColumnOperation value => $"DropColumn:{value.Table}.{value.Name}",
        CreateTableOperation value => $"CreateTable:{value.Name}",
        DropTableOperation value => $"DropTable:{value.Name}",
        CreateIndexOperation value => $"CreateIndex:{value.Table}.{value.Name}",
        DropIndexOperation value => $"DropIndex:{value.Table}.{value.Name}",
        AddCheckConstraintOperation value => $"AddCheck:{value.Table}.{value.Name}",
        DropCheckConstraintOperation value => $"DropCheck:{value.Table}.{value.Name}",
        SqlOperation => "Sql",
        InsertDataOperation value => $"InsertData:{value.Table}",
        UpdateDataOperation value => $"UpdateData:{value.Table}",
        DeleteDataOperation value => $"DeleteData:{value.Table}",
        _ => operation.GetType().Name
    };

    private async Task<Guid> SeedValidScalarRowsAsync()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid servicePrincipalId = Guid.CreateVersion7();
        Guid actorId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        Guid catalogId = Guid.CreateVersion7();
        Guid fixedTicketId = Guid.CreateVersion7();
        Guid slidingTicketId = Guid.CreateVersion7();
        Guid pairedLocationId = Guid.CreateVersion7();
        Guid nullLocationId = Guid.CreateVersion7();
        Guid agendaItemId = Guid.CreateVersion7();
        Guid scheduledSessionId = Guid.CreateVersion7();
        Guid unscheduledSessionId = Guid.CreateVersion7();
        Guid concurrencyStamp = Guid.CreateVersion7();

        await ExecuteAsync(
            """
            INSERT INTO islamu_event.tenants
                (id, full_name, slug, tenant_status_id, created_at)
            VALUES
                (@tenant_id, 'Semantic migration tenant', 'semantic-migration-tenant', 2,
                 TIMESTAMPTZ '2026-08-25 00:00:00+00');

            INSERT INTO islamu_event.service_principals
                (id, code, display_name, created_at, is_deleted, concurrency_stamp)
            VALUES
                (@service_principal_id, 'semantic-migration-principal', 'Semantic migration principal',
                 TIMESTAMPTZ '2026-08-25 00:00:00+00', FALSE, @concurrency_stamp);

            INSERT INTO islamu_event.actors
                (id, actor_type_id, service_principal_id, is_suspended, created_at, is_deleted,
                 concurrency_stamp)
            VALUES
                (@actor_id, 5, @service_principal_id, FALSE,
                 TIMESTAMPTZ '2026-08-25 00:00:00+00', FALSE, @concurrency_stamp);

            INSERT INTO islamu_event.events
                (id, title, actor_id, event_provenance_type_id, tenant_id, public_code,
                 visibility_type_id, event_status_id, event_format_id, created_at, is_deleted,
                 concurrency_stamp)
            VALUES
                (@event_id, 'Semantic migration event', @actor_id, 1, @tenant_id, 'SEMANTIC10',
                 1, 1, 1, TIMESTAMPTZ '2026-08-25 00:00:00+00', FALSE, @concurrency_stamp);

            INSERT INTO islamu_event.event_ticket_catalog_versions
                (id, tenant_id, event_id, currency_code, version_number, ticket_catalog_status_id,
                 concurrency_stamp, created_at, is_deleted)
            VALUES
                (@catalog_id, @tenant_id, @event_id, 'USD', 1, 1, @concurrency_stamp,
                 TIMESTAMPTZ '2026-08-25 00:00:00+00', FALSE);

            INSERT INTO islamu_event.event_ticket_types
                (id, tenant_id, catalog_id, name, currency_code, ticket_pricing_mode_id,
                 fixed_price_minor, minimum_price_minor, suggested_price_minor,
                 participant_data_collection_mode_id, requires_guardian, requires_approval,
                 concurrency_stamp, created_at, is_deleted)
            VALUES
                (@fixed_ticket_id, @tenant_id, @catalog_id, 'Fixed', 'USD', 1,
                 2500, NULL, NULL, 1, FALSE, FALSE, @concurrency_stamp,
                 TIMESTAMPTZ '2026-08-25 00:00:00+00', FALSE),
                (@sliding_ticket_id, @tenant_id, @catalog_id, 'Sliding', 'USD', 5,
                 NULL, 1000, 2000, 1, FALSE, FALSE, @concurrency_stamp,
                 TIMESTAMPTZ '2026-08-25 00:00:00+00', FALSE);

            INSERT INTO islamu_event.locations
                (id, full_name, country, city, tenant_id, location_kind_id,
                 location_privacy_state_id, created_at, concurrency_stamp, display_sort_key, display_sort_key_version)
            VALUES
                (@paired_location_id, 'Paired coordinate fixture', 'Synthetic', 'Synthetic',
                 @tenant_id, 2, 2, TIMESTAMPTZ '2026-08-25 00:00:00+00', @concurrency_stamp, 'PAIRED COORDINATE FIXTURE', 2),
                (@null_location_id, 'Null coordinate fixture', 'Synthetic', 'Synthetic',
                 @tenant_id, 2, 2, TIMESTAMPTZ '2026-08-25 00:00:00+00', @concurrency_stamp, 'NULL COORDINATE FIXTURE', 2);

            INSERT INTO islamu_event.location_pii
                (location_id, address, postcode, latitude, longitude, address_substring_key, address_substring_key_version)
            VALUES
                (@paired_location_id, @paired_address, @paired_postcode, 51.0504, 13.7373, 'SYNTHETIC-PAIRED-ADDRESS', 2),
                (@null_location_id, @null_address, @null_postcode, NULL, NULL, 'SYNTHETIC-NULL-ADDRESS', 2);

            INSERT INTO islamu_event.event_agenda_items
                (id, event_id, title, start_time, end_time, local_start_date, local_end_date,
                 local_start_time, local_end_time, local_start_minute_of_day,
                 local_end_minute_of_day, sort_order, tenant_id, created_at, is_deleted,
                 concurrency_stamp)
            VALUES
                (@agenda_item_id, @event_id, 'Cross-day agenda fixture',
                 TIMESTAMPTZ '2026-08-25 22:00:00+00', TIMESTAMPTZ '2026-08-26 01:00:00+00',
                 DATE '2026-08-25', DATE '2026-08-26', TIME '22:00:00', TIME '01:00:00',
                 1320, 60, 1, @tenant_id, TIMESTAMPTZ '2026-08-25 00:00:00+00', FALSE,
                 @concurrency_stamp);

            INSERT INTO islamu_event.event_sessions
                (id, event_id, start_time, end_time, end_time_type, local_start_date,
                 local_end_date, local_start_time, local_end_time, local_start_minute_of_day,
                 local_end_minute_of_day, sort_order, title, event_session_status_id, tenant_id,
                 created_at, is_deleted, concurrency_stamp)
            VALUES
                (@scheduled_session_id, @event_id, TIMESTAMPTZ '2026-08-25 10:00:00+00',
                 TIMESTAMPTZ '2026-08-25 11:00:00+00', 0, DATE '2026-08-25',
                 DATE '2026-08-25', TIME '10:00:00', TIME '11:00:00', 600, 660, 1,
                 'Scheduled fixture', 1, @tenant_id, TIMESTAMPTZ '2026-08-25 00:00:00+00',
                 FALSE, @concurrency_stamp),
                (@unscheduled_session_id, @event_id, NULL, NULL, 0, NULL, NULL, NULL, NULL,
                 NULL, NULL, 2, 'Unscheduled fixture', 1, @tenant_id,
                 TIMESTAMPTZ '2026-08-25 00:00:00+00', FALSE, @concurrency_stamp);
            """,
            ("tenant_id", tenantId),
            ("service_principal_id", servicePrincipalId),
            ("actor_id", actorId),
            ("event_id", eventId),
            ("catalog_id", catalogId),
            ("fixed_ticket_id", fixedTicketId),
            ("sliding_ticket_id", slidingTicketId),
            ("paired_location_id", pairedLocationId),
            ("null_location_id", nullLocationId),
            ("agenda_item_id", agendaItemId),
            ("scheduled_session_id", scheduledSessionId),
            ("unscheduled_session_id", unscheduledSessionId),
            ("concurrency_stamp", concurrencyStamp),
            ("paired_address", "synthetic-paired-address"),
            ("paired_postcode", "synthetic-paired-postcode"),
            ("null_address", "synthetic-null-address"),
            ("null_postcode", "synthetic-null-postcode"));

        return tenantId;
    }

    private async Task<string> ReadScalarCardinalityAsync(Guid tenantId)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT concat(
                (SELECT count(*) FROM islamu_event.event_ticket_types
                 WHERE tenant_id = @tenant_id), ':',
                (SELECT count(*) FROM islamu_event.location_pii AS pii
                 INNER JOIN islamu_event.locations AS location ON location.id = pii.location_id
                 WHERE location.tenant_id = @tenant_id), ':',
                (SELECT count(*) FROM islamu_event.event_agenda_items
                 WHERE tenant_id = @tenant_id), ':',
                (SELECT count(*) FROM islamu_event.event_sessions
                 WHERE tenant_id = @tenant_id))
            """,
            connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        return (string)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("The scalar-row cardinality query returned no value."));
    }

    private async Task<Guid> ReadPairedLocationIdAsync(Guid tenantId)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT pii.location_id
            FROM islamu_event.location_pii AS pii
            INNER JOIN islamu_event.locations AS location ON location.id = pii.location_id
            WHERE location.tenant_id = @tenant_id
              AND pii.latitude IS NOT NULL
              AND pii.longitude IS NOT NULL
            """,
            connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        return (Guid)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("The paired-location query returned no value."));
    }

    private async Task<string> ReadScalarFingerprintAsync(Guid tenantId)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT jsonb_build_object(
                'tickets', (
                    SELECT COALESCE(jsonb_agg(jsonb_build_object(
                        'id', ticket.id,
                        'currency_code', ticket.currency_code,
                        'fixed_price_minor', ticket.fixed_price_minor,
                        'minimum_price_minor', ticket.minimum_price_minor,
                        'suggested_price_minor', ticket.suggested_price_minor)
                        ORDER BY ticket.id), '[]'::jsonb)
                    FROM islamu_event.event_ticket_types AS ticket
                    WHERE ticket.tenant_id = @tenant_id),
                'locations', (
                    SELECT COALESCE(jsonb_agg(jsonb_build_object(
                        'location_id', pii.location_id,
                        'address_sha256', encode(sha256(convert_to(pii.address, 'UTF8')), 'hex'),
                        'postcode_sha256', encode(sha256(convert_to(pii.postcode, 'UTF8')), 'hex'),
                        'coordinate_shape', CASE
                            WHEN pii.latitude IS NULL AND pii.longitude IS NULL THEN 'null'
                            WHEN pii.latitude IS NOT NULL AND pii.longitude IS NOT NULL THEN 'pair'
                            ELSE 'partial'
                        END,
                        'coordinate_sha256', CASE
                            WHEN pii.latitude IS NOT NULL AND pii.longitude IS NOT NULL
                            THEN encode(sha256(convert_to(
                                pii.latitude::text || ':' || pii.longitude::text, 'UTF8')), 'hex')
                            ELSE NULL
                        END) ORDER BY pii.location_id), '[]'::jsonb)
                    FROM islamu_event.location_pii AS pii
                    INNER JOIN islamu_event.locations AS location ON location.id = pii.location_id
                    WHERE location.tenant_id = @tenant_id),
                'agenda', (
                    SELECT COALESCE(jsonb_agg(jsonb_build_object(
                        'id', item.id,
                        'local_start_date', item.local_start_date,
                        'local_end_date', item.local_end_date) ORDER BY item.id), '[]'::jsonb)
                    FROM islamu_event.event_agenda_items AS item
                    WHERE item.tenant_id = @tenant_id),
                'sessions', (
                    SELECT COALESCE(jsonb_agg(jsonb_build_object(
                        'id', session.id,
                        'local_start_date', session.local_start_date,
                        'local_end_date', session.local_end_date) ORDER BY session.id), '[]'::jsonb)
                    FROM islamu_event.event_sessions AS session
                    WHERE session.tenant_id = @tenant_id))::text
            """,
            connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        return (string)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("The scalar-row fingerprint query returned no value."));
    }

    private async Task<int> CountSemanticConstraintsAsync()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT count(*)::integer
            FROM pg_catalog.pg_constraint AS constraint_entry
            JOIN pg_catalog.pg_class AS table_entry
              ON table_entry.oid = constraint_entry.conrelid
            JOIN pg_catalog.pg_namespace AS schema_entry
              ON schema_entry.oid = table_entry.relnamespace
            WHERE constraint_entry.contype = 'c'
              AND schema_entry.nspname = 'islamu_event'
              AND constraint_entry.conname = ANY (@constraint_names)
            """,
            connection);
        command.Parameters.AddWithValue(
            "constraint_names",
            ExpectedConstraintIdentities.Select(identity => identity.Split('.')[1]).ToArray());
        return (int)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("The constraint-count query returned no value."));
    }

    private async Task<int> CountLegacyViolationsAsync(
        Guid tenantId,
        LegacyViolation violation)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(LegacyViolationCountSql(violation), connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        return (int)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("The legacy-violation count query returned no value."));
    }

    private static string LegacyMutationSql(LegacyViolation violation) => violation switch
    {
        LegacyViolation.NegativeTicketAmount =>
            """
            UPDATE islamu_event.event_ticket_types
            SET fixed_price_minor = -1
            WHERE tenant_id = @tenant_id AND fixed_price_minor IS NOT NULL
            """,
        LegacyViolation.PartialCoordinate =>
            """
            UPDATE islamu_event.location_pii AS pii
            SET longitude = NULL
            FROM islamu_event.locations AS location
            WHERE location.id = pii.location_id
              AND location.tenant_id = @tenant_id
              AND pii.latitude IS NOT NULL
            """,
        LegacyViolation.ReversedAgendaDates =>
            """
            UPDATE islamu_event.event_agenda_items
            SET local_end_date = local_start_date - 1
            WHERE tenant_id = @tenant_id
            """,
        LegacyViolation.ReversedSessionDates =>
            """
            UPDATE islamu_event.event_sessions
            SET local_end_date = local_start_date - 1
            WHERE tenant_id = @tenant_id AND local_start_date IS NOT NULL
            """,
        _ => throw new ArgumentOutOfRangeException(nameof(violation), violation, null)
    };

    private static string LegacyRepairSql(LegacyViolation violation) => violation switch
    {
        LegacyViolation.NegativeTicketAmount =>
            """
            UPDATE islamu_event.event_ticket_types
            SET fixed_price_minor = 2500
            WHERE tenant_id = @tenant_id AND fixed_price_minor < 0
            """,
        LegacyViolation.PartialCoordinate =>
            """
            UPDATE islamu_event.location_pii AS pii
            SET longitude = 13.7373
            FROM islamu_event.locations AS location
            WHERE location.id = pii.location_id
              AND location.tenant_id = @tenant_id
              AND pii.latitude IS NOT NULL
              AND pii.longitude IS NULL
            """,
        LegacyViolation.ReversedAgendaDates =>
            """
            UPDATE islamu_event.event_agenda_items
            SET local_end_date = local_start_date
            WHERE tenant_id = @tenant_id AND local_end_date < local_start_date
            """,
        LegacyViolation.ReversedSessionDates =>
            """
            UPDATE islamu_event.event_sessions
            SET local_end_date = local_start_date
            WHERE tenant_id = @tenant_id AND local_end_date < local_start_date
            """,
        _ => throw new ArgumentOutOfRangeException(nameof(violation), violation, null)
    };

    private static string LegacyViolationCountSql(LegacyViolation violation) => violation switch
    {
        LegacyViolation.NegativeTicketAmount =>
            """
            SELECT count(*)::integer
            FROM islamu_event.event_ticket_types
            WHERE tenant_id = @tenant_id AND fixed_price_minor < 0
            """,
        LegacyViolation.PartialCoordinate =>
            """
            SELECT count(*)::integer
            FROM islamu_event.location_pii AS pii
            INNER JOIN islamu_event.locations AS location ON location.id = pii.location_id
            WHERE location.tenant_id = @tenant_id
              AND ((pii.latitude IS NULL) <> (pii.longitude IS NULL))
            """,
        LegacyViolation.ReversedAgendaDates =>
            """
            SELECT count(*)::integer
            FROM islamu_event.event_agenda_items
            WHERE tenant_id = @tenant_id AND local_end_date < local_start_date
            """,
        LegacyViolation.ReversedSessionDates =>
            """
            SELECT count(*)::integer
            FROM islamu_event.event_sessions
            WHERE tenant_id = @tenant_id AND local_end_date < local_start_date
            """,
        _ => throw new ArgumentOutOfRangeException(nameof(violation), violation, null)
    };

    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }

    private ExploreDbContext CreatePostgreSqlContext(Action<string>? captureDiagnostics = null)
    {
        var connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString);
        var builder = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(
            builder,
            new PrimaryDatabaseConnectionOptions
            {
                Role = PrimaryDatabaseRole.Migrator,
                Provider = PrimaryDatabaseProvider.PostgreSql,
                Host = connection.Host,
                Port = connection.Port,
                Database = connection.Database,
                Username = connection.Username,
                Password = connection.Password,
                TlsMode = PrimaryDatabaseTlsMode.Disabled
            });
        if (captureDiagnostics is not null)
        {
            builder.EnableSensitiveDataLogging(false)
                .LogTo(captureDiagnostics, LogLevel.Information);
        }

        return new ExploreDbContext(builder.Options);
    }

    private static ExploreDbContext CreateCatalogContext(PrimaryDatabaseProvider provider)
    {
        var builder = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder, CreateCatalogOptions(provider));
        return new ExploreDbContext(builder.Options);
    }

    private static PrimaryDatabaseConnectionOptions CreateCatalogOptions(
        PrimaryDatabaseProvider provider)
    {
        if (provider == PrimaryDatabaseProvider.Sqlite)
        {
            return new PrimaryDatabaseConnectionOptions
            {
                Role = PrimaryDatabaseRole.Migrator,
                Provider = provider,
                Database = Path.Combine(Path.GetTempPath(), "semantic-value-constraint-catalog.db")
            };
        }

        PrimaryDatabaseServerFlavor? flavor =
            Enum.TryParse(provider.ToString(), out PrimaryDatabaseServerFlavor parsedFlavor)
                ? parsedFlavor
                : null;
        return new PrimaryDatabaseConnectionOptions
        {
            Role = PrimaryDatabaseRole.Migrator,
            Provider = provider,
            Host = "database.example.test",
            Database = "event_catalog",
            Username = "migration_catalog_reader",
            Password = Guid.CreateVersion7().ToString("N"),
            TlsMode = PrimaryDatabaseTlsMode.Required,
            ServerFlavor = flavor,
            ServerVersion = flavor switch
            {
                PrimaryDatabaseServerFlavor.MariaDb => new Version(11, 4),
                PrimaryDatabaseServerFlavor.MySql => new Version(8, 4),
                _ => null
            }
        };
    }

    private static string ConstraintIdentity(string table, string constraint) =>
        $"{NormalizeTableName(table)}.{constraint}";

    private static string NormalizeTableName(string table) =>
        table.StartsWith(ProviderTablePrefix, StringComparison.Ordinal)
            ? table[ProviderTablePrefix.Length..]
            : table;

    private static string NormalizeSql(string sql)
    {
        string unquoted = sql
            .Replace("\"", string.Empty, StringComparison.Ordinal)
            .Replace("`", string.Empty, StringComparison.Ordinal)
            .Replace("[", string.Empty, StringComparison.Ordinal)
            .Replace("]", string.Empty, StringComparison.Ordinal);
        return string.Join(
                ' ',
                unquoted.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant();
    }

    private sealed record SemanticMigrationCatalog(
        string MigrationId,
        string PreviousMigrationId,
        Migration Migration);

    private sealed class CompleteDiagnostics
    {
        private readonly object _gate = new();
        private readonly List<string> _entries = [];

        public string Text
        {
            get
            {
                lock (_gate)
                {
                    return string.Join('\n', _entries);
                }
            }
        }

        public void Capture(string message)
        {
            lock (_gate)
            {
                _entries.Add(message);
            }
        }
    }

    public enum LegacyViolation
    {
        NegativeTicketAmount,
        PartialCoordinate,
        ReversedAgendaDates,
        ReversedSessionDates
    }
}
