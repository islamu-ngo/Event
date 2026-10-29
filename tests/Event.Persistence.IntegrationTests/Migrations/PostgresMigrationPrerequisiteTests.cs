#nullable enable

using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NpgsqlTypes;

namespace Event.Persistence.IntegrationTests.Migrations;

[ClassDataSource<RecipientDeliveryMigrationContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("RecipientDeliveryMigrationDb")]
public sealed class PostgresMigrationPrerequisiteTests(RecipientDeliveryMigrationContainerFixture fixture)
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [TUnit.Core.Executors.TestExecutor<FreshEfProcessExecutor>]
    public Task FreshMigrationInstallsGuardedDiscoveryInTargetSchemaAndRemainsRetrySafe(bool seed) =>
        RunFreshMigrationAsync(seed, failSecondSeedBatch: false);

    [Test]
    [TUnit.Core.Executors.TestExecutor<FreshEfProcessExecutor>]
    public Task SecondSeedBatchFailureRollsBackEverySetting() =>
        RunFreshMigrationAsync(seed: true, failSecondSeedBatch: true);

    [Test]
    [TUnit.Core.Executors.TestExecutor<FreshEfProcessExecutor>]
    public Task MixedPermissionBoundariesRemainFailClosedAfterFreshMigration() =>
        WithFreshMigrationDatabaseAsync(async (migration, options, configuration, databaseOptions, rolePasswords) =>
        {
            await ExploreDatabaseMigrator.MigrateAsync(migration, configuration);
            WritePhaseBestEffort("migration_public phase=first_returned");
            await Assert.That(await migration.Database.GetAppliedMigrationsAsync()).IsEquivalentTo(migration.Database.GetMigrations());
            string schema = databaseOptions.Schema;
            ((IDbContextOptionsBuilderInfrastructure)options).AddOrUpdateExtension(
                new RelationalNamespaceOptionsExtension(schema, schema));
            await using var observer = new ExploreDbContext(options.Options);
            observer.BindTenantLease();
            await observer.Database.OpenConnectionAsync();
            var enrollment = PostgresNativeQueueEnrollment.Bind(configuration);
            await PostgresDiscoveryTraversalContract.RequireReadyAsync(observer, enrollment);
            await RequireRevisionPolicyAsync(observer);

            var globalOptions = TestDbContextOptions.Create<ExploreDbContext>();
            PrimaryDatabaseProviderComposition.ConfigureApplication(globalOptions, databaseOptions with
            {
                Role = PrimaryDatabaseRole.Global,
                Username = enrollment.GlobalLogin,
                Password = rolePasswords[enrollment.GlobalLogin]
            });
            ((IDbContextOptionsBuilderInfrastructure)globalOptions).AddOrUpdateExtension(
                new RelationalNamespaceOptionsExtension(schema, schema));
            await using var globalObserver = new ExploreDbContext(globalOptions.Options);
            globalObserver.BindTenantLease();
            await globalObserver.Database.OpenConnectionAsync();
            await RequireStorageReadyAsync(globalObserver, enrollment);
            await AssertMixedPermissionBoundariesAsync(observer, globalObserver, enrollment, databaseOptions);
        });

    private Task RunFreshMigrationAsync(bool seed, bool failSecondSeedBatch)
    {
        return WithFreshMigrationDatabaseAsync(async (migration, options, configuration, databaseOptions, rolePasswords) =>
        {
            string schema = databaseOptions.Schema;
            if (seed)
                await AssertSeedFactoryPreservesAuthorityAsync(migration, configuration, databaseOptions);

            async Task MigrateAsync()
            {
                if (seed)
                    await ExploreDatabaseMigrator.MigrateAndSeedAsync(
                        ProductionHostEnvironment.Instance, configuration, databaseOptions,
                        NullLogger.Instance, seedDevelopmentData: false);
                else
                    await ExploreDatabaseMigrator.MigrateAsync(migration, configuration);
            }

            SeedBatchObservation? seedBatches = seed ? new(databaseOptions, failSecondSeedBatch) : null;
            try
            {
                if (failSecondSeedBatch)
                {
                    bool propagated = false;
                    try { await MigrateAsync(); }
                    catch (Exception failure) when (seedBatches!.IsInjectedFailure(failure)) { propagated = true; }
                    await Assert.That(propagated).IsTrue();
                    await Assert.That(seedBatches!.InjectedOnceAfterFirstCommand).IsTrue();
                }
                else
                {
                    await MigrateAsync();
                    WritePhaseBestEffort("migration_public phase=first_returned");
                }
            }
            finally { seedBatches?.Dispose(); }
            if (seedBatches is not null)
                await Assert.That(seedBatches.CleanupSucceeded).IsTrue();

            string[] history = (await migration.Database.GetAppliedMigrationsAsync()).ToArray();
            if (!failSecondSeedBatch)
                await Assert.That(history).IsEquivalentTo(migration.Database.GetMigrations());

            ((IDbContextOptionsBuilderInfrastructure)options).AddOrUpdateExtension(
                new RelationalNamespaceOptionsExtension(schema, schema));
            await using var observer = new ExploreDbContext(options.Options);
            observer.BindTenantLease();
            await observer.Database.OpenConnectionAsync();
            if (seedBatches is not null)
            {
                await seedBatches.AssertPersistenceAsync(observer, shouldExist: !failSecondSeedBatch);
                if (failSecondSeedBatch) return;
            }
            var enrollment = PostgresNativeQueueEnrollment.Bind(configuration);
            await PostgresDiscoveryTraversalContract.RequireReadyAsync(observer, enrollment);
            await RequireRevisionPolicyAsync(observer);

            var globalOptions = TestDbContextOptions.Create<ExploreDbContext>();
            PrimaryDatabaseProviderComposition.ConfigureApplication(globalOptions, databaseOptions with
            {
                Role = PrimaryDatabaseRole.Global,
                Username = enrollment.GlobalLogin,
                Password = rolePasswords[enrollment.GlobalLogin]
            });
            ((IDbContextOptionsBuilderInfrastructure)globalOptions).AddOrUpdateExtension(
                new RelationalNamespaceOptionsExtension(schema, schema));
            await using var globalObserver = new ExploreDbContext(globalOptions.Options);
            globalObserver.BindTenantLease();
            await globalObserver.Database.OpenConnectionAsync();
            await RequireStorageReadyAsync(globalObserver, enrollment);

            await MigrateAsync();
            WritePhaseBestEffort("migration_public phase=repeat_returned");
            await Assert.That(await migration.Database.GetAppliedMigrationsAsync()).IsEquivalentTo(history);
            await PostgresDiscoveryTraversalContract.RequireReadyAsync(observer, enrollment);
            await RequireRevisionPolicyAsync(observer);
            await RequireStorageReadyAsync(globalObserver, enrollment);

            if (!seed)
            {
                var connection = (NpgsqlConnection)observer.Database.GetDbConnection();
                var names = observer.GetService<ISqlGenerationHelper>();
                var tenant = observer.Model.FindEntityType(typeof(Explore.Domain.Tenant))!;
                var tenantStore = StoreObjectIdentifier.Table(tenant.GetTableName()!, tenant.GetSchema());
                string tenantTable = names.DelimitIdentifier(tenantStore.Name, tenantStore.Schema);
                string slugColumn = tenant.FindProperty(nameof(Explore.Domain.Tenant.Slug))!.GetColumnName(tenantStore)!;
                string runtime = names.DelimitIdentifier(enrollment.RuntimeLogin);
                await ExecuteAsync(connection,
                    $"GRANT SELECT ({names.DelimitIdentifier(slugColumn)}) ON {tenantTable} TO {runtime}");
                try
                {
                    var escalation = await Assert.That(async () =>
                        await PostgresDiscoveryTraversalContract.RequireReadyAsync(observer, enrollment))
                        .Throws<PostgresException>();
                    await Assert.That(escalation!.SqlState).IsEqualTo("42501");
                    await Assert.That(escalation.MessageText).IsEqualTo("discovery_traversal_runtime_source_privilege");
                    bool retained = await observer.Database.SqlQueryRaw<bool>(
                        """
                        SELECT pg_catalog.has_column_privilege(@login, @relation::regclass, @column, 'SELECT') AS "Value"
                        """, new NpgsqlParameter("login", enrollment.RuntimeLogin),
                        new NpgsqlParameter("relation", tenantTable), new NpgsqlParameter("column", slugColumn)).SingleAsync();
                    await Assert.That(retained).IsTrue();
                }
                finally
                {
                    await ExecuteAsync(connection,
                        $"REVOKE SELECT ({names.DelimitIdentifier(slugColumn)}) ON {tenantTable} FROM {runtime}");
                }
                await PostgresDiscoveryTraversalContract.RequireReadyAsync(observer, enrollment);

                (Type Type, string Property, string Privilege)[] undeclared =
                [
                    (typeof(Explore.Domain.Actor), nameof(Explore.Domain.Actor.IsSuspended), "SELECT"),
                    (typeof(Explore.Domain.TenantUser), nameof(Explore.Domain.TenantUser.UserId), "UPDATE")
                ];
                foreach (var grant in undeclared)
                {
                    var entity = observer.Model.FindEntityType(grant.Type)!;
                    var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
                    string table = names.DelimitIdentifier(store.Name, store.Schema);
                    string column = entity.FindProperty(grant.Property)!.GetColumnName(store)!;
                    await ExecuteAsync(connection,
                        $"GRANT {grant.Privilege} ({names.DelimitIdentifier(column)}) ON {table} TO {runtime}");
                    try
                    {
                        var escalation = await Assert.That(async () =>
                            await PostgresPublicEventDiscoveryContract.ApplyAsync(observer, enrollment))
                            .Throws<PostgresException>();
                        await Assert.That(escalation!.SqlState).IsEqualTo("42501");
                        await Assert.That(escalation.MessageText).IsEqualTo("public_discovery_runtime_source_privilege");
                        var publication = await Assert.That(async () =>
                            await PostgresPublicationFanoutExecutionContract.ApplyAsync(observer, enrollment))
                            .Throws<PostgresException>();
                        await Assert.That(publication!.SqlState).IsEqualTo("42501");
                        await Assert.That(publication.MessageText).IsEqualTo("publication_fanout_private_source_privilege");
                        bool retained = await observer.Database.SqlQueryRaw<bool>(
                            """
                            SELECT pg_catalog.has_column_privilege(@login, @relation::regclass, @column, @privilege) AS "Value"
                            """, new NpgsqlParameter("login", enrollment.RuntimeLogin),
                            new NpgsqlParameter("relation", table), new NpgsqlParameter("column", column),
                            new NpgsqlParameter("privilege", grant.Privilege)).SingleAsync();
                        await Assert.That(retained).IsTrue();
                    }
                    finally
                    {
                        await ExecuteAsync(connection,
                            $"REVOKE {grant.Privilege} ({names.DelimitIdentifier(column)}) ON {table} FROM {runtime}");
                    }
                    await PostgresPublicEventDiscoveryContract.ApplyAsync(observer, enrollment);
                    await PostgresPublicationFanoutExecutionContract.ApplyAsync(observer, enrollment);
                }

                string global = names.DelimitIdentifier(enrollment.GlobalLogin);
                var user = observer.Model.FindEntityType(typeof(Explore.Domain.User))!;
                var userStore = StoreObjectIdentifier.Table(user.GetTableName()!, user.GetSchema());
                string userTable = names.DelimitIdentifier(userStore.Name, userStore.Schema);
                string deletedColumn = user.FindProperty(nameof(Explore.Domain.User.IsDeleted))!.GetColumnName(userStore)!;
                (string Diagnostic, Func<Task> Apply)[] privateReaders =
                [
                    ("ai_actor_context_private_source_privilege", () => PostgresAiActorContextReadContract.ApplyAsync(observer, enrollment)),
                    ("event_creation_publisher_private_source_privilege", () => PostgresEventCreationPublisherReadContract.ApplyAsync(observer, enrollment))
                ];
                await ExecuteAsync(connection,
                    $"GRANT UPDATE ({names.DelimitIdentifier(deletedColumn)}) ON {userTable} TO {global}");
                try
                {
                    foreach (var reader in privateReaders)
                    {
                        var escalation = await Assert.That(async () => await reader.Apply()).Throws<PostgresException>();
                        await Assert.That(escalation!.SqlState).IsEqualTo("42501");
                        await Assert.That(escalation.MessageText).IsEqualTo(reader.Diagnostic);
                        bool retained = await observer.Database.SqlQueryRaw<bool>(
                            """
                            SELECT pg_catalog.has_column_privilege(@login, @relation::regclass, @column, 'UPDATE') AS "Value"
                            """, new NpgsqlParameter("login", enrollment.GlobalLogin),
                            new NpgsqlParameter("relation", userTable), new NpgsqlParameter("column", deletedColumn)).SingleAsync();
                        await Assert.That(retained).IsTrue();
                    }
                }
                finally
                {
                    await ExecuteAsync(connection,
                        $"REVOKE UPDATE ({names.DelimitIdentifier(deletedColumn)}) ON {userTable} FROM {global}");
                }
                foreach (var reader in privateReaders) await reader.Apply();

                await ExecuteAsync(connection,
                    $"CREATE POLICY unexpected_worker_policy ON migration_target.tenant_setting_overrides FOR SELECT TO {global} USING (true)");
                try
                {
                    var policy = await Assert.That(async () =>
                        await PostgresStorageProbeSourceContract.RequireReadyAsync(observer, enrollment))
                        .Throws<PostgresException>();
                    await Assert.That(policy!.SqlState).IsEqualTo("55000");
                    await Assert.That(policy.MessageText).IsEqualTo("storage_probe_source_rls_drift");
                    bool retained = await observer.Database.SqlQueryRaw<bool>(
                        """
                        SELECT EXISTS (
                            SELECT 1 FROM pg_catalog.pg_policy p
                            WHERE p.polrelid = 'migration_target.tenant_setting_overrides'::regclass
                                AND p.polname = 'unexpected_worker_policy'
                                AND p.polroles = ARRAY[pg_catalog.to_regrole(@login)::oid]
                        ) AS "Value"
                        """, new NpgsqlParameter("login", enrollment.GlobalLogin)).SingleAsync();
                    await Assert.That(retained).IsTrue();
                }
                finally
                {
                    await ExecuteAsync(connection,
                        "DROP POLICY unexpected_worker_policy ON migration_target.tenant_setting_overrides");
                }
                await PostgresStorageProbeSourceContract.RequireReadyAsync(observer, enrollment);

                (string Name, string Error)[] triggers =
                [
                    ("policy_source_set_write", "policy_source_set_trigger_drift"),
                    ("policy_source_set_truncate", "policy_source_set_trigger_drift"),
                    ("storage_configuration_changed", "storage_configuration_exclusion_drift"),
                    ("ordinary_settings_changed", "ordinary_settings_trigger_drift"),
                    ("ordinary_settings_row", "ordinary_settings_trigger_drift"),
                    ("event_resource_provider_changed", "resource_provider_trigger_drift"),
                    ("event_resource_provider_row", "resource_provider_trigger_drift")
                ];
                foreach (var trigger in triggers)
                {
                    await ExecuteAsync(connection,
                        $"ALTER TABLE migration_target.tenant_setting_overrides DISABLE TRIGGER \"{trigger.Name}\"");
                    try
                    {
                        var exception = await Assert.That(async () =>
                            await PostgresManagedTenantWorkerCreationPermissions.ApplyAsync(
                                observer, enrollment, IdentityDatabaseTopology.Colocated)).Throws<PostgresException>();
                        await Assert.That(exception!.SqlState).IsEqualTo("55000");
                        await Assert.That(exception.MessageText).IsEqualTo(trigger.Error);
                        bool disabled = await observer.Database.SqlQueryRaw<bool>(
                            """
                            SELECT EXISTS (
                                SELECT 1 FROM pg_catalog.pg_trigger t
                                WHERE t.tgrelid = 'migration_target.tenant_setting_overrides'::regclass
                                    AND t.tgname = @trigger AND t.tgenabled = 'D'
                            ) AS "Value"
                            """, new NpgsqlParameter("trigger", trigger.Name)).SingleAsync();
                        await Assert.That(disabled).IsTrue();
                    }
                    finally
                    {
                        await ExecuteAsync(connection,
                            $"ALTER TABLE migration_target.tenant_setting_overrides ENABLE ALWAYS TRIGGER \"{trigger.Name}\"");
                    }
                }

                await ExecuteAsync(connection,
                    """
                    CREATE TRIGGER unexpected_worker_source AFTER INSERT OR UPDATE OR DELETE
                    ON migration_target.tenant_setting_overrides FOR EACH ROW
                    EXECUTE FUNCTION migration_target.policy_source_set_setting_write();
                    ALTER TABLE migration_target.tenant_setting_overrides ENABLE ALWAYS TRIGGER unexpected_worker_source;
                    """);
                try
                {
                    var unknown = await Assert.That(async () =>
                        await PostgresManagedTenantWorkerCreationPermissions.ApplyAsync(
                            observer, enrollment, IdentityDatabaseTopology.Colocated)).Throws<UnauthorizedAccessException>();
                    await Assert.That(unknown!.Message).IsEqualTo(
                        "managed_tenant_worker_creation_permissions_not_ready:storage:TenantSetting");
                    bool unknownRetained = await observer.Database.SqlQueryRaw<bool>(
                        """
                        SELECT EXISTS (
                            SELECT 1 FROM pg_catalog.pg_trigger t
                            WHERE t.tgrelid = 'migration_target.tenant_setting_overrides'::regclass
                                AND t.tgname = 'unexpected_worker_source' AND t.tgenabled = 'A'
                        ) AS "Value"
                        """).SingleAsync();
                    await Assert.That(unknownRetained).IsTrue();
                }
                finally
                {
                    await ExecuteAsync(connection,
                        "DROP TRIGGER unexpected_worker_source ON migration_target.tenant_setting_overrides");
                }

                string login = observer.GetService<ISqlGenerationHelper>().DelimitIdentifier(enrollment.GlobalLogin);
                bool workerSource = PostgresManagedTenantWorkerCreationPermissions.TryGetGlobalColumns(
                    observer, typeof(Explore.Domain.TenantSetting), out _, out var workerInsert, out _);
                await Assert.That(workerSource).IsTrue();
                PostgresTenantCreationPermissions.TryGetGlobalColumns(
                    observer, typeof(Explore.Domain.TenantSetting), out _, out var creatorInsert);
                string[] insertPreimage = workerInsert.Concat(creatorInsert).Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal).ToArray();
                await Assert.That(insertPreimage.Length > 0).IsTrue();
                string settingTable = Mapping<Explore.Domain.TenantSetting>(observer, nameof(Explore.Domain.TenantSetting.Id)).Table;
                async Task AssertSettingInsertPreimageAsync()
                {
                    bool exact = await observer.Database.SqlQueryRaw<bool>(
                        """
                        SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_class c
                            WHERE c.oid=@relation::regclass AND c.relowner=pg_catalog.to_regrole(@issuer))
                            AND NOT pg_catalog.has_table_privilege(@login,@relation::regclass,'INSERT')
                            AND cardinality(@columns)>0
                            AND ARRAY(SELECT a.attname::text FROM pg_catalog.pg_attribute a
                                CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                                WHERE a.attrelid=@relation::regclass AND a.attnum>0 AND NOT a.attisdropped
                                    AND acl.grantee=pg_catalog.to_regrole(@login) AND acl.privilege_type='INSERT'
                                ORDER BY a.attname::text COLLATE "C")=@columns
                            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                                CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                                WHERE a.attrelid=@relation::regclass AND acl.privilege_type='INSERT'
                                    AND (acl.grantee=0 OR (acl.grantee=pg_catalog.to_regrole(@login)
                                        AND (acl.grantor<>pg_catalog.to_regrole(@issuer) OR acl.is_grantable)))) AS "Value"
                        """, new NpgsqlParameter("relation", NpgsqlDbType.Text) { Value = settingTable },
                        new NpgsqlParameter("issuer", NpgsqlDbType.Text) { Value = enrollment.MigratorLogin },
                        new NpgsqlParameter("login", NpgsqlDbType.Text) { Value = enrollment.GlobalLogin },
                        new NpgsqlParameter("columns", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = insertPreimage }).SingleAsync();
                    await Assert.That(exact).IsTrue();
                }
                await RequireStorageReadyAsync(globalObserver, enrollment);
                await AssertSettingInsertPreimageAsync();
                var escalationFailures = new List<Exception>();
                try
                {
                    await ExecuteAsync(connection,
                        $"GRANT INSERT ON migration_target.tenant_setting_overrides TO {login}");
                    var escalation = await Assert.That(async () =>
                        await PostgresEventResourceProviderContract.RequireReadyAsync(observer, enrollment))
                        .Throws<PostgresException>();
                    await Assert.That(escalation!.SqlState).IsEqualTo("55000");
                    await Assert.That(escalation.MessageText).IsEqualTo("resource_provider_ambient_write");
                    bool retained = await observer.Database.SqlQueryRaw<bool>(
                        """
                        SELECT pg_catalog.has_table_privilege(
                            @login, 'migration_target.tenant_setting_overrides'::regclass, 'INSERT') AS "Value"
                        """, new NpgsqlParameter("login", enrollment.GlobalLogin)).SingleAsync();
                    await Assert.That(retained).IsTrue();
                }
                catch (Exception failure) { escalationFailures.Add(failure); }
                finally
                {
                    try
                    {
                        await ExecuteAsync(connection,
                            $"REVOKE INSERT ON migration_target.tenant_setting_overrides FROM {login}");
                    }
                    catch (Exception failure) { escalationFailures.Add(failure); }
                    try
                    {
                        string columns = string.Join(',', insertPreimage.Select(names.DelimitIdentifier));
                        await ExecuteAsync(connection, $"GRANT INSERT ({columns}) ON {settingTable} TO {login}");
                    }
                    catch (Exception failure) { escalationFailures.Add(failure); }
                }
                if (escalationFailures.Count == 1) ExceptionDispatchInfo.Capture(escalationFailures[0]).Throw();
                if (escalationFailures.Count > 1)
                    throw new AggregateException("migration_table_insert_escalation_failed", escalationFailures);
                await AssertSettingInsertPreimageAsync();
                await RequireStorageReadyAsync(globalObserver, enrollment);
                await PostgresEventResourceProviderContract.RequireReadyAsync(observer, enrollment);
                await AssertStoragePermissionBoundariesAsync(observer, globalObserver, enrollment);
                await PostgresMixedSettingsContract.RequireReadyAsync(observer, enrollment);
                await PostgresOperatorIdentityEditorContract.RequireReadyAsync(observer, enrollment);
            }
        });
    }

    private async Task WithFreshMigrationDatabaseAsync(Func<ExploreDbContext, DbContextOptionsBuilder<ExploreDbContext>,
        IConfiguration, PrimaryDatabaseConnectionOptions, IReadOnlyDictionary<string, string>, Task> exercise)
    {
        string suffix = Guid.CreateVersion7().ToString("N");
        string databaseName = $"migration_prerequisite_{suffix}";
        const string schema = "migration_target";
        var administrator = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Pooling = false };
        await using var administration = new NpgsqlConnection(administrator.ConnectionString);
        await administration.OpenAsync();
        var createdRoles = new List<string>();
        var rolePasswords = new Dictionary<string, string>(StringComparer.Ordinal);
        var configuration = new ConfigurationManager
        {
            ["Authentication:Provider"] = "Keycloak",
            ["IdentityDatabase:Topology"] = "colocated",
            ["PrivacyErasure:Authority:Topology"] = "CoLocated",
            ["Database:Migrator:Username"] = administrator.Username
        };

        await ExecuteAsync(administration, $"CREATE DATABASE \"{databaseName}\"");
        try
        {
            foreach (PrimaryDatabaseRole role in Enum.GetValues<PrimaryDatabaseRole>()
                         .Where(role => role is not PrimaryDatabaseRole.Migrator and not PrimaryDatabaseRole.LocalIdentityWriter))
            {
                string login = $"prerequisite_{(int)role}_{suffix}";
                string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
                await ExecuteAsync(administration,
                    $"CREATE ROLE \"{login}\" LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS PASSWORD '{password}'");
                createdRoles.Add(login);
                rolePasswords.Add(login, password);
                configuration[$"Database:{role}:Username"] = login;
            }

            var databaseOptions = new PrimaryDatabaseConnectionOptions
            {
                Role = PrimaryDatabaseRole.Migrator,
                Provider = PrimaryDatabaseProvider.PostgreSql,
                Host = administrator.Host!,
                Port = administrator.Port,
                Database = databaseName,
                Username = administrator.Username,
                Password = administrator.Password,
                Schema = schema,
                TlsMode = PrimaryDatabaseTlsMode.Disabled
            };
            var options = TestDbContextOptions.Create<ExploreDbContext>();
            PrimaryDatabaseProviderComposition.ConfigureApplication(options, databaseOptions);
            await using var migration = new ExploreDbContext(options.Options);
            await Assert.That(await migration.Database.GetAppliedMigrationsAsync()).IsEmpty();
            await exercise(migration, options, configuration, databaseOptions, rolePasswords);
        }
        finally
        {
            await ExecuteAsync(administration, $"DROP DATABASE \"{databaseName}\" WITH (FORCE)");
            foreach (string login in createdRoles)
                await ExecuteAsync(administration, $"DROP ROLE \"{login}\"");
        }
    }

    private static async Task AssertSeedFactoryPreservesAuthorityAsync(
        ExploreDbContext original, IConfiguration configuration, PrimaryDatabaseConnectionOptions declared)
    {
        var originalOptions = original.GetService<IDbContextOptions>();
        var originalExtensions = originalOptions.Extensions.ToArray();
        var originalRelational = originalExtensions.OfType<RelationalOptionsExtension>().Single();
        int? originalBatchSize = originalRelational.MaxBatchSize;
        Guid? originalTenant = original.TenantFilterTenantId;
        bool originalBypass = original.IsTenantFilterBypassed;
        string? originalBypassReason = original.TenantFilterBypassReason;
        bool originalRetries = original.Database.CreateExecutionStrategy().RetriesOnFailure;
        var enrollment = PostgresNativeQueueEnrollment.Bind(configuration);
        var factory = typeof(ExploreDatabaseMigrator).GetMethod("CreateNamespaceExecutionContext",
            BindingFlags.Static | BindingFlags.NonPublic, null, [typeof(ExploreDbContext), typeof(bool)], null)!;
        await using var seeding = (ExploreDbContext)factory.Invoke(null, [original, true])!;
        await using var migrating = (ExploreDbContext)factory.Invoke(null, [original, false])!;
        foreach (var candidate in new[] { (Context: seeding, Seeding: true), (Context: migrating, Seeding: false) })
        {
            var candidateOptions = candidate.Context.GetService<IDbContextOptions>();
            var relational = candidateOptions.Extensions.OfType<RelationalOptionsExtension>().Single();
            var mapping = candidateOptions.FindExtension<RelationalNamespaceOptionsExtension>()!;
            var connection = new NpgsqlConnectionStringBuilder(relational.ConnectionString!);
            bool sameConnection = relational.ConnectionString == originalRelational.ConnectionString
                && ReferenceEquals(relational.Connection, originalRelational.Connection);
            bool sameDeclaredPurpose = declared.Role == PrimaryDatabaseRole.Migrator
                && connection.Username == enrollment.MigratorLogin && connection.Username == declared.Username
                && connection.Database == declared.Database && connection.Host == declared.Host && connection.Port == declared.Port;
            bool sameHistory = relational.MigrationsAssembly == originalRelational.MigrationsAssembly
                && relational.MigrationsHistoryTableName == originalRelational.MigrationsHistoryTableName
                && relational.MigrationsHistoryTableSchema == originalRelational.MigrationsHistoryTableSchema;
            var originalServices = originalExtensions.Where(extension => !extension.Info.IsDatabaseProvider
                && extension is not RelationalNamespaceOptionsExtension).ToArray();
            var candidateServices = candidateOptions.Extensions.Where(extension => !extension.Info.IsDatabaseProvider
                && extension is not RelationalNamespaceOptionsExtension).ToArray();
            bool sameServices = originalServices.Length == candidateServices.Length
                && originalServices.All(extension => candidateServices.Any(candidateExtension => ReferenceEquals(extension, candidateExtension)));
            bool sameRetry = ReferenceEquals(relational.ExecutionStrategyFactory, originalRelational.ExecutionStrategyFactory)
                && originalRetries && candidate.Context.Database.CreateExecutionStrategy().RetriesOnFailure;
            bool sameNamespace = mapping.ModelSchema == declared.Schema && mapping.TargetSchema == declared.Schema;
            bool onlySeedingBatchesChanged = relational.MaxBatchSize == (candidate.Seeding ? 1 : originalBatchSize);
            candidate.Context.RequireBoundTenantLease();
            bool sameGlobalLease = candidate.Context.TenantFilterTenantId is null && !candidate.Context.IsTenantFilterBypassed;
            await Assert.That(sameConnection).IsTrue();
            await Assert.That(sameDeclaredPurpose).IsTrue();
            await Assert.That(sameHistory).IsTrue();
            await Assert.That(sameServices).IsTrue();
            await Assert.That(sameRetry).IsTrue();
            await Assert.That(sameNamespace).IsTrue();
            await Assert.That(onlySeedingBatchesChanged).IsTrue();
            await Assert.That(sameGlobalLease).IsTrue();
            await Assert.That(relational.CommandTimeout == 30 && relational.CommandTimeout == originalRelational.CommandTimeout).IsTrue();
            await Assert.That(relational.QuerySplittingBehavior == QuerySplittingBehavior.SplitQuery
                && relational.QuerySplittingBehavior == originalRelational.QuerySplittingBehavior).IsTrue();
        }
        bool originalUnmutated = ReferenceEquals(original.GetService<IDbContextOptions>(), originalOptions)
            && originalOptions.Extensions.Count() == originalExtensions.Length
            && originalExtensions.All(extension => originalOptions.Extensions.Any(candidate => ReferenceEquals(extension, candidate)))
            && originalRelational.MaxBatchSize == originalBatchSize && original.TenantFilterTenantId == originalTenant
            && original.IsTenantFilterBypassed == originalBypass && original.TenantFilterBypassReason == originalBypassReason;
        await Assert.That(originalUnmutated).IsTrue();
        await Assert.That(enrollment == PostgresNativeQueueEnrollment.Bind(configuration)).IsTrue();
    }

    private sealed class SeedBatchInvariantException() : Exception("seed_batch_atomicity_breaker");

    private sealed class SeedBatchObservation : IObserver<DiagnosticListener>,
        IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly PrimaryDatabaseConnectionOptions _declared;
        private readonly bool _failSecondBatch;
        private readonly IDisposable _listeners;
        private readonly List<IDisposable> _subscriptions = [];
        private readonly HashSet<ExploreDbContext> _contexts = [];
        private readonly HashSet<Guid> _commands = [];
        private readonly HashSet<Guid> _executed = [];
        private readonly SeedBatchInvariantException _failure = new();
        private ExploreDbContext? _writer;
        private DbContextId _contextId;
        private NpgsqlConnection? _connection;
        private DbTransaction? _transaction;
        private int _backend;
        private Guid[] _ids = [];
        private Guid _firstCommand;
        private int _injections;
        private bool _saveCompleted;
        private bool _observingAttempt;
        private string? _table;
        private string? _idColumn;

        internal SeedBatchObservation(PrimaryDatabaseConnectionOptions declared, bool failSecondBatch)
        {
            _declared = declared;
            _failSecondBatch = failSecondBatch;
            _listeners = DiagnosticListener.AllListeners.Subscribe(this);
        }

        internal bool InjectedOnceAfterFirstCommand => _injections == 1 && _commands.Count == 2
            && _executed.Contains(_firstCommand);
        internal bool CleanupSucceeded { get; private set; } = true;

        internal bool IsInjectedFailure(Exception failure) => ReferenceEquals(failure, _failure)
            || failure is DbUpdateException { InnerException: { } inner } && ReferenceEquals(inner, _failure);

        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name == DbLoggerCategory.Name)
                _subscriptions.Add(listener.Subscribe(this, name =>
                    name == RelationalEventId.CommandExecuting.Name || name == RelationalEventId.CommandExecuted.Name));
        }

        public void OnNext(KeyValuePair<string, object?> diagnostic)
        {
            if (diagnostic.Key == RelationalEventId.CommandExecuting.Name && diagnostic.Value is CommandEventData command)
            {
                if (command.Context is ExploreDbContext context && MatchesDeclaredConnection(context) && _contexts.Add(context))
                {
                    context.SavingChanges += CaptureWriteSet;
                    context.SavedChanges += SaveCompleted;
                }
                ObserveCommand(command);
            }
            else if (diagnostic.Key == RelationalEventId.CommandExecuted.Name
                && diagnostic.Value is CommandExecutedEventData executed && ReferenceEquals(executed.Context, _writer)
                && _commands.Contains(executed.CommandId))
                _executed.Add(executed.CommandId);
        }

        private bool MatchesDeclaredConnection(ExploreDbContext context)
        {
            if (_declared.Role != PrimaryDatabaseRole.Migrator || !context.Database.IsNpgsql()
                || context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema != _declared.Schema
                || context.Database.GetDbConnection() is not NpgsqlConnection connection)
                return false;
            var actual = new NpgsqlConnectionStringBuilder(connection.ConnectionString);
            return actual.Database == _declared.Database && actual.Username == _declared.Username
                && actual.Host == _declared.Host && actual.Port == _declared.Port;
        }

        private void CaptureWriteSet(object? sender, SavingChangesEventArgs args)
        {
            if (sender is not ExploreDbContext context || _writer is not null && !ReferenceEquals(_writer, context)
                || !MatchesDeclaredConnection(context)
                || context.TenantFilterTenantId is not null || !context.IsTenantFilterBypassed
                || context.TenantFilterBypassReason != TenantFilterBypassReasons.DatabaseSeeding)
                return;
            var writes = context.ChangeTracker.Entries()
                .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToArray();
            if (writes.Length != 61 || writes.Any(entry => entry.State != EntityState.Added || entry.Entity is not Explore.Domain.SystemSetting))
            {
                _observingAttempt = false;
                return;
            }
            Guid[] ids = writes.Select(entry => ((Explore.Domain.SystemSetting)entry.Entity).Id).ToArray();
            if (_writer is not null && !ids.ToHashSet().SetEquals(_ids))
            {
                _observingAttempt = false;
                return;
            }
            context.RequireBoundTenantLease();
            if (_writer is null)
            {
                _writer = context;
                _contextId = context.ContextId;
                _connection = (NpgsqlConnection)context.Database.GetDbConnection();
                _ids = ids;
                var entity = context.Model.FindEntityType(typeof(Explore.Domain.SystemSetting))!;
                var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
                var names = context.GetService<ISqlGenerationHelper>();
                _table = names.DelimitIdentifier(store.Name, store.Schema);
                _idColumn = names.DelimitIdentifier(entity.FindProperty(nameof(Explore.Domain.SystemSetting.Id))!.GetColumnName(store)!);
            }
            else if (context.ContextId != _contextId || !ReferenceEquals(context.Database.GetDbConnection(), _connection))
                throw new InvalidOperationException("seed_batch_original_owner_required");
            _commands.Clear();
            _executed.Clear();
            _transaction = null;
            _backend = 0;
            _firstCommand = Guid.Empty;
            _saveCompleted = false;
            _observingAttempt = true;
        }

        private void ObserveCommand(CommandEventData observed)
        {
            if (!_observingAttempt || _saveCompleted || !ReferenceEquals(observed.Context, _writer) || _writer is null
                || observed.CommandSource != CommandSource.SaveChanges)
                return;
            string[] statements = observed.Command.CommandText.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (statements.Length != 1 || !statements[0].StartsWith($"INSERT INTO {_table} (", StringComparison.Ordinal))
                throw new InvalidOperationException("seed_batch_single_setting_command_required");
            _writer.RequireBoundTenantLease();
            var physical = _writer.Database.CurrentTransaction?.GetDbTransaction();
            var connection = _connection;
            if (_writer.ContextId != _contextId || !MatchesDeclaredConnection(_writer)
                || connection is null || !ReferenceEquals(observed.Command.Connection, connection) || connection.State != ConnectionState.Open
                || physical is null || !ReferenceEquals(observed.Command.Transaction, physical))
                throw new InvalidOperationException("seed_batch_original_owner_required");
            if (_transaction is null)
            {
                _transaction = physical;
                _backend = connection.ProcessID;
            }
            if (!ReferenceEquals(_transaction, physical) || connection.ProcessID != _backend)
                throw new InvalidOperationException("seed_batch_original_transaction_required");
            if (!_commands.Add(observed.CommandId))
                throw new InvalidOperationException("seed_batch_unique_command_required");
            if (_commands.Count == 1) _firstCommand = observed.CommandId;
            if (_failSecondBatch && _commands.Count == 2)
            {
                if (!_executed.Contains(_firstCommand))
                    throw new InvalidOperationException("seed_batch_first_execution_required");
                _injections++;
                throw _failure;
            }
        }

        private void SaveCompleted(object? sender, SavedChangesEventArgs args)
        {
            if (_observingAttempt && ReferenceEquals(sender, _writer) && _commands.Count != 0) _saveCompleted = true;
        }

        internal async Task AssertPersistenceAsync(ExploreDbContext observer, bool shouldExist)
        {
            await Assert.That(_ids.Length == 61 && _ids.Distinct().Count() == 61).IsTrue();
            await Assert.That(_commands.Count == (shouldExist ? 61 : 2)).IsTrue();
            await Assert.That(!shouldExist || _saveCompleted && _executed.Count == 61).IsTrue();
            await using var command = new NpgsqlCommand(
                $"SELECT count(*) = @expected FROM {_table} WHERE {_idColumn} = ANY(@ids)",
                (NpgsqlConnection)observer.Database.GetDbConnection());
            command.Parameters.AddWithValue("expected", shouldExist ? 61 : 0);
            command.Parameters.AddWithValue("ids", _ids);
            bool persisted = (bool)(await command.ExecuteScalarAsync())!;
            await Assert.That(persisted).IsTrue();
        }

        public void Dispose()
        {
            try { _listeners.Dispose(); }
            catch { CleanupSucceeded = false; }
            foreach (var subscription in _subscriptions)
            {
                try { subscription.Dispose(); }
                catch { CleanupSucceeded = false; }
            }
            foreach (var context in _contexts)
            {
                try { context.SavingChanges -= CaptureWriteSet; }
                catch { CleanupSucceeded = false; }
                try { context.SavedChanges -= SaveCompleted; }
                catch { CleanupSucceeded = false; }
            }
        }

        public void OnError(Exception error) { }
        public void OnCompleted() { }
    }

    private static async Task RequireStorageReadyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        await PostgresStorageReconciliationContract.ValidateReadinessAsync(context, enrollment);
        await PostgresLocalStorageQuarantineContract.RequireReadyAsync(context, enrollment);
    }

    private static async Task RequireStorageRejectedAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var reconciliation = await Assert.That(async () =>
            await PostgresStorageReconciliationContract.ValidateReadinessAsync(context, enrollment))
            .Throws<InvalidOperationException>();
        await Assert.That(reconciliation!.Message).IsEqualTo("storage_reconciliation_authority_not_ready");
        var quarantine = await Assert.That(async () =>
            await PostgresLocalStorageQuarantineContract.RequireReadyAsync(context, enrollment))
            .Throws<InvalidOperationException>();
        await Assert.That(quarantine!.Message).IsEqualTo("local_storage_quarantine_authority_not_ready");
    }

    private static (string Table, string Column) Mapping<T>(ExploreDbContext context, string property) where T : class
    {
        var entity = context.Model.FindEntityType(typeof(T))!;
        var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var names = context.GetService<ISqlGenerationHelper>();
        return (names.DelimitIdentifier(store.Name, store.Schema),
            names.DelimitIdentifier(entity.FindProperty(property)!.GetColumnName(store)!));
    }

    private static async Task AssertStoragePermissionBoundariesAsync(
        ExploreDbContext migrator, ExploreDbContext global, PostgresNativeQueueEnrollment enrollment)
    {
        var names = migrator.GetService<ISqlGenerationHelper>();
        string login = names.DelimitIdentifier(enrollment.GlobalLogin);
        var inside = Mapping<Explore.Domain.OutboxMessage>(migrator, nameof(Explore.Domain.OutboxMessage.Status));
        var outside = Mapping<Explore.Domain.Category>(migrator, nameof(Explore.Domain.Category.FullName));
        var connection = (NpgsqlConnection)migrator.Database.GetDbConnection();
        await AssertIsolatedGlobalCatalogueAsync(migrator, enrollment);
        await AssertAuthenticationPermissionBoundariesAsync(migrator, global, enrollment);
        foreach (var grant in new[]
        {
            (Privilege: "UPDATE", Source: inside, Column: true),
            (Privilege: "SELECT", Source: outside, Column: true),
            (Privilege: "SELECT", Source: outside, Column: false)
        })
        {
            string permission = grant.Privilege + (grant.Column ? $" ({grant.Source.Column})" : "");
            await ExecuteAsync(connection, $"GRANT {permission} ON {grant.Source.Table} TO {login}");
            try
            {
                await RequireStorageRejectedAsync(global, enrollment);
                bool retained = await migrator.Database.SqlQueryRaw<bool>(
                    grant.Column
                        ? """SELECT pg_catalog.has_column_privilege(@login,@relation::regclass,@column,@privilege) AS "Value" """
                        : """SELECT pg_catalog.has_table_privilege(@login,@relation::regclass,@privilege) AS "Value" """,
                    new NpgsqlParameter("login", enrollment.GlobalLogin), new NpgsqlParameter("relation", grant.Source.Table),
                    new NpgsqlParameter("column", grant.Source.Column.Trim('"')), new NpgsqlParameter("privilege", grant.Privilege)).SingleAsync();
                await Assert.That(retained).IsTrue();
            }
            finally { await ExecuteAsync(connection, $"REVOKE {permission} ON {grant.Source.Table} FROM {login}"); }
            await RequireStorageReadyAsync(global, enrollment);
        }

        string parent = names.DelimitIdentifier($"prerequisite_extra_{Guid.CreateVersion7():N}");
        await ExecuteAsync(connection, $"CREATE ROLE {parent} NOLOGIN NOINHERIT");
        try
        {
            await ExecuteAsync(connection, $"GRANT SELECT ({outside.Column}) ON {outside.Table} TO {parent}");
            await ExecuteAsync(connection, $"GRANT {parent} TO {login} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE");
            await RequireStorageRejectedAsync(global, enrollment);
            bool retained = await migrator.Database.SqlQueryRaw<bool>(
                """SELECT pg_catalog.pg_has_role(@login,@parent,'MEMBER') AND NOT pg_catalog.pg_has_role(@login,@parent,'USAGE') AS "Value" """,
                new NpgsqlParameter("login", enrollment.GlobalLogin), new NpgsqlParameter("parent", parent.Trim('"'))).SingleAsync();
            await Assert.That(retained).IsTrue();
        }
        finally
        {
            await ExecuteAsync(connection, $"REVOKE {parent} FROM {login}");
            await ExecuteAsync(connection, $"REVOKE SELECT ({outside.Column}) ON {outside.Table} FROM {parent}");
            await ExecuteAsync(connection, $"DROP ROLE {parent}");
        }
        await RequireStorageReadyAsync(global, enrollment);

        string capability = PostgresLocalStorageQuarantineContract.GlobalRole;
        string runtime = names.DelimitIdentifier(enrollment.RuntimeLogin);
        foreach (string change in new[] { "missing", "set", "admin", "runtime_member" })
        {
            await ExecuteAsync(connection, change == "missing"
                ? $"REVOKE {capability} FROM {login}"
                : $"GRANT {capability} TO {(change == "runtime_member" ? runtime : login)} WITH INHERIT TRUE, SET {(change == "set" ? "TRUE" : "FALSE")}, ADMIN {(change == "admin" ? "TRUE" : "FALSE")}");
            try
            {
                var rejected = await Assert.That(async () =>
                    await PostgresLocalStorageQuarantineContract.RequireReadyAsync(global, enrollment))
                    .Throws<InvalidOperationException>();
                await Assert.That(rejected!.Message).IsEqualTo("local_storage_quarantine_authority_not_ready");
                var install = await Assert.That(async () =>
                    await PostgresLocalStorageQuarantineContract.ApplyAsync(migrator, enrollment))
                    .Throws<PostgresException>();
                await Assert.That(install!.MessageText).IsEqualTo("local_storage_quarantine_membership_invalid");
                bool retained = await migrator.Database.SqlQueryRaw<bool>(
                    """
                    SELECT CASE WHEN @change='missing' THEN NOT EXISTS (
                        SELECT 1 FROM pg_catalog.pg_auth_members e
                        WHERE e.member=pg_catalog.to_regrole(@login) AND e.roleid=pg_catalog.to_regrole(@capability))
                    WHEN @change='runtime_member' THEN pg_catalog.pg_has_role(@runtime,@capability,'USAGE')
                    ELSE EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members e
                        WHERE e.member=pg_catalog.to_regrole(@login) AND e.roleid=pg_catalog.to_regrole(@capability)
                            AND CASE WHEN @change='set' THEN e.set_option ELSE e.admin_option END) END AS "Value"
                    """, new NpgsqlParameter("change", change), new NpgsqlParameter("login", enrollment.GlobalLogin),
                    new NpgsqlParameter("capability", capability), new NpgsqlParameter("runtime", enrollment.RuntimeLogin)).SingleAsync();
                await Assert.That(retained).IsTrue();
            }
            finally
            {
                if (change == "runtime_member") await ExecuteAsync(connection, $"REVOKE {capability} FROM {runtime}");
                else await ExecuteAsync(connection, $"GRANT {capability} TO {login} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE");
            }
            await RequireStorageReadyAsync(global, enrollment);
        }
        string alternate = names.DelimitIdentifier($"prerequisite_alternate_{Guid.CreateVersion7():N}");
        await ExecuteAsync(connection, $"CREATE ROLE {alternate} NOLOGIN NOINHERIT");
        try
        {
            await ExecuteAsync(connection, $"GRANT {capability} TO {alternate} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE");
            await ExecuteAsync(connection, $"GRANT {alternate} TO {login} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE");
            var denied = await Assert.That(async () =>
                await PostgresLocalStorageQuarantineContract.RequireReadyAsync(global, enrollment))
                .Throws<InvalidOperationException>();
            await Assert.That(denied!.Message).IsEqualTo("local_storage_quarantine_authority_not_ready");
            var installed = await Assert.That(async () =>
                await PostgresLocalStorageQuarantineContract.ApplyAsync(migrator, enrollment)).Throws<PostgresException>();
            await Assert.That(installed!.MessageText).IsEqualTo("local_storage_quarantine_membership_invalid");
            bool retained = await migrator.Database.SqlQueryRaw<bool>(
                """SELECT pg_catalog.pg_has_role(@login,@alternate,'MEMBER') AND pg_catalog.pg_has_role(@alternate,@capability,'MEMBER') AS "Value" """,
                new NpgsqlParameter("login", enrollment.GlobalLogin), new NpgsqlParameter("alternate", alternate.Trim('"')),
                new NpgsqlParameter("capability", capability)).SingleAsync();
            await Assert.That(retained).IsTrue();
        }
        finally
        {
            await ExecuteAsync(connection, $"REVOKE {alternate} FROM {login}");
            await ExecuteAsync(connection, $"REVOKE {capability} FROM {alternate}");
            await ExecuteAsync(connection, $"DROP ROLE {alternate}");
        }
        await RequireStorageReadyAsync(global, enrollment);
    }

    private static void WritePhaseBestEffort(string message)
    {
        FreshEfProcessExecutor.WritePhaseBestEffort(message);
    }

    private static async Task AssertAuthenticationPermissionBoundariesAsync(
        ExploreDbContext migrator, ExploreDbContext global, PostgresNativeQueueEnrollment enrollment)
    {
        WritePhaseBestEffort("migration_auth_boundary phase=entry_readiness_start");
        await RequireStorageReadyAsync(global, enrollment);
        WritePhaseBestEffort("migration_auth_boundary phase=entry_readiness_complete");
        var names = migrator.GetService<ISqlGenerationHelper>();
        var connection = (NpgsqlConnection)migrator.Database.GetDbConnection();
        var apiKeyName = Mapping<Explore.Domain.ExternalApiKey>(migrator, nameof(Explore.Domain.ExternalApiKey.Name));
        var apiKeyId = Mapping<Explore.Domain.ExternalApiKey>(migrator, nameof(Explore.Domain.ExternalApiKey.KeyId));
        var scannerLabel = Mapping<Explore.Domain.AdmissionScannerCapability>(migrator, nameof(Explore.Domain.AdmissionScannerCapability.DeviceLabel));
        var scannerDigest = Mapping<Explore.Domain.AdmissionScannerCapability>(migrator, nameof(Explore.Domain.AdmissionScannerCapability.LookupDigest));
        foreach (var change in new[]
        {
            (Case: "api_name_update", Role: PostgresExternalApiKeyAuthenticationContract.GlobalRole, Source: apiKeyName, Privilege: "UPDATE", Add: true),
            (Case: "scanner_label_update", Role: PostgresAdmissionScannerAuthenticationContract.GlobalRole, Source: scannerLabel, Privilege: "UPDATE", Add: true),
            (Case: "api_key_id_select", Role: PostgresExternalApiKeyAuthenticationContract.GlobalRole, Source: apiKeyId, Privilege: "SELECT", Add: false),
            (Case: "scanner_digest_select", Role: PostgresAdmissionScannerAuthenticationContract.GlobalRole, Source: scannerDigest, Privilege: "SELECT", Add: false)
        })
        {
            async Task AssertDeclaredAclAsync()
            {
                bool exact = await migrator.Database.SqlQueryRaw<bool>(
                    """
                    SELECT EXISTS (
                        SELECT 1 FROM pg_catalog.pg_attribute a
                        JOIN pg_catalog.pg_class c ON c.oid=a.attrelid
                        WHERE a.attrelid=@relation::regclass AND a.attname=@column
                            AND a.attnum>0 AND NOT a.attisdropped
                            AND pg_catalog.has_column_privilege(@role,c.oid,a.attnum,@privilege)=@expected
                            AND EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl
                                WHERE acl.grantee=pg_catalog.to_regrole(@role)
                                    AND acl.privilege_type=@privilege
                                    AND acl.grantor=c.relowner AND NOT acl.is_grantable)=@expected
                            AND NOT EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl
                                WHERE acl.grantee=pg_catalog.to_regrole(@role)
                                    AND acl.privilege_type=@privilege
                                    AND (acl.grantor<>c.relowner OR acl.is_grantable))
                            AND NOT EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl
                                WHERE acl.grantee=0 AND acl.privilege_type=@privilege)) AS "Value"
                    """, new NpgsqlParameter("role", change.Role),
                    new NpgsqlParameter("relation", change.Source.Table),
                    new NpgsqlParameter("column", change.Source.Column.Trim('"')),
                    new NpgsqlParameter("privilege", change.Privilege),
                    new NpgsqlParameter("expected", !change.Add)).SingleAsync();
                await Assert.That(exact).IsTrue();
            }
            WritePhaseBestEffort($"migration_auth_boundary case={change.Case} phase=preimage_start");
            await AssertDeclaredAclAsync();
            WritePhaseBestEffort($"migration_auth_boundary case={change.Case} phase=preimage_complete");
            string role = names.DelimitIdentifier(change.Role);
            string permission = $"{change.Privilege} ({change.Source.Column}) ON {change.Source.Table}";
            await ExecuteAsync(connection, change.Add ? $"GRANT {permission} TO {role}" : $"REVOKE {permission} FROM {role}");
            try
            {
                WritePhaseBestEffort($"migration_auth_boundary case={change.Case} phase=tamper_applied");
                await RequireStorageRejectedAsync(global, enrollment);
                bool retained = await migrator.Database.SqlQueryRaw<bool>(
                    """SELECT pg_catalog.has_column_privilege(@role,@relation::regclass,@column,@privilege) = @expected AS "Value" """,
                    new NpgsqlParameter("role", change.Role), new NpgsqlParameter("relation", change.Source.Table),
                    new NpgsqlParameter("column", change.Source.Column.Trim('"')), new NpgsqlParameter("privilege", change.Privilege),
                    new NpgsqlParameter("expected", change.Add)).SingleAsync();
                await Assert.That(retained).IsTrue();
            }
            finally
            {
                await ExecuteAsync(connection, change.Add ? $"REVOKE {permission} FROM {role}" : $"GRANT {permission} TO {role}");
                WritePhaseBestEffort($"migration_auth_boundary case={change.Case} phase=restore_returned");
            }
            await AssertDeclaredAclAsync();
            WritePhaseBestEffort($"migration_auth_boundary case={change.Case} phase=restored_acl_complete");
            await RequireStorageReadyAsync(global, enrollment);
            WritePhaseBestEffort($"migration_auth_boundary case={change.Case} phase=restored_readiness_complete");
        }
    }

    private static async Task AssertMixedPermissionBoundariesAsync(
        ExploreDbContext migrator, ExploreDbContext global, PostgresNativeQueueEnrollment enrollment,
        PrimaryDatabaseConnectionOptions selectedOptions)
    {
        WritePhaseBestEffort("migration_mixed_boundary phase=entry");
        var names = migrator.GetService<ISqlGenerationHelper>();
        string login = names.DelimitIdentifier(enrollment.GlobalLogin);
        var source = Mapping<Explore.Domain.OutboxMessage>(migrator, nameof(Explore.Domain.OutboxMessage.Id));
        var extra = Mapping<Explore.Domain.OutboxMessage>(migrator, nameof(Explore.Domain.OutboxMessage.Status));
        var connection = (NpgsqlConnection)migrator.Database.GetDbConnection();
        using var observationStop = new CancellationTokenSource();
        Task observation = ObserveMixedHelperAsync(migrator, global, enrollment, selectedOptions, observationStop.Token);
        Exception? primaryFailure = null;
        bool observationCleanupFailed = false;
        try
        {
            await PostgresMixedSettingsContract.RequireReadyAsync(migrator, enrollment);
            await PostgresOperatorIdentityEditorContract.RequireReadyAsync(migrator, enrollment);
            WritePhaseBestEffort("migration_mixed_boundary phase=baseline_ready");
            string renamed = $"prerequisite_missing_{Guid.CreateVersion7():N}";
            string renamedTable = names.DelimitIdentifier(renamed,
                migrator.Model.FindEntityType(typeof(Explore.Domain.OutboxMessage))!.GetSchema());
            await migrator.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using (var missingSource = await migrator.Database.BeginTransactionAsync())
                {
                    await migrator.Database.ExecuteSqlRawAsync($"ALTER TABLE {source.Table} RENAME TO {names.DelimitIdentifier(renamed)}");
                    await missingSource.CreateSavepointAsync("missing_source_admission");
                    try
                    {
                        var missing = await Assert.That(async () =>
                            await PostgresMixedSettingsContract.RequireReadyAsync(migrator, enrollment)).Throws<PostgresException>();
                        await Assert.That(missing!.SqlState).IsEqualTo("55000");
                        await Assert.That(missing.MessageText).IsEqualTo("mixed_settings_source_catalog_drift");
                        await missingSource.RollbackToSavepointAsync("missing_source_admission");
                        bool retained = await migrator.Database.SqlQueryRaw<bool>(
                            """SELECT pg_catalog.to_regclass(@original) IS NULL AND pg_catalog.to_regclass(@renamed) IS NOT NULL AS "Value" """,
                            new NpgsqlParameter("original", source.Table), new NpgsqlParameter("renamed", renamedTable)).SingleAsync();
                        await Assert.That(retained).IsTrue();
                        WritePhaseBestEffort("migration_mixed_boundary phase=missing_source_retained");
                    }
                    finally { await missingSource.RollbackAsync(); }
                }
            });
            await PostgresMixedSettingsContract.RequireReadyAsync(migrator, enrollment);
            WritePhaseBestEffort("migration_mixed_boundary phase=missing_source_restored");
            foreach (string change in new[] { "extra", "missing", "public", "grant_option", "wrong_issuer" })
            {
                string issuer = names.DelimitIdentifier($"prerequisite_issuer_{Guid.CreateVersion7():N}");
                switch (change)
                {
                    case "extra": await ExecuteAsync(connection, $"GRANT UPDATE ({extra.Column}) ON {source.Table} TO {login}"); break;
                    case "missing": await ExecuteAsync(connection, $"REVOKE INSERT ({source.Column}) ON {source.Table} FROM {login}"); break;
                    case "public": await ExecuteAsync(connection, $"GRANT INSERT ({source.Column}) ON {source.Table} TO PUBLIC"); break;
                    case "grant_option": await ExecuteAsync(connection, $"GRANT INSERT ({source.Column}) ON {source.Table} TO {login} WITH GRANT OPTION"); break;
                    case "wrong_issuer":
                        await ExecuteAsync(connection, $"CREATE ROLE {issuer} NOLOGIN");
                        await ExecuteAsync(connection, $"GRANT USAGE ON SCHEMA {names.DelimitIdentifier(migrator.Model.FindEntityType(typeof(Explore.Domain.OutboxMessage))!.GetSchema()!)} TO {issuer}");
                        await ExecuteAsync(connection, $"REVOKE INSERT ({source.Column}) ON {source.Table} FROM {login}");
                        await ExecuteAsync(connection, $"GRANT INSERT ({source.Column}) ON {source.Table} TO {issuer} WITH GRANT OPTION");
                        await ExecuteAsync(connection, $"SET ROLE {issuer}");
                        try { await ExecuteAsync(connection, $"GRANT INSERT ({source.Column}) ON {source.Table} TO {login}"); }
                        finally { await ExecuteAsync(connection, "RESET ROLE"); }
                        break;
                }
                try
                {
                    var mixed = await Assert.That(async () => await PostgresMixedSettingsContract.RequireReadyAsync(migrator, enrollment))
                        .Throws<PostgresException>();
                    await Assert.That(mixed!.SqlState).IsEqualTo("55000");
                    await Assert.That(mixed.MessageText).IsEqualTo(change == "public"
                        ? "native_privilege_guard_not_ready" : "mixed_settings_ambient_scalar_authority");
                    var editor = await Assert.That(async () => await PostgresOperatorIdentityEditorContract.RequireReadyAsync(migrator, enrollment))
                        .Throws<PostgresException>();
                    await Assert.That(editor!.SqlState).IsEqualTo("55000");
                    await Assert.That(editor.MessageText).IsEqualTo(change == "public"
                        ? "native_privilege_guard_not_ready" : "operator_editor_ambient_write_authority");
                    await RequireStorageRejectedAsync(global, enrollment);
                    bool retained = await migrator.Database.SqlQueryRaw<bool>(
                        """
                        SELECT CASE @change
                            WHEN 'missing' THEN NOT pg_catalog.has_column_privilege(@login,@relation::regclass,@column,'INSERT')
                            ELSE EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                                CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                                WHERE a.attrelid=@relation::regclass AND a.attname=CASE WHEN @change='extra' THEN @extra ELSE @column END
                                    AND acl.privilege_type=CASE WHEN @change='extra' THEN 'UPDATE' ELSE 'INSERT' END
                                    AND CASE @change WHEN 'public' THEN acl.grantee=0
                                        WHEN 'grant_option' THEN acl.grantee=pg_catalog.to_regrole(@login) AND acl.is_grantable
                                        WHEN 'wrong_issuer' THEN acl.grantee=pg_catalog.to_regrole(@login) AND acl.grantor=pg_catalog.to_regrole(@issuer)
                                        ELSE acl.grantee=pg_catalog.to_regrole(@login) END) END AS "Value"
                        """, new NpgsqlParameter("change", change), new NpgsqlParameter("login", enrollment.GlobalLogin),
                        new NpgsqlParameter("relation", source.Table), new NpgsqlParameter("column", source.Column.Trim('"')),
                        new NpgsqlParameter("extra", extra.Column.Trim('"')), new NpgsqlParameter("issuer", issuer.Trim('"'))).SingleAsync();
                    await Assert.That(retained).IsTrue();
                }
                finally
                {
                    switch (change)
                    {
                        case "extra": await ExecuteAsync(connection, $"REVOKE UPDATE ({extra.Column}) ON {source.Table} FROM {login}"); break;
                        case "public": await ExecuteAsync(connection, $"REVOKE INSERT ({source.Column}) ON {source.Table} FROM PUBLIC"); break;
                        case "grant_option": await ExecuteAsync(connection, $"REVOKE GRANT OPTION FOR INSERT ({source.Column}) ON {source.Table} FROM {login}"); break;
                        case "wrong_issuer":
                            await ExecuteAsync(connection, $"REVOKE INSERT ({source.Column}) ON {source.Table} FROM {issuer} CASCADE");
                            await ExecuteAsync(connection, $"REVOKE USAGE ON SCHEMA {names.DelimitIdentifier(migrator.Model.FindEntityType(typeof(Explore.Domain.OutboxMessage))!.GetSchema()!)} FROM {issuer}");
                            await ExecuteAsync(connection, $"DROP ROLE {issuer}");
                            goto case "missing";
                        case "missing": await ExecuteAsync(connection, $"GRANT INSERT ({source.Column}) ON {source.Table} TO {login}"); break;
                    }
                }
                await PostgresMixedSettingsContract.RequireReadyAsync(migrator, enrollment);
                await PostgresOperatorIdentityEditorContract.RequireReadyAsync(migrator, enrollment);
                await RequireStorageReadyAsync(global, enrollment);
                WritePhaseBestEffort($"migration_mixed_boundary case={change} phase=restored");
            }
            WritePhaseBestEffort("migration_mixed_boundary phase=complete");
        }
        catch (Exception error) { primaryFailure = error; }
        finally
        {
            try { await observationStop.CancelAsync(); }
            catch { observationCleanupFailed = true; }
            try { await observation; }
            catch { observationCleanupFailed = true; }
        }
        if (observationCleanupFailed)
        {
            var cleanupFailure = new InvalidOperationException("mixed_wait_observer_cleanup_failed");
            if (primaryFailure is not null) throw new AggregateException(primaryFailure, cleanupFailure);
            throw cleanupFailure;
        }
        if (primaryFailure is not null) ExceptionDispatchInfo.Capture(primaryFailure).Throw();
    }

    internal static string DescribeMixedWait(bool visible, bool? active, bool? lockWait, int[]? blockers, int ownedGlobalBackend)
    {
        const string inconclusive = "migration_mixed_wait phase=mixed_helper_scope outcome=inconclusive";
        if (!visible || active is null || lockWait is null || blockers is null || ownedGlobalBackend <= 0
            || blockers.Any(blocker => blocker != ownedGlobalBackend)) return inconclusive;
        return $"migration_mixed_wait phase=mixed_helper_scope active={(active.Value ? "true" : "false")} lock_wait={(lockWait.Value ? "true" : "false")} owned_blocker={(blockers.Length != 0 ? "true" : "false")}";
    }

    private static async Task ObserveMixedHelperAsync(
        ExploreDbContext migrator, ExploreDbContext global, PostgresNativeQueueEnrollment enrollment,
        PrimaryDatabaseConnectionOptions selectedOptions, CancellationToken caseStopped)
    {
        const string inconclusive = "migration_mixed_wait phase=mixed_helper_scope outcome=inconclusive";
        string result = inconclusive;
        NpgsqlConnection? observer = null;
        NpgsqlCommand? command = null;
        NpgsqlDataReader? reader = null;
        bool cleanupFailed = false;
        try
        {
            // Capture caller-owned connection facts before yielding; the observer never uses either context concurrently.
            var target = (NpgsqlConnection)migrator.Database.GetDbConnection();
            var ownedGlobal = (NpgsqlConnection)global.Database.GetDbConnection();
            if (selectedOptions.Role != PrimaryDatabaseRole.Migrator || selectedOptions.Provider != PrimaryDatabaseProvider.PostgreSql
                || target.State != ConnectionState.Open || ownedGlobal.State != ConnectionState.Open
                || migrator.Database.CurrentTransaction is not null || global.Database.CurrentTransaction is not null
                || System.Transactions.Transaction.Current is not null)
            {
                WritePhaseBestEffort(result);
                return;
            }
            var declared = new NpgsqlConnectionStringBuilder(PrimaryDatabaseConfiguration.BuildConnectionString(selectedOptions).ConnectionString);
            var original = new NpgsqlConnectionStringBuilder(target.ConnectionString);
            var originalGlobal = new NpgsqlConnectionStringBuilder(ownedGlobal.ConnectionString);
            int targetBackend = target.ProcessID;
            int globalBackend = ownedGlobal.ProcessID;
            if (targetBackend <= 0 || globalBackend <= 0 || targetBackend == globalBackend
                || declared.Host != original.Host || declared.Port != original.Port || declared.Database != original.Database
                || declared.Username != original.Username || declared.SslMode != original.SslMode
                || declared.Username != enrollment.MigratorLogin || originalGlobal.Username != enrollment.GlobalLogin
                || declared.Host != originalGlobal.Host || declared.Port != originalGlobal.Port
                || declared.Database != originalGlobal.Database || declared.SslMode != originalGlobal.SslMode)
            {
                WritePhaseBestEffort(result);
                return;
            }
            declared.Pooling = false;
            await Task.Delay(TimeSpan.FromMilliseconds(250), caseStopped);
            using var observationDeadline = CancellationTokenSource.CreateLinkedTokenSource(caseStopped);
            observationDeadline.CancelAfter(TimeSpan.FromSeconds(2));
            CancellationToken token = observationDeadline.Token;
            observer = new NpgsqlConnection(declared.ConnectionString);
            await observer.OpenAsync(token);
            command = new NpgsqlCommand(
                """
                SELECT current_user = @login AND session_user = @login
                        AND a.state IN ('active','idle','idle in transaction','idle in transaction (aborted)','fastpath function call'),
                    a.state = 'active', COALESCE(a.wait_event_type = 'Lock', false),
                    pg_catalog.pg_blocking_pids(a.pid)
                FROM pg_catalog.pg_stat_activity a
                WHERE a.pid = @target AND a.datname = @database AND a.usename = @login
                    AND a.backend_type = 'client backend'
                """, observer) { CommandTimeout = 1 };
            command.Parameters.Add(new NpgsqlParameter("login", NpgsqlDbType.Text) { Value = declared.Username! });
            command.Parameters.Add(new NpgsqlParameter("database", NpgsqlDbType.Text) { Value = declared.Database! });
            command.Parameters.Add(new NpgsqlParameter("target", NpgsqlDbType.Integer) { Value = targetBackend });
            reader = await command.ExecuteReaderAsync(token);
            if (await reader.ReadAsync(token))
            {
                result = DescribeMixedWait(!reader.IsDBNull(0) && reader.GetBoolean(0),
                    reader.IsDBNull(1) ? null : reader.GetBoolean(1), reader.IsDBNull(2) ? null : reader.GetBoolean(2),
                    reader.IsDBNull(3) ? null : reader.GetFieldValue<int[]>(3), globalBackend);
                if (await reader.ReadAsync(token)) result = inconclusive;
            }
        }
        catch (OperationCanceledException) when (caseStopped.IsCancellationRequested)
        {
            result = "migration_mixed_wait phase=mixed_helper_scope outcome=not_observed";
        }
        catch { result = inconclusive; }
        finally
        {
            if (reader is not null)
            {
                try { await reader.DisposeAsync(); }
                catch { cleanupFailed = true; }
            }
            if (command is not null)
            {
                try { await command.DisposeAsync(); }
                catch { cleanupFailed = true; }
            }
            if (observer is not null)
            {
                try { await observer.DisposeAsync(); }
                catch { cleanupFailed = true; }
            }
        }
        // Emit only after disposal. This phase names the arming scope, not a synchronized operation cursor.
        if (caseStopped.IsCancellationRequested) result = "migration_mixed_wait phase=mixed_helper_scope outcome=not_observed";
        if (cleanupFailed) result = inconclusive;
        WritePhaseBestEffort(result);
        if (cleanupFailed) throw new InvalidOperationException("mixed_wait_observer_cleanup_failed");
    }

    private static async Task AssertIsolatedGlobalCatalogueAsync(
        ExploreDbContext migrator, PostgresNativeQueueEnrollment enrollment)
    {
        var names = migrator.GetService<ISqlGenerationHelper>();
        string isolatedName = $"prerequisite_zero_{Guid.CreateVersion7():N}";
        string parentName = $"prerequisite_zero_parent_{Guid.CreateVersion7():N}";
        string isolated = names.DelimitIdentifier(isolatedName);
        string parent = names.DelimitIdentifier(parentName);
        var connection = (NpgsqlConnection)migrator.Database.GetDbConnection();
        var inside = Mapping<Explore.Domain.OutboxMessage>(migrator, nameof(Explore.Domain.OutboxMessage.Status));
        var outside = Mapping<Explore.Domain.Category>(migrator, nameof(Explore.Domain.Category.FullName));
        string predicate = PostgresManagedTenantWorkerCreationPermissions.GlobalApplicationCataloguePredicate(
            migrator, enrollment with { GlobalLogin = isolatedName });
        async Task<bool> ReadyAsync() => await migrator.Database.SqlQueryRaw<bool>(
            $"""SELECT ({predicate}) AS "Value" """).SingleAsync();
        await ExecuteAsync(connection, $"CREATE ROLE {isolated} LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS");
        await ExecuteAsync(connection, $"CREATE ROLE {parent} NOLOGIN NOINHERIT");
        try
        {
            await Assert.That(await ReadyAsync()).IsTrue();
            await ExecuteAsync(connection, $"GRANT UPDATE ({inside.Column}) ON {inside.Table} TO {isolated}");
            try { await Assert.That(await ReadyAsync()).IsFalse(); }
            finally { await ExecuteAsync(connection, $"REVOKE UPDATE ({inside.Column}) ON {inside.Table} FROM {isolated}"); }
            await Assert.That(await ReadyAsync()).IsTrue();
            await ExecuteAsync(connection, $"GRANT SELECT ({outside.Column}) ON {outside.Table} TO {parent}");
            await ExecuteAsync(connection, $"GRANT {parent} TO {isolated} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE");
            await Assert.That(await ReadyAsync()).IsFalse();
            await ExecuteAsync(connection, $"REVOKE {parent} FROM {isolated}");
            await ExecuteAsync(connection, $"REVOKE SELECT ({outside.Column}) ON {outside.Table} FROM {parent}");
            await Assert.That(await ReadyAsync()).IsTrue();
        }
        finally
        {
            await ExecuteAsync(connection, $"REVOKE {parent} FROM {isolated}");
            await ExecuteAsync(connection, $"REVOKE SELECT ({outside.Column}) ON {outside.Table} FROM {parent}");
            await ExecuteAsync(connection, $"DROP ROLE {parent}");
            await ExecuteAsync(connection, $"DROP ROLE {isolated}");
        }
    }

    private static async Task RequireRevisionPolicyAsync(ExploreDbContext context)
    {
        bool protectedSource = await context.Database.SqlQueryRaw<bool>(
            """
            SELECT EXISTS (
                SELECT 1 FROM pg_catalog.pg_class c
                JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
                JOIN pg_catalog.pg_policy p ON p.polrelid = c.oid
                WHERE n.nspname = 'migration_target' AND c.relname = 'event_discovery_revisions'
                    AND c.relrowsecurity AND c.relforcerowsecurity
                    AND pg_catalog.obj_description(p.oid, 'pg_policy') LIKE 'event-tenant-policy:v2:%'
            ) AS "Value"
            """).SingleAsync();
        await Assert.That(protectedSource).IsTrue();
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
