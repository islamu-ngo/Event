// ABOUTME: Verifies generated application/data-protection migrations and the retained authority chain.
// ABOUTME: Pins runtime lookup/schema application, receipt-linkage primitives, rollback safety, and authority ACLs.

using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Persistence;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Schema;
using Explore.Persistence.Seed;
using Explore.Secrets.Database;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Migrations;

[ClassDataSource<RecipientDeliveryMigrationContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("RecipientDeliveryMigrationDb")]
public sealed class GeneratedInitMigrationBehaviorTests(
    RecipientDeliveryMigrationContainerFixture fixture)
{
    [Test]
    public async Task GeneratedCatalogs_ContainExactlyOneInitPerPostgreSqlContext()
    {
        await using ExploreDbContext explore = CreateExploreContext();
        await using DataProtectionKeyContext dataProtection = CreateDataProtectionContext();
        await using PrivacyErasureAuthorityDbContext authority = CreateAuthorityContext();

        string[] exploreMigrations = MigrationIds(explore);
        string[] dataProtectionMigrations = MigrationIds(dataProtection);
        string[] authorityMigrations = MigrationIds(authority);
        await Assert.That(exploreMigrations.Count(migration => migration.EndsWith("_Init", StringComparison.Ordinal))).IsEqualTo(1);
        await Assert.That(dataProtectionMigrations).HasSingleItem();
        await Assert.That(authorityMigrations).HasSingleItem();
        await Assert.That(dataProtectionMigrations[0]).EndsWith("_Init");
        await Assert.That(authorityMigrations[0]).EndsWith("_Init");

        Migration dataProtectionInit = InitMigration(dataProtection);
        CreateTableOperation table = dataProtectionInit.UpOperations
            .OfType<CreateTableOperation>()
            .Single();
        await Assert.That(table.Name).IsEqualTo("data_protection_keys");
        await Assert.That(table.Columns.Select(column => column.Name))
            .IsEquivalentTo(["id", "friendly_name", "xml"]);
        await Assert.That(dataProtectionInit.UpOperations.Count).IsEqualTo(2);
        await Assert.That(dataProtectionInit.UpOperations.OfType<EnsureSchemaOperation>().Single().Name)
            .IsEqualTo("islamu_event");
    }

    [Test]
    public async Task DataProtectionInitial_AppliesRollsBackAndReapplies()
    {
        await ResetDatabaseAsync();
        var connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString);
        await SqliteApplicationInitialLifecycleTests.AssertDataProtectionLifecycleAsync(
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
    }

    [Test]
    public async Task ExploreInit_WithRuntimeCatalogAndSchemaApplication_IsReversibleFromEmpty()
    {
        await ResetDatabaseAsync();
        try
        {
            await using ExploreDbContext context = CreateExploreContext();
            IMigrator migrator = context.GetService<IMigrator>();
            string migrationId = MigrationIds(context)[^1];

            await migrator.MigrateAsync(migrationId);
            await PostgresModelConstraintApplier.ApplyAsync(context);
            await LookupTableSeeder.SeedAsync(context);

            await Assert.That(await ScalarIntAsync(
                """
                SELECT
                    (SELECT count(*) FROM islamu_event.location_kinds
                     WHERE (id, master_code) IN (
                         (1, 'UNCLASSIFIED'), (2, 'COMMERCIAL_VENUE'),
                         (3, 'PUBLIC_SPACE'), (4, 'COMMUNITY_VENUE'), (5, 'PRIVATE_HOME')))
                  + (SELECT count(*) FROM islamu_event.location_privacy_states
                     WHERE (id, master_code) IN (
                         (1, 'NOT_PROVIDED'), (2, 'ACTIVE'), (3, 'ERASED')))
                  + (SELECT count(*) FROM islamu_event.location_disclosure_audiences
                     WHERE (id, master_code) IN (
                         (1, 'NEVER'), (2, 'ANY_CURRENT_REGISTRANT'),
                         (3, 'CONFIRMED_PARTICIPANT')))
                """)).IsEqualTo(11);
            await Assert.That(await ScalarIntAsync(
                """
                SELECT count(*)::integer
                FROM pg_catalog.pg_proc AS function_entry
                JOIN pg_catalog.pg_namespace AS schema_entry
                  ON schema_entry.oid = function_entry.pronamespace
                WHERE schema_entry.nspname = 'public'
                  AND function_entry.proname LIKE 'elp_%'
                """)).IsEqualTo(0);
            await Assert.That(await ScalarIntAsync(
                """
                SELECT count(*)::integer
                FROM pg_catalog.pg_trigger
                WHERE NOT tgisinternal AND tgname LIKE 'tr_elp_%'
                """)).IsEqualTo(0);
            await Assert.That(await ScalarIntAsync(
                """
                SELECT
                    (SELECT count(*)
                     FROM pg_catalog.pg_constraint AS constraint_entry
                     JOIN pg_catalog.pg_class AS table_entry
                       ON table_entry.oid = constraint_entry.conrelid
                     WHERE table_entry.relname = 'event_report_decision_executions'
                       AND constraint_entry.contype = 'f'
                       AND ARRAY(
                             SELECT attribute_entry.attname
                             FROM unnest(constraint_entry.conkey) WITH ORDINALITY AS key_entry(attnum, ordinal)
                             JOIN pg_catalog.pg_attribute AS attribute_entry
                               ON attribute_entry.attrelid = table_entry.oid
                              AND attribute_entry.attnum = key_entry.attnum
                             ORDER BY key_entry.ordinal)
                           = ARRAY['tenant_id', 'report_id', 'decision_id']::name[])
                  + (SELECT count(*)
                     FROM pg_catalog.pg_constraint AS constraint_entry
                     JOIN pg_catalog.pg_class AS table_entry
                       ON table_entry.oid = constraint_entry.conrelid
                     WHERE table_entry.relname = 'event_report_decision_executions'
                       AND constraint_entry.contype = 'f'
                       AND ARRAY(
                             SELECT attribute_entry.attname
                             FROM unnest(constraint_entry.conkey) WITH ORDINALITY AS key_entry(attnum, ordinal)
                             JOIN pg_catalog.pg_attribute AS attribute_entry
                               ON attribute_entry.attrelid = table_entry.oid
                              AND attribute_entry.attnum = key_entry.attnum
                             ORDER BY key_entry.ordinal)
                           = ARRAY['tenant_id', 'moderation_record_id']::name[])
                  + (SELECT count(*)
                     FROM pg_catalog.pg_index AS index_entry
                     JOIN pg_catalog.pg_class AS table_entry
                       ON table_entry.oid = index_entry.indrelid
                     WHERE table_entry.relname = 'event_moderation_records'
                       AND index_entry.indisunique
                       AND ARRAY(
                             SELECT attribute_entry.attname
                             FROM unnest(index_entry.indkey) WITH ORDINALITY AS key_entry(attnum, ordinal)
                             JOIN pg_catalog.pg_attribute AS attribute_entry
                               ON attribute_entry.attrelid = table_entry.oid
                              AND attribute_entry.attnum = key_entry.attnum
                             ORDER BY key_entry.ordinal)
                           = ARRAY['tenant_id', 'source_report_id', 'source_report_decision_id', 'id']::name[])
                """)).IsEqualTo(3);
            await Assert.That(await ScalarIntAsync(
                """
                SELECT count(*)::integer
                FROM pg_catalog.pg_constraint
                WHERE conname = 'ex_event_session_room_no_overlap'
                  AND conrelid = 'islamu_event.event_sessions'::regclass
                """)).IsEqualTo(1);

            await migrator.MigrateAsync(Migration.InitialDatabase);
            await migrator.MigrateAsync(migrationId);
            await PostgresModelConstraintApplier.ApplyAsync(context);
            context.ChangeTracker.Clear();
            await LookupTableSeeder.SeedAsync(context);
            await Assert.That(await ScalarIntAsync(
                "SELECT count(*)::integer FROM \"__EFMigrationsHistory\" WHERE migration_id = @id",
                new NpgsqlParameter("id", migrationId)))
                .IsEqualTo(1);
        }
        finally
        {
            await ResetDatabaseAsync();
        }
    }

    [Test]
    public async Task ExploreInit_LeavesLookupOwnershipToTheRuntimeSeeder()
    {
        await ResetDatabaseAsync();
        try
        {
            await using ExploreDbContext context = CreateExploreContext();
            IMigrator migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(MigrationIds(context)[^1]);

            await Assert.That(await ScalarIntAsync(
                "SELECT count(*)::integer FROM islamu_event.location_kinds")).IsEqualTo(0);
            await LookupTableSeeder.SeedAsync(context);
            await Assert.That(await ScalarIntAsync(
                "SELECT count(*)::integer FROM islamu_event.location_kinds")).IsGreaterThanOrEqualTo(5);
        }
        finally
        {
            await ResetDatabaseAsync();
        }
    }

    [Test]
    public async Task AuthorityInit_IsFunctionOnlyAndProtectsRetainedEvidenceRollback()
    {
        await ResetDatabaseAsync();
        try
        {
            await using PrivacyErasureAuthorityDbContext context = CreateAuthorityContext();
            IMigrator migrator = context.GetService<IMigrator>();
            string migrationId = MigrationIds(context).Last();

            await migrator.MigrateAsync(migrationId);
            await migrator.MigrateAsync(Migration.InitialDatabase);
            await migrator.MigrateAsync(migrationId);
            await context.Database.ExecuteSqlRawAsync(
                PrivacyErasureAuthorityDatabaseContract.RoleProvisioningSql);
            await ExploreDatabaseMigrator.ApplyExternalPrivacyErasureAuthorityContractAsync(context);
            await ExploreDatabaseMigrator.ApplyExternalPrivacyErasureAuthorityContractAsync(context);

            await Assert.That(await ScalarIntAsync(
                """
                SELECT count(*)::integer FROM pg_catalog.pg_roles
                WHERE rolname IN (
                    'privacy_erasure_authority_owner',
                    'privacy_erasure_authority_migrator',
                    'privacy_erasure_authority_runtime')
                  AND NOT rolcanlogin AND NOT rolsuper AND NOT rolcreatedb
                  AND NOT rolcreaterole AND NOT rolinherit AND NOT rolreplication
                  AND NOT rolbypassrls
                """)).IsEqualTo(3);
            await Assert.That(await ScalarIntAsync(
                """
                SELECT count(*)::integer
                FROM pg_catalog.pg_proc AS function_entry
                JOIN pg_catalog.pg_namespace AS schema_entry
                  ON schema_entry.oid = function_entry.pronamespace
                WHERE schema_entry.nspname = 'privacy_erasure_authority'
                  AND function_entry.proname IN (
                      'reject_erasure_intent_mutation',
                      'append_erasure_intent',
                      'read_erasure_intents_after')
                """)).IsEqualTo(3);
            await Assert.That(await ScalarIntAsync(
                """
                SELECT count(*)::integer
                FROM information_schema.role_routine_grants
                WHERE grantee = 'privacy_erasure_authority_runtime'
                  AND routine_schema = 'privacy_erasure_authority'
                  AND privilege_type = 'EXECUTE'
                  AND routine_name IN (
                      'append_erasure_intent_with_retention',
                      'read_erasure_intents_after')
                """)).IsEqualTo(2);
            await Assert.That(await ScalarIntAsync(
                """
                SELECT count(*)::integer
                FROM information_schema.role_table_grants
                WHERE grantee = 'privacy_erasure_authority_runtime'
                  AND table_schema = 'privacy_erasure_authority'
                """)).IsEqualTo(0);

            await ScalarLongAsync(
                """
                SELECT authority_sequence
                FROM privacy_erasure_authority.append_erasure_intent(
                    uuidv7(), 1::smallint, uuidv7(), 1::smallint, 1)
                """);

            Exception rollback = (await Assert.ThrowsAsync<Exception>(
                () => migrator.MigrateAsync(Migration.InitialDatabase)))!;
            PostgresException postgres = FindPostgresException(rollback)
                ?? throw new InvalidOperationException("Expected a PostgreSQL rollback guard.", rollback);
            await Assert.That(postgres.SqlState).IsEqualTo(PostgresErrorCodes.DependentObjectsStillExist);
            await Assert.That(await ScalarIntAsync(
                "SELECT count(*)::integer FROM privacy_erasure_authority.erasure_intents"))
                .IsEqualTo(1);
        }
        finally
        {
            await ResetDatabaseAsync();
        }
    }

    [Test]
    public async Task CancelledDedicatedMigration_LeavesNoAuthoritySchemaOrHistory()
    {
        await ResetDatabaseAsync();
        try
        {
            await using PrivacyErasureAuthorityDbContext context = CreateAuthorityContext();
            IMigrator migrator = context.GetService<IMigrator>();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                migrator.MigrateAsync(MigrationIds(context).Last(), cancellation.Token));

            await Assert.That(await ScalarIntAsync(
                "SELECT count(*)::integer FROM pg_catalog.pg_namespace WHERE nspname = 'privacy_erasure_authority'"))
                .IsEqualTo(0);
            await Assert.That(await ScalarIntAsync(
                "SELECT count(*)::integer FROM information_schema.tables WHERE table_schema = 'public' AND table_name = '__EFMigrationsHistory'"))
                .IsEqualTo(0);
        }
        finally
        {
            await ResetDatabaseAsync();
        }
    }

    [Test]
    public async Task SuccessfulApplicationMigration_DoesNotAuthorizeDedicatedMigrationOnSameTarget()
    {
        await ResetDatabaseAsync();
        try
        {
            await using ExploreDbContext context = CreateExploreContext();
            await context.GetService<IMigrator>().MigrateAsync(MigrationIds(context)[^1]);
            await Assert.That(await ScalarIntAsync(
                "SELECT count(*)::integer FROM information_schema.tables WHERE table_schema = 'privacy_erasure_authority'"))
                .IsEqualTo(0);
            await Assert.That(await ScalarIntAsync(
                """
                SELECT count(*)::integer
                FROM pg_catalog.pg_proc AS function_entry
                JOIN pg_catalog.pg_namespace AS schema_entry
                  ON schema_entry.oid = function_entry.pronamespace
                WHERE schema_entry.nspname = 'privacy_erasure_authority'
                  AND function_entry.proname IN ('append_erasure_intent', 'read_erasure_intents_after')
                """))
                .IsEqualTo(0);

            var authorityTarget = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
            {
                ApplicationName = "authority-migrator-canary"
            };
            var applicationTarget = new NpgsqlConnectionStringBuilder(fixture.ConnectionString);
            string applicationRuntimeUsername = applicationTarget.Username + "_runtime";
            string applicationMigratorUsername = applicationTarget.Username + "_migrator";
            string authorityRuntimeUsername = authorityTarget.Username + "_runtime";
            string authorityMigratorUsername = authorityTarget.Username + "_migrator";
            IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["PrivacyErasure:Authority:Topology"] = "ExternalDatabase",
                    ["Database:Provider"] = "PostgreSql",
                    ["Database:Host"] = applicationTarget.Host,
                    ["Database:Port"] = applicationTarget.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["Database:Database"] = applicationTarget.Database,
                    ["Database:Runtime:Username"] = applicationRuntimeUsername,
                    ["Database:Runtime:Password"] = applicationTarget.Password,
                    ["Database:Runtime:TlsMode"] = "Prefer",
                    ["Database:Migrator:Username"] = applicationMigratorUsername,
                    ["Database:Migrator:Password"] = applicationTarget.Password,
                    ["PrivacyErasureAuthorityDatabase:Provider"] = "PostgreSql",
                    ["PrivacyErasureAuthorityDatabase:Host"] = authorityTarget.Host,
                    ["PrivacyErasureAuthorityDatabase:Port"] = authorityTarget.Port.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    ["PrivacyErasureAuthorityDatabase:Database"] = authorityTarget.Database,
                    ["PrivacyErasureAuthorityDatabase:TlsMode"] = "Prefer",
                    ["PrivacyErasureAuthorityDatabase:Runtime:Username"] = authorityRuntimeUsername,
                    ["PrivacyErasureAuthorityDatabase:Runtime:Password"] = authorityTarget.Password,
                    ["PrivacyErasureAuthorityDatabase:Migrator:Username"] = authorityMigratorUsername,
                    ["PrivacyErasureAuthorityDatabase:Migrator:Password"] = authorityTarget.Password
                }).Build();
            var services = new ServiceCollection();

            OptionsValidationException? exception = await Assert.That(() =>
                    services.ConfigurePersistenceServices(
                        configuration,
                        skipLookupCacheInitializer: true))
                .Throws<OptionsValidationException>();

            await Assert.That(exception!.Message)
                .Contains("different physical PostgreSQL database", StringComparison.OrdinalIgnoreCase);
            await Assert.That(services.Any(descriptor =>
                descriptor.ServiceType == typeof(IPrivacyErasureAuthority))).IsFalse();
        }
        finally
        {
            await ResetDatabaseAsync();
        }
    }

    private ExploreDbContext CreateExploreContext()
    {
        var builder = TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention();
        return new ExploreDbContext(builder.Options);
    }

    private DataProtectionKeyContext CreateDataProtectionContext()
    {
        var builder = TestDbContextOptions.Create<DataProtectionKeyContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention();
        return new DataProtectionKeyContext(builder.Options);
    }

    private PrivacyErasureAuthorityDbContext CreateAuthorityContext()
    {
        var builder = TestDbContextOptions.Create<PrivacyErasureAuthorityDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention();
        return new PrivacyErasureAuthorityDbContext(builder.Options);
    }

    private static string[] MigrationIds(DbContext context) =>
        context.GetService<IMigrationsAssembly>().Migrations.Keys.Order().ToArray();

    private static Migration InitMigration(DbContext context)
    {
        IMigrationsAssembly assembly = context.GetService<IMigrationsAssembly>();
        KeyValuePair<string, System.Reflection.TypeInfo> item = assembly.Migrations
            .Single(migration => migration.Key.EndsWith("_Init", StringComparison.Ordinal));
        return assembly.CreateMigration(item.Value, context.Database.ProviderName!);
    }

    private Task ResetDatabaseAsync() => fixture.ResetAsync();

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<int> ScalarIntAsync(string sql, params NpgsqlParameter[] parameters)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task<long> ScalarLongAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static PostgresException? FindPostgresException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres)
            {
                return postgres;
            }
        }

        return null;
    }
}
