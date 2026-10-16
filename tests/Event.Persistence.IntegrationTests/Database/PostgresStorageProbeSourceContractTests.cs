using System.Data;
using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Secrets;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using Testcontainers.PostgreSql;

namespace Event.Persistence.IntegrationTests.Database;

/// <summary>Exercises the original source contract directly, without the managed provider session.</summary>
[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class PostgresStorageProbeSourceContractTests(PostgreSqlContainerFixture fixture)
{
    private static readonly Type[] Sources =
        [typeof(SystemSetting), typeof(TenantSetting), typeof(SecretBinding), typeof(InstanceBootstrapState)];

    [Test]
    public async Task InstallationAndReinstallationPreserveSourcesAndOriginalReadWriteAuthority()
    {
        await using var database = await ProbeDatabase.CreateAsync(fixture, install: false);
        foreach (Type source in Sources) await database.InsertAsync(database.Migration, source);
        // An original source reader remains a reader. The fence must neither replace
        // this authority nor broaden it to another source, column or write.
        await ProbeDatabase.ExecuteAsync(database.Migration, $"""
            GRANT SELECT ({database.Sql.Column(typeof(SystemSetting), "Id")})
                ON {database.Sql.Table(typeof(SystemSetting))}
                TO {database.Sql.Quote(fixture.NativeQueueEnrollment.RuntimeLogin)};
            """);
        string rows = await database.RowsAsync();
        string privileges = await database.OriginalPrivilegesAsync();

        await database.InstallAsync();
        await database.InstallAsync();
        await PostgresStorageProbeSourceContract.RequireReadyAsync(database.Migration, fixture.NativeQueueEnrollment);

        await Assert.That(await database.RowsAsync()).IsEqualTo(rows);
        await Assert.That(await database.OriginalPrivilegesAsync()).IsEqualTo(privileges);
        foreach (PrimaryDatabaseRole role in new[] { PrimaryDatabaseRole.Runtime, PrimaryDatabaseRole.Global })
        {
            await using var caller = await database.OpenAsync(role);
            Guid? tenant = role == PrimaryDatabaseRole.Runtime ? database.Tenant : null;
            await database.SetTenantAsync(caller, tenant);
            await PostgresStorageProbeSourceContract.RequireReadyAsync(caller, fixture.NativeQueueEnrollment);
            await Assert.That(await database.ReadAsync(caller, tenant)).IsEqualTo(0L);
            await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await Assert.That(await database.ReadAsync(caller, tenant, acquire: true)).IsEqualTo(0L);
            await Assert.That(await ProbeDatabase.ScalarAsync<string>(caller,
                "SELECT session_user::text || ':' || current_setting('app.current_tenant_id')"))
                .IsEqualTo(fixture.GetNativeQueueOptions(role).Username! + ":" + (tenant?.ToString("D") ?? ""));
            await transaction.RollbackAsync();
        }
    }

    [Test]
    [Arguments(typeof(SystemSetting), PrimaryDatabaseRole.Runtime)]
    [Arguments(typeof(TenantSetting), PrimaryDatabaseRole.Runtime)]
    [Arguments(typeof(SecretBinding), PrimaryDatabaseRole.Runtime)]
    [Arguments(typeof(InstanceBootstrapState), PrimaryDatabaseRole.Runtime)]
    [Arguments(typeof(SystemSetting), PrimaryDatabaseRole.Global)]
    [Arguments(typeof(TenantSetting), PrimaryDatabaseRole.Global)]
    [Arguments(typeof(SecretBinding), PrimaryDatabaseRole.Global)]
    [Arguments(typeof(InstanceBootstrapState), PrimaryDatabaseRole.Global)]
    public async Task PrivateSentinelExcludesEverySourceStatementAndRollbackReleasesIt(Type source, PrimaryDatabaseRole role)
    {
        await using var database = await ProbeDatabase.CreateAsync(fixture);
        await using var caller = await database.OpenAsync(role);
        Guid? tenant = role == PrimaryDatabaseRole.Runtime ? database.Tenant : null;
        await database.SetTenantAsync(caller, tenant);
        await using var writer = await database.OpenAsync(PrimaryDatabaseRole.Migrator);
        foreach (string operation in new[] { "insert", "metadata", "rename", "replacement", "delete", "truncate" })
        {
            if (operation == "truncate") await database.InsertAsync(writer, source);
            long generation = await database.ReadAsync(caller, tenant);
            string before = await database.RowsAsync();
            await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await Assert.That(await database.ReadAsync(caller, tenant, acquire: true)).IsEqualTo(generation);
            // Acquisition has completed on a different, still-open backend. This is
            // a known-held exclusion, not a scheduling wait or an advisory lock.
            await ProbeDatabase.ExecuteAsync(writer, "SET lock_timeout = '250ms'");
            var blocked = await Assert.ThrowsAsync<PostgresException>(() => database.WriteAsync(writer, source, operation));
            await Assert.That(blocked!.SqlState).IsEqualTo("55P03");
            await Assert.That(await database.RowsAsync()).IsEqualTo(before);

            await transaction.RollbackAsync();
            await database.WriteAsync(writer, source, operation);
            await Assert.That(await database.ReadAsync(caller, tenant) > generation).IsTrue();
        }
    }

    [Test]
    [Arguments(typeof(SystemSetting), "insert")]
    [Arguments(typeof(SystemSetting), "metadata")]
    [Arguments(typeof(SystemSetting), "rename")]
    [Arguments(typeof(SystemSetting), "replacement")]
    [Arguments(typeof(TenantSetting), "insert")]
    [Arguments(typeof(TenantSetting), "metadata")]
    [Arguments(typeof(SecretBinding), "insert")]
    [Arguments(typeof(SecretBinding), "metadata")]
    [Arguments(typeof(SecretBinding), "rename")]
    [Arguments(typeof(InstanceBootstrapState), "insert")]
    public async Task CommittedAbsentUnrelatedAndMetadataWritesInvalidateOriginalSnapshot(Type source, string operation)
    {
        await using var database = await ProbeDatabase.CreateAsync(fixture);
        if (operation != "insert") await database.InsertAsync(database.Migration, source);
        await using var caller = await database.OpenAsync(PrimaryDatabaseRole.Global);
        await using var writer = await database.OpenAsync(PrimaryDatabaseRole.Migrator);
        await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await database.ReadAsync(caller, null);

        // No storage-key predicate: inserted keys are unrelated and bootstrap starts
        // genuinely absent. Metadata-only changes and renames must still participate.
        await database.WriteAsync(writer, source, operation);

        var error = await Assert.ThrowsAsync<PostgresException>(() => database.ReadAsync(caller, null, acquire: true));
        await Assert.That(error!.SqlState).IsEqualTo("40001");
        await transaction.RollbackAsync();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SourceGenerationFollowsPhysicalCommitRatherThanAttemptedWrites(bool commit)
    {
        await using var database = await ProbeDatabase.CreateAsync(fixture);
        await using var caller = await database.OpenAsync(PrimaryDatabaseRole.Global);
        await using var writer = await database.OpenAsync(PrimaryDatabaseRole.Migrator);
        await using (var transaction = await writer.Database.BeginTransactionAsync())
        {
            await database.InsertAsync(writer, typeof(SystemSetting));
            await Assert.That(await ProbeDatabase.ScalarAsync<long>(writer,
                $"SELECT revision FROM {database.Anchor} WHERE id = 1")).IsEqualTo(1L);
            await Assert.That(await database.ReadAsync(caller, null)).IsEqualTo(0L);
            if (commit) await transaction.CommitAsync();
            else await transaction.RollbackAsync();
        }
        await Assert.That(await database.ReadAsync(caller, null)).IsEqualTo(commit ? 1L : 0L);
    }

    [Test]
    public async Task StatementExclusionIncludesZeroMatchesAndTruncateAndRollbackRestoresGeneration()
    {
        await using var database = await ProbeDatabase.CreateAsync(fixture);
        await using var caller = await database.OpenAsync(PrimaryDatabaseRole.Global);
        await using var writer = await database.OpenAsync(PrimaryDatabaseRole.Migrator);
        foreach (Type source in Sources)
        {
            await using var transaction = await writer.Database.BeginTransactionAsync();
            string id = database.Sql.Column(source, "Id");
            await ProbeDatabase.ExecuteAsync(writer, $"UPDATE {database.Sql.Table(source)} SET {id} = {id} WHERE false");
            await ProbeDatabase.ExecuteAsync(writer, $"DELETE FROM {database.Sql.Table(source)} WHERE false");
            await ProbeDatabase.ExecuteAsync(writer, $"TRUNCATE {database.Sql.Table(source)}");
            await Assert.That(await ProbeDatabase.ScalarAsync<long>(writer,
                $"SELECT revision FROM {database.Anchor} WHERE id = 1")).IsEqualTo(3L);
            await transaction.RollbackAsync();
            await Assert.That(await database.ReadAsync(caller, null)).IsEqualTo(0L);
        }
    }

    [Test]
    public async Task RuntimeAndGlobalReceiveOnlyExecuteAuthorityNotPrivateSentinelOrSourceAccess()
    {
        await using var database = await ProbeDatabase.CreateAsync(fixture);
        foreach (PrimaryDatabaseRole role in new[] { PrimaryDatabaseRole.Runtime, PrimaryDatabaseRole.Global })
        {
            await using var caller = await database.OpenAsync(role);
            Guid? tenant = role == PrimaryDatabaseRole.Runtime ? database.Tenant : null;
            await database.SetTenantAsync(caller, tenant);
            await Assert.That(await database.ReadAsync(caller, tenant)).IsEqualTo(0L);
            foreach (string table in Sources.Select(database.Sql.Table).Append(database.Anchor))
            {
                var read = await Assert.ThrowsAsync<PostgresException>(() => ProbeDatabase.ExecuteAsync(caller,
                    $"SELECT id FROM {table}"));
                await Assert.That(read!.SqlState).IsEqualTo("42501");
                var write = await Assert.ThrowsAsync<PostgresException>(() => ProbeDatabase.ExecuteAsync(caller,
                    $"DELETE FROM {table} WHERE false"));
                await Assert.That(write!.SqlState).IsEqualTo("42501");
            }
        }
    }

    [Test]
    public async Task WrongOriginalLoginTenantIsolationAndSessionRoleCannotBorrowCustody()
    {
        await using var database = await ProbeDatabase.CreateAsync(fixture);
        foreach (var scenario in new[]
        {
            (PrimaryDatabaseRole.Runtime, (Guid?)null, (Guid?)null, IsolationLevel.Serializable),
            (PrimaryDatabaseRole.Runtime, (Guid?)Guid.Empty, (Guid?)Guid.Empty, IsolationLevel.Serializable),
            (PrimaryDatabaseRole.Runtime, (Guid?)database.Tenant, (Guid?)Guid.CreateVersion7(), IsolationLevel.Serializable),
            (PrimaryDatabaseRole.Global, (Guid?)database.Tenant, (Guid?)database.Tenant, IsolationLevel.Serializable),
            (PrimaryDatabaseRole.Runtime, (Guid?)database.Tenant, (Guid?)database.Tenant, IsolationLevel.ReadCommitted),
            (PrimaryDatabaseRole.Global, (Guid?)null, (Guid?)null, IsolationLevel.RepeatableRead),
            (PrimaryDatabaseRole.EmailPolicyInstanceMutation, (Guid?)null, (Guid?)null, IsolationLevel.Serializable),
            (PrimaryDatabaseRole.EmailPolicyTenantMutation, (Guid?)database.Tenant, (Guid?)database.Tenant, IsolationLevel.Serializable),
            (PrimaryDatabaseRole.Migrator, (Guid?)null, (Guid?)null, IsolationLevel.Serializable)
        })
        {
            await using var caller = await database.OpenAsync(scenario.Item1);
            await database.SetTenantAsync(caller, scenario.Item3);
            foreach (bool acquire in scenario.Item4 == IsolationLevel.Serializable ? new[] { false, true } : [true])
            {
                await using var transaction = await caller.Database.BeginTransactionAsync(scenario.Item4);
                var error = await Assert.ThrowsAsync<PostgresException>(() =>
                    database.ReadAsync(caller, scenario.Item2, acquire));
                await Assert.That(error!.SqlState).IsEqualTo("42501");
                await transaction.RollbackAsync();
            }
        }
        // SET ROLE cannot turn the migrator's original login into the enrolled
        // ordinary Global session, even though it can execute Global's routine.
        await using var switched = await database.OpenAsync(PrimaryDatabaseRole.Migrator);
        await using var switchedTransaction = await switched.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await ProbeDatabase.ExecuteAsync(switched,
            $"SET LOCAL ROLE {database.Sql.Quote(fixture.NativeQueueEnrollment.GlobalLogin)}");
        var switchedError = await Assert.ThrowsAsync<PostgresException>(() => database.ReadAsync(switched, null, acquire: true));
        await Assert.That(switchedError!.SqlState).IsEqualTo("42501");
        await switchedTransaction.RollbackAsync();
    }

    [Test]
    [Arguments("body")]
    [Arguments("body-and-stamp")]
    [Arguments("trigger")]
    [Arguments("trigger-event")]
    [Arguments("role")]
    [Arguments("membership")]
    [Arguments("routine-acl")]
    [Arguments("source-acl")]
    [Arguments("anchor-acl")]
    [Arguments("search-path")]
    [Arguments("column")]
    [Arguments("collation")]
    [Arguments("key")]
    [Arguments("owner")]
    [Arguments("rls")]
    [Arguments("policy")]
    [Arguments("extra-policy")]
    [Arguments("missing-anchor")]
    public async Task IndependentReadinessAndReinstallRefuseExactDriftWithoutRepair(string drift)
    {
        await using var database = await ProbeDatabase.CreateAsync(fixture);
        string observe = database.Function("observe");
        string system = database.Sql.Table(typeof(SystemSetting));
        string tenant = database.Sql.Table(typeof(TenantSetting));
        string change = drift switch
        {
            "body" or "body-and-stamp" => $"""
                CREATE OR REPLACE FUNCTION {observe}(p_tenant uuid) RETURNS bigint
                    LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                    AS 'BEGIN RETURN 0; END;';
                """,
            "trigger" => $"ALTER TABLE {system} DISABLE TRIGGER storage_probe_source_changed",
            "trigger-event" => $"""
                DROP TRIGGER storage_probe_source_changed ON {system};
                CREATE TRIGGER storage_probe_source_changed BEFORE UPDATE ON {system}
                    FOR EACH STATEMENT EXECUTE FUNCTION {database.Function("changed")}();
                ALTER TABLE {system} ENABLE ALWAYS TRIGGER storage_probe_source_changed;
                """,
            "role" => $"ALTER ROLE {PostgresStorageProbeSourceContract.OwnerRole} LOGIN",
            "membership" => $"""
                GRANT {PostgresStorageProbeSourceContract.GlobalRole}
                    TO {database.Sql.Quote(fixture.NativeQueueEnrollment.GlobalLogin)}
                    WITH INHERIT TRUE, SET TRUE, ADMIN FALSE
                """,
            "routine-acl" => $"GRANT EXECUTE ON FUNCTION {observe}(uuid) TO PUBLIC",
            "source-acl" => $"GRANT SELECT ON {system} TO {PostgresStorageProbeSourceContract.OwnerRole}",
            "anchor-acl" => $"""
                GRANT SELECT ON {database.Anchor} TO {database.Sql.Quote(fixture.NativeQueueEnrollment.RuntimeLogin)}
                """,
            "search-path" => $"ALTER FUNCTION {observe}(uuid) SET search_path = public",
            "column" => $"ALTER TABLE {system} ALTER COLUMN description TYPE text",
            "collation" => $"""
                ALTER TABLE {database.Sql.Table(typeof(InstanceBootstrapState))}
                    ALTER COLUMN completed_identity_fingerprint TYPE character(64) COLLATE "default"
                """,
            "key" => $"""
                DROP INDEX {database.Sql.Name(database.Migration.Model.FindEntityType(typeof(SystemSetting))!
                    .GetIndexes().Single(index => index.IsUnique).GetDatabaseName()!)}
                """,
            "owner" => $"ALTER TABLE {system} OWNER TO {database.Sql.Quote(fixture.NativeQueueEnrollment.RuntimeLogin)}",
            "rls" => $"ALTER TABLE {tenant} NO FORCE ROW LEVEL SECURITY",
            "policy" => $"""
                ALTER POLICY {database.Sql.Quote(PostgresTenantRowLevelSecurityModel.PolicyName)}
                    ON {tenant} USING (true) WITH CHECK (true)
                """,
            "extra-policy" => $"CREATE POLICY probe_extra ON {tenant} USING (true) WITH CHECK (true)",
            "missing-anchor" => $"DELETE FROM {database.Anchor} WHERE id = 1",
            _ => throw new ArgumentOutOfRangeException(nameof(drift))
        };
        await ProbeDatabase.ExecuteAsync(database.Migration, change);
        if (drift == "body-and-stamp")
            await ProbeDatabase.ExecuteAsync(database.Migration, $"""
                DO $forge$ DECLARE stamp text; BEGIN
                    SELECT 'storage-probe-source:v1:' ||
                        encode(sha256(convert_to(prosrc,'UTF8')),'hex') INTO stamp
                    FROM pg_proc WHERE oid = '{observe}(uuid)'::regprocedure;
                    EXECUTE format('COMMENT ON FUNCTION {observe}(uuid) IS %L',stamp);
                END $forge$;
                """);
        string before = await database.CatalogAsync();
        var ready = await Assert.ThrowsAsync<PostgresException>(() =>
            PostgresStorageProbeSourceContract.RequireReadyAsync(database.Migration, fixture.NativeQueueEnrollment));
        await Assert.That(ready!.SqlState).IsEqualTo("55000");
        if (drift == "body-and-stamp")
            await Assert.That(ready.MessageText).IsEqualTo("storage_probe_source_body_drift");
        await Assert.That(await database.CatalogAsync()).IsEqualTo(before);
        // Exact expected bodies are independently admitted by RequireReadyAsync,
        // not trusted from a routine's self-authored comment. Do not ask a replaced
        // routine to attest itself; structural drift is also rejected at raw entry.
        if (drift is not ("body" or "body-and-stamp"))
        {
            await using var caller = await database.OpenAsync(PrimaryDatabaseRole.Global);
            foreach (bool acquire in new[] { false, true })
            {
                await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                var error = await Assert.ThrowsAsync<PostgresException>(() => database.ReadAsync(caller, null, acquire));
                await Assert.That(error!.SqlState).IsEqualTo("55000");
                await transaction.RollbackAsync();
            }
        }
        var reinstall = await Assert.ThrowsAsync<PostgresException>(database.InstallAsync);
        await Assert.That(reinstall!.SqlState).IsEqualTo("55000");
        if (drift == "body-and-stamp")
            await Assert.That(reinstall.MessageText).IsEqualTo("storage_probe_source_body_drift");
        await Assert.That(await database.CatalogAsync()).IsEqualTo(before);
    }

    [Test]
    [Arguments("role")]
    [Arguments("anchor")]
    [Arguments("routine")]
    [Arguments("trigger")]
    public async Task PartialProvisioningWriterFootprintIsNotTreatedAsWholeAbsenceOrRepaired(string footprint)
    {
        await using var database = await ProbeDatabase.CreateAsync(fixture);
        string writer = database.Sql.Name("human_provisioning_changed");
        await ProbeDatabase.ExecuteAsync(database.Migration, footprint switch
        {
            "role" => $"CREATE ROLE {PostgresManagedProviderProvisioningContract.OwnerRole} NOLOGIN NOINHERIT",
            "anchor" => $"CREATE TABLE {database.Sql.Name(PostgresManagedProviderProvisioningContract.SourceTable)} (id smallint)",
            "routine" => $"CREATE FUNCTION {writer}() RETURNS trigger LANGUAGE plpgsql AS 'BEGIN RETURN NULL; END;'",
            // A real extra trigger with the reserved writer name, not a dummy writer
            // installed to satisfy another contract's guard.
            "trigger" => $"""
                CREATE TRIGGER human_provisioning_changed BEFORE UPDATE ON {database.Sql.Table(typeof(SystemSetting))}
                    FOR EACH STATEMENT EXECUTE FUNCTION {database.Function("changed")}()
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(footprint))
        });
        string before = await database.CatalogAsync();
        await using var caller = await database.OpenAsync(PrimaryDatabaseRole.Global);
        var observe = await Assert.ThrowsAsync<PostgresException>(() => database.ReadAsync(caller, null));
        await Assert.That(observe!.SqlState).IsEqualTo("55000");
        await Assert.ThrowsAsync<PostgresException>(() =>
            PostgresStorageProbeSourceContract.RequireReadyAsync(database.Migration, fixture.NativeQueueEnrollment));
        await Assert.ThrowsAsync<PostgresException>(database.InstallAsync);
        await Assert.That(await database.CatalogAsync()).IsEqualTo(before);
    }

    private sealed class ProbeDatabase(
        PostgreSqlContainerFixture fixture, PostgreSqlContainer container, ExploreDbContext migration) : IAsyncDisposable
    {
        internal ExploreDbContext Migration { get; } = migration;
        internal HumanPrincipalSourceSql Sql { get; } = new(migration);
        internal Guid Tenant { get; } = Guid.CreateVersion7();
        internal string Anchor => Sql.Name(PostgresStorageProbeSourceContract.SourceTable);
        internal string Function(string operation) => Sql.Name("storage_probe_source_" + operation);

        internal static async Task<ProbeDatabase> CreateAsync(PostgreSqlContainerFixture fixture, bool install = true)
        {
            var original = fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator);
            // Contract roles are cluster-wide: reuse the injected credentials, never
            // mutate the shared fixture cluster or impersonate Runtime with SET ROLE.
            var container = new PostgreSqlBuilder("postgres:18-alpine")
                .WithDatabase("storage_source_" + Guid.CreateVersion7().ToString("N"))
                .WithUsername(original.Username!).WithPassword(original.Password!).Build();
            try
            {
                await container.StartAsync();
                var migration = CreateContext(fixture, container, PrimaryDatabaseRole.Migrator);
                try
                {
                    var database = new ProbeDatabase(fixture, container, migration);
                    await migration.Database.OpenConnectionAsync();
                    await database.CreateSourcesAsync();
                    if (install) await database.InstallAsync();
                    return database;
                }
                catch
                {
                    await migration.DisposeAsync();
                    throw;
                }
            }
            catch
            {
                await container.DisposeAsync();
                throw;
            }
        }

        private async Task CreateSourcesAsync()
        {
            foreach (PrimaryDatabaseRole role in new[]
            {
                PrimaryDatabaseRole.Runtime, PrimaryDatabaseRole.Global,
                PrimaryDatabaseRole.EmailPolicyInstanceMutation, PrimaryDatabaseRole.EmailPolicyTenantMutation
            })
            {
                var original = fixture.GetNativeQueueOptions(role);
                await ExecuteAsync(Migration, $"""
                    CREATE ROLE {Sql.Quote(original.Username!)} LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE
                        NOINHERIT NOREPLICATION NOBYPASSRLS PASSWORD {Literal(original.Password!)};
                    """);
            }
            await ExecuteAsync(Migration, $"CREATE SCHEMA {Sql.Quote(Sql.Schema)}");
            // Full mapped source columns and their real key/index/FK targets, rather
            // than LIKE copies without RLS or security-definer placeholder routines.
            var entities = Sources.Select(type => Migration.Model.FindEntityType(type)!).ToArray();
            var principals = entities.SelectMany(entity => entity.GetForeignKeys())
                .Select(key => key.PrincipalEntityType).Distinct().Except(entities).ToArray();
            foreach (var entity in entities.Concat(principals))
            {
                Type type = entity.ClrType;
                string columns = string.Join(",", entity.GetProperties().Select(property =>
                    $"{Sql.Column(type, property.Name)} {property.GetRelationalTypeMapping().StoreType}"
                    + (property.FindAnnotation(PortableOrdinalAsciiPropertyExtensions.AnnotationName)?.Value is true
                        ? " COLLATE \"C\"" : "")
                    + (property.IsNullable ? "" : " NOT NULL")));
                await ExecuteAsync(Migration, $"CREATE TABLE {Sql.Table(type)} ({columns})");
                foreach (var key in entity.GetKeys())
                    await ExecuteAsync(Migration, $"""
                        ALTER TABLE {Sql.Table(type)} ADD {(key.IsPrimaryKey() ? "PRIMARY KEY" : "UNIQUE")}
                            ({Columns(type, key.Properties)})
                        """);
                foreach (var index in entity.GetIndexes().Where(index => index.IsUnique))
                    await ExecuteAsync(Migration, $"""
                        CREATE UNIQUE INDEX {Sql.Quote(index.GetDatabaseName()!)} ON {Sql.Table(type)}
                            ({Columns(type, index.Properties)})
                            {(index.GetFilter() is { } filter ? "WHERE " + filter : "")}
                        """);
            }
            foreach (var entity in entities)
                foreach (var key in entity.GetForeignKeys())
                    await ExecuteAsync(Migration, $"""
                        ALTER TABLE {Sql.Table(entity.ClrType)} ADD FOREIGN KEY ({Columns(entity.ClrType, key.Properties)})
                            REFERENCES {Sql.Table(key.PrincipalEntityType.ClrType)}
                            ({Columns(key.PrincipalEntityType.ClrType, key.PrincipalKey.Properties)})
                        """);
            // Only the source's relevant FK boundary is installed. Supporting rows
            // carry actual mapped types; unrelated principal aggregate graphs are not exercised.
            foreach (var principal in principals)
            {
                object id = principal.FindPrimaryKey()!.Properties.Single().ClrType == typeof(Guid) ? Tenant : 1;
                await InsertRowAsync(Migration, principal.ClrType, ("Id", id));
            }
            foreach (var table in PostgresTenantRowLevelSecurityModel.GetTenantTables(Migration.Model)
                .Where(table => entities.Any(entity => entity.GetTableName() == table.TableName)))
                await ExecuteAsync(Migration, PostgresTenantRowLevelSecurityModel.BuildEnableRlsSql(table, Sql.Schema));
            await ExecuteAsync(Migration, $"""
                GRANT USAGE ON SCHEMA {Sql.Quote(Sql.Schema)}
                    TO {Sql.Quote(fixture.NativeQueueEnrollment.RuntimeLogin)},
                        {Sql.Quote(fixture.NativeQueueEnrollment.GlobalLogin)},
                        {Sql.Quote(fixture.NativeQueueEnrollment.EmailInstanceMutationLogin)},
                        {Sql.Quote(fixture.NativeQueueEnrollment.EmailTenantMutationLogin)};
                """);
        }

        private string Columns(Type type, IReadOnlyList<IProperty> properties) =>
            string.Join(",", properties.Select(property => Sql.Column(type, property.Name)));

        internal Task InstallAsync() =>
            PostgresStorageProbeSourceContract.ApplyAsync(Migration, fixture.NativeQueueEnrollment);

        internal async Task<ExploreDbContext> OpenAsync(PrimaryDatabaseRole role)
        {
            var context = CreateContext(fixture, container, role);
            try
            {
                await context.Database.OpenConnectionAsync();
                await Assert.That(await ScalarAsync<string>(context, "SELECT session_user::text"))
                    .IsEqualTo(fixture.GetNativeQueueOptions(role).Username!);
                await SetTenantAsync(context, null);
                return context;
            }
            catch
            {
                await context.DisposeAsync();
                throw;
            }
        }

        internal async Task SetTenantAsync(ExploreDbContext context, Guid? tenant)
        {
            await using var command = Command(context, "SELECT set_config('app.current_tenant_id',@tenant,false)");
            command.Parameters.Add(new NpgsqlParameter("tenant", tenant?.ToString("D") ?? ""));
            await command.ExecuteScalarAsync();
        }

        internal async Task<long> ReadAsync(ExploreDbContext context, Guid? tenant, bool acquire = false)
        {
            await using var command = Command(context, $"SELECT {Function(acquire ? "acquire" : "observe")}(@tenant)");
            command.Parameters.Add(new NpgsqlParameter("tenant", NpgsqlDbType.Uuid) { Value = (object?)tenant ?? DBNull.Value });
            return (long)(await command.ExecuteScalarAsync())!;
        }

        internal Task InsertAsync(ExploreDbContext context, Type type)
        {
            if (type == typeof(InstanceBootstrapState))
                return InsertRowAsync(context, type, ("Generation", 1L));
            if (type == typeof(SecretBinding))
                return InsertRowAsync(context, type, ("SettingKey", "unrelated.probe.metadata"),
                    ("Qualifier", ""), ("SettingScopeId", 1), ("SecretSourceTypeId", 1),
                    ("SecretValidationStatusId", 1), ("EnvironmentVariableName", "PROBE_SOURCE_METADATA"));
            if (type == typeof(TenantSetting))
                return InsertRowAsync(context, type, ("SettingKey", "unrelated.probe.metadata"), ("TenantId", Tenant));
            return InsertRowAsync(context, type, ("SettingKey", "unrelated.probe.metadata"), ("SettingValueTypeId", 1));
        }

        internal async Task WriteAsync(ExploreDbContext context, Type type, string operation)
        {
            string table = Sql.Table(type);
            switch (operation)
            {
                case "insert":
                    await InsertAsync(context, type);
                    break;
                case "metadata":
                    string assignment = type == typeof(SystemSetting) ? $"{Sql.Column(type, "Description")} = 'changed metadata'"
                        : type == typeof(TenantSetting) ? $"{Sql.Column(type, "UpdatedBy")} = '{Guid.CreateVersion7()}'::uuid"
                        : type == typeof(SecretBinding) ? $"{Sql.Column(type, "EnvironmentVariableName")} = 'PROBE_RENAMED_METADATA'"
                        : $"{Sql.Column(type, "Generation")} = {Sql.Column(type, "Generation")} + 1";
                    await ExecuteAsync(context, $"UPDATE {table} SET {assignment}");
                    break;
                case "rename":
                    string rename = type == typeof(InstanceBootstrapState)
                        ? $"{Sql.Column(type, "Generation")} = {Sql.Column(type, "Generation")} + 1"
                        : $"{Sql.Column(type, "SettingKey")} = 'unrelated.probe.renamed'";
                    await ExecuteAsync(context, $"UPDATE {table} SET {rename}");
                    break;
                case "replacement":
                    await ExecuteAsync(context, $"DELETE FROM {table}");
                    await InsertAsync(context, type);
                    break;
                case "delete":
                    await ExecuteAsync(context, $"DELETE FROM {table}");
                    break;
                case "truncate":
                    await ExecuteAsync(context, $"TRUNCATE {table}");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(operation));
            }
        }

        private async Task InsertRowAsync(ExploreDbContext context, Type type, params (string Name, object Value)[] supplied)
        {
            var values = supplied.ToDictionary(value => value.Name, value => value.Value);
            foreach (var property in Migration.Model.FindEntityType(type)!.GetProperties().Where(property => !property.IsNullable))
            {
                if (values.ContainsKey(property.Name)) continue;
                string storeType = property.GetRelationalTypeMapping().StoreType;
                values[property.Name] = storeType switch
                {
                    "uuid" => Guid.CreateVersion7(),
                    "integer" => 1,
                    "bigint" => 1L,
                    "boolean" => false,
                    "timestamp with time zone" => DateTime.UnixEpoch,
                    "timestamp without time zone" => DateTime.SpecifyKind(DateTime.UnixEpoch, DateTimeKind.Unspecified),
                    "text" => "source",
                    "jsonb" => "{}",
                    "bytea" => Array.Empty<byte>(),
                    "numeric" => 0m,
                    "double precision" => 0d,
                    _ when storeType.StartsWith("character", StringComparison.Ordinal) => "source",
                    _ when storeType.StartsWith("numeric(", StringComparison.Ordinal) => 0m,
                    _ => throw new InvalidOperationException($"Declare a seed value for {type.Name}.{property.Name} ({storeType}).")
                };
            }
            await using var command = Command(context, $"""
                INSERT INTO {Sql.Table(type)} ({string.Join(",", values.Keys.Select(name => Sql.Column(type, name)))})
                VALUES ({string.Join(",", values.Select((_, index) => "@p" + index))})
                """);
            int index = 0;
            foreach (var value in values)
            {
                var parameter = new NpgsqlParameter("p" + index++, value.Value);
                if (Migration.Model.FindEntityType(type)!.FindProperty(value.Key)!.GetRelationalTypeMapping().StoreType == "jsonb")
                    parameter.NpgsqlDbType = NpgsqlDbType.Jsonb;
                command.Parameters.Add(parameter);
            }
            await command.ExecuteNonQueryAsync();
        }

        internal Task<string> RowsAsync() => ScalarAsync<string>(Migration,
            "SELECT jsonb_build_array(" + string.Join(",", Sources.Select(type =>
                $"(SELECT jsonb_agg(to_jsonb(t) ORDER BY to_jsonb(t)::text) FROM {Sql.Table(type)} t)")) + ")::text");

        internal Task<string> OriginalPrivilegesAsync() => ScalarAsync<string>(Migration, $"""
            SELECT jsonb_agg(jsonb_build_array(r.rolname,c.relname,a.attname,
                has_table_privilege(r.oid,c.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER'),
                has_column_privilege(r.oid,c.oid,a.attnum,'SELECT'),
                has_column_privilege(r.oid,c.oid,a.attnum,'INSERT'),
                has_column_privilege(r.oid,c.oid,a.attnum,'UPDATE'),
                has_column_privilege(r.oid,c.oid,a.attnum,'REFERENCES'))
                ORDER BY r.rolname,c.relname,a.attnum)::text
            FROM pg_roles r CROSS JOIN pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped
            WHERE r.rolname IN ({Literal(fixture.NativeQueueEnrollment.RuntimeLogin)},{Literal(fixture.NativeQueueEnrollment.GlobalLogin)})
                AND n.nspname = {Literal(Sql.Schema)}
                AND c.relname IN ({string.Join(",", Sources.Select(type => Literal(Migration.Model.FindEntityType(type)!.GetTableName()!)))})
            """);

        internal Task<string> CatalogAsync() => ScalarAsync<string>(Migration, $"""
            SELECT jsonb_build_object(
                'relations',(SELECT jsonb_agg(to_jsonb(c) ORDER BY c.oid) FROM pg_class c
                    JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = {Literal(Sql.Schema)}),
                'attributes',(SELECT jsonb_agg(to_jsonb(a) ORDER BY a.attrelid,a.attnum) FROM pg_attribute a
                    JOIN pg_class c ON c.oid = a.attrelid JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname = {Literal(Sql.Schema)}),
                'routines',(SELECT jsonb_agg(to_jsonb(p) || jsonb_build_object('stamp',obj_description(p.oid,'pg_proc'))
                    ORDER BY p.oid) FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
                    WHERE n.nspname = {Literal(Sql.Schema)}),
                'triggers',(SELECT jsonb_agg(to_jsonb(t) ORDER BY t.oid) FROM pg_trigger t
                    JOIN pg_class c ON c.oid = t.tgrelid JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname = {Literal(Sql.Schema)}),
                'policies',(SELECT jsonb_agg(to_jsonb(p) || jsonb_build_object('stamp',obj_description(p.oid,'pg_policy'))
                    ORDER BY p.oid) FROM pg_policy p JOIN pg_class c ON c.oid = p.polrelid
                    JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = {Literal(Sql.Schema)}),
                'roles',(SELECT jsonb_agg(to_jsonb(r) || jsonb_build_object('stamp',shobj_description(r.oid,'pg_authid'))
                    ORDER BY r.oid) FROM pg_roles r),
                'memberships',(SELECT jsonb_agg(to_jsonb(m) ORDER BY m.roleid,m.member) FROM pg_auth_members m),
                'anchor',(SELECT jsonb_agg(to_jsonb(t) ORDER BY id) FROM {Anchor} t)
            )::text
            """);

        internal static DbCommand Command(ExploreDbContext context, string sql)
        {
            var command = context.Database.GetDbConnection().CreateCommand();
            command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandTimeout = 15;
            command.CommandText = sql;
            return command;
        }

        internal static async Task ExecuteAsync(ExploreDbContext context, string sql)
        {
            await using var command = Command(context, sql);
            await command.ExecuteNonQueryAsync();
        }

        internal static async Task<T> ScalarAsync<T>(ExploreDbContext context, string sql)
        {
            await using var command = Command(context, sql);
            return (T)(await command.ExecuteScalarAsync())!;
        }

        private static ExploreDbContext CreateContext(
            PostgreSqlContainerFixture fixture, PostgreSqlContainer container, PrimaryDatabaseRole role)
        {
            var endpoint = new NpgsqlConnectionStringBuilder(container.GetConnectionString());
            var original = fixture.GetNativeQueueOptions(role) with
            {
                Host = endpoint.Host, Port = endpoint.Port, Database = endpoint.Database
            };
            var builder = TestDbContextOptions.Create<ExploreDbContext>();
            PrimaryDatabaseProviderComposition.ConfigureApplication(builder, original);
            return new ExploreDbContext(builder.Options);
        }

        private static string Literal(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

        public async ValueTask DisposeAsync()
        {
            try { await Migration.DisposeAsync(); }
            finally { await container.DisposeAsync(); }
        }
    }
}
