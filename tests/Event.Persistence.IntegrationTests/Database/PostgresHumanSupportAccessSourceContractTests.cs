using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using Testcontainers.PostgreSql;

namespace Event.Persistence.IntegrationTests.Database;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class PostgresHumanSupportAccessSourceContractTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task ManagedObservationPreservesAllowedValuesJson()
    {
        await using var ingress = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var (subject, forwarding, _) =
            await global::Event.Persistence.IntegrationTests.HumanSupportAccessSourceFenceTests.CaptureAsync(ingress, null);
        await using var database = await SupportDatabase.CreateAsync(fixture);
        await database.SeedAsync();
        await SupportDatabase.ExecuteAsync(database.Migration, $"""
            UPDATE {database.Sql.Table(typeof(SystemSetting))}
            SET {database.Sql.Column(typeof(SystemSetting), "AllowedValues")} = '["true","false"]'::jsonb
            WHERE {database.Sql.Column(typeof(SystemSetting), "SettingKey")} =
                '{GovernanceSettingKeys.SupportAccess.Enabled}'
            """);
        await using var context = await database.OpenAsync(PrimaryDatabaseRole.Runtime);
        context.TenantContext = ingress;
        await database.SetTenantAsync(context, ingress.TenantId);
        var source = new HumanSupportAccessSourceFence(context, subject, forwarding,
            PolicySourceSetPurpose.RuntimeTenant, fixture.NativeQueueEnrollment);

        var facts = await source.ObserveAsync();

        using var allowed = JsonDocument.Parse(facts.Settings.Single(row =>
            row.SettingKey == GovernanceSettingKeys.SupportAccess.Enabled).AllowedValues!);
        await Assert.That(allowed.RootElement.GetArrayLength()).IsEqualTo(2);
        await Assert.That(allowed.RootElement[0].GetString()).IsEqualTo("true");
        await Assert.That(allowed.RootElement[1].GetString()).IsEqualTo("false");
        await Assert.That(facts.Sessions.Length).IsEqualTo(0);
    }

    private static readonly string[] SupportKeys =
    [
        GovernanceSettingKeys.SupportAccess.Enabled,
        GovernanceSettingKeys.SupportAccess.MaxReadOnlyMinutes,
        GovernanceSettingKeys.SupportAccess.MaxWriteMinutes,
        GovernanceSettingKeys.SupportAccess.AllowWriteMode,
        GovernanceSettingKeys.SupportAccess.RequireTicketReference,
        GovernanceSettingKeys.SupportAccess.OneActiveSessionPerActor
    ];

    [Test]
    [Arguments(PrimaryDatabaseRole.Runtime, 1, false)]
    [Arguments(PrimaryDatabaseRole.Runtime, 1, true)]
    [Arguments(PrimaryDatabaseRole.Global, 2, false)]
    [Arguments(PrimaryDatabaseRole.Global, 2, true)]
    [Arguments(PrimaryDatabaseRole.Global, 3, false)]
    [Arguments(PrimaryDatabaseRole.Global, 3, true)]
    public async Task ReinstallationPreservesEnrolledSessionAndFixedGovernanceProjection(
        PrimaryDatabaseRole role, int purpose, bool acquire)
    {
        await using var database = await SupportDatabase.CreateAsync(fixture);
        await database.SeedAsync();
        string before = await database.SourceRowsAsync();
        await database.InstallAsync();
        await Assert.That(await database.SourceRowsAsync()).IsEqualTo(before);
        await using var caller = await database.OpenAsync(role);
        Guid? tenant = role == PrimaryDatabaseRole.Runtime ? database.Tenant : null;
        await database.SetTenantAsync(caller, tenant);
        await SupportDatabase.ExecuteAsync(caller,
            PostgresHumanSupportAccessSourceContract.AdmissionSql(database.Migration, fixture.NativeQueueEnrollment));
        await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        var facts = await database.ReadAsync(caller, database.Actor, purpose, tenant,
            database.Session, database.Tenant, acquire);

        await Assert.That(facts.EnumerateObject().Count()).IsEqualTo(2);
        var session = facts.GetProperty("q0").EnumerateArray().Single();
        await Assert.That(session.EnumerateObject().Count()).IsEqualTo(12);
        await Assert.That(session.GetProperty("Id").GetGuid()).IsEqualTo(database.Session);
        await Assert.That(session.GetProperty("ActorUserId").GetGuid()).IsEqualTo(database.Actor);
        await Assert.That(session.GetProperty("TargetTenantId").GetGuid()).IsEqualTo(database.Tenant);
        await Assert.That(session.GetProperty("TargetTenantUserId").GetGuid()).IsEqualTo(database.TargetUser);
        await Assert.That(session.GetProperty("StatusId").GetInt32()).IsEqualTo(1);
        await Assert.That(session.GetProperty("ModeId").GetInt32()).IsEqualTo(1);
        await Assert.That(session.GetProperty("StartedAtUtc").GetDateTimeOffset()).IsEqualTo(DateTimeOffset.UnixEpoch);
        await Assert.That(session.GetProperty("ExpiresAtUtc").GetDateTimeOffset()).IsEqualTo(DateTimeOffset.UnixEpoch.AddHours(1));
        await Assert.That(session.GetProperty("EndedAtUtc").ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(session.GetProperty("ReasonCode").GetString()).IsEqualTo("support-investigation");
        await Assert.That(session.GetProperty("TicketReference").GetString()).IsEqualTo("support-ticket");
        await Assert.That(session.GetProperty("ConcurrencyStamp").GetGuid()).IsEqualTo(database.Stamp);
        var settings = facts.GetProperty("q1").EnumerateArray().ToArray();
        await Assert.That(settings.Length).IsEqualTo(6);
        foreach (string key in SupportKeys)
        {
            var row = settings.Single(row => row.GetProperty("SettingKey").GetString() == key);
            await Assert.That(row.EnumerateObject().Count()).IsEqualTo(8);
            await Assert.That(row.GetProperty("Id").GetGuid() == Guid.Empty).IsFalse();
            await Assert.That(row.GetProperty("Value").GetString()).IsEqualTo("support:" + key);
            await Assert.That(row.GetProperty("SettingValueTypeId").GetInt32()).IsEqualTo(1);
            await Assert.That(row.GetProperty("IsLocked").GetBoolean()).IsTrue();
            await Assert.That(row.GetProperty("Description").GetString()).IsEqualTo("support description");
            await Assert.That(row.GetProperty("Category").GetString()).IsEqualTo("support category");
            var allowed = row.GetProperty("AllowedValues");
            await Assert.That(allowed.GetArrayLength()).IsEqualTo(2);
            await Assert.That(allowed[0].GetString()).IsEqualTo("true");
            await Assert.That(allowed[1].GetString()).IsEqualTo("false");
        }
        await transaction.RollbackAsync();
    }

    [Test]
    [Arguments(PrimaryDatabaseRole.Runtime, 1)]
    [Arguments(PrimaryDatabaseRole.Global, 2)]
    [Arguments(PrimaryDatabaseRole.Global, 3)]
    public async Task ObserveSelectsOnlyTheRequestedActorSessionAndResolvedTenant(PrimaryDatabaseRole role, int purpose)
    {
        await using var database = await SupportDatabase.CreateAsync(fixture);
        await database.SeedAsync();
        await using var caller = await database.OpenAsync(role);
        Guid? tenant = role == PrimaryDatabaseRole.Runtime ? database.Tenant : null;
        await database.SetTenantAsync(caller, tenant);

        foreach (var selection in new[]
        {
            (database.OtherActor, (Guid?)database.Session),
            (database.Actor, (Guid?)database.OtherSession),
            (database.Actor, (Guid?)null),
            (database.Actor, (Guid?)Guid.CreateVersion7())
        })
        {
            var facts = await database.ReadAsync(caller, selection.Item1, purpose, tenant, selection.Item2, database.Tenant);
            await Assert.That(facts.GetProperty("q0").GetArrayLength()).IsEqualTo(0);
            await Assert.That(facts.GetProperty("q1").GetArrayLength()).IsEqualTo(6);
        }
        var other = await database.ReadAsync(caller, database.OtherActor, purpose, tenant,
            database.OtherSession, database.Tenant);
        await Assert.That(other.GetProperty("q0").EnumerateArray().Single().GetProperty("Id").GetGuid())
            .IsEqualTo(database.OtherSession);
        if (role == PrimaryDatabaseRole.Global)
        {
            var mismatch = await database.ReadAsync(caller, database.Actor, purpose, null,
                database.Session, database.OtherTenant);
            await Assert.That(mismatch.GetProperty("q0").GetArrayLength()).IsEqualTo(0);
            var unresolved = await database.ReadAsync(caller, database.Actor, purpose, null, database.Session, null);
            await Assert.That(unresolved.GetProperty("q0").GetArrayLength()).IsEqualTo(1);
            var otherTenant = await database.ReadAsync(caller, database.Actor, purpose, null,
                database.OtherTenantSession, database.OtherTenant);
            await Assert.That(otherTenant.GetProperty("q0").EnumerateArray().Single().GetProperty("TargetTenantId").GetGuid())
                .IsEqualTo(database.OtherTenant);
        }
        else
        {
            var otherTenant = await database.ReadAsync(caller, database.Actor, purpose, tenant,
                database.OtherTenantSession, database.Tenant);
            await Assert.That(otherTenant.GetProperty("q0").GetArrayLength()).IsEqualTo(0);
        }
    }

    [Test]
    [Arguments(PrimaryDatabaseRole.Runtime, 2, IsolationLevel.Serializable, false)]
    [Arguments(PrimaryDatabaseRole.Runtime, 3, IsolationLevel.Serializable, true)]
    [Arguments(PrimaryDatabaseRole.Global, 1, IsolationLevel.Serializable, false)]
    [Arguments(PrimaryDatabaseRole.Global, 4, IsolationLevel.Serializable, true)]
    [Arguments(PrimaryDatabaseRole.EmailPolicyInstanceMutation, 1, IsolationLevel.Serializable, false)]
    [Arguments(PrimaryDatabaseRole.EmailPolicyInstanceMutation, 1, IsolationLevel.Serializable, true)]
    [Arguments(PrimaryDatabaseRole.Runtime, 1, IsolationLevel.ReadCommitted, true)]
    [Arguments(PrimaryDatabaseRole.Runtime, 1, IsolationLevel.RepeatableRead, true)]
    [Arguments(PrimaryDatabaseRole.Global, 2, IsolationLevel.ReadCommitted, true)]
    public async Task WrongLoginPurposeOrAcquisitionIsolationFailsClosed(
        PrimaryDatabaseRole role, int purpose, IsolationLevel isolation, bool acquire)
    {
        await using var database = await SupportDatabase.CreateAsync(fixture);
        await using var caller = await database.OpenAsync(role);
        Guid? tenant = role == PrimaryDatabaseRole.Global ? null : database.Tenant;
        await database.SetTenantAsync(caller, tenant);
        await using var transaction = await caller.Database.BeginTransactionAsync(isolation);

        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            database.ReadAsync(caller, database.Actor, purpose, tenant, database.Session, tenant, acquire));

        await Assert.That(error!.SqlState).IsEqualTo("42501");
        if (role != PrimaryDatabaseRole.EmailPolicyInstanceMutation)
            await Assert.That(error.MessageText).IsEqualTo("human_support_caller_mismatch");
        await transaction.RollbackAsync();
    }

    [Test]
    [Arguments("different-session")]
    [Arguments("missing-session")]
    [Arguments("missing-tenant")]
    [Arguments("empty-tenant")]
    [Arguments("different-resolved")]
    [Arguments("missing-resolved")]
    [Arguments("empty-session-id")]
    [Arguments("empty-actor")]
    [Arguments("global-tenant")]
    public async Task InvalidActorOrTenantContextFailsClosed(string scenario)
    {
        await using var database = await SupportDatabase.CreateAsync(fixture);
        bool global = scenario == "global-tenant";
        await using var caller = await database.OpenAsync(global ? PrimaryDatabaseRole.Global : PrimaryDatabaseRole.Runtime);
        Guid? tenant = scenario == "missing-tenant" ? null
            : scenario == "empty-tenant" ? Guid.Empty : database.Tenant;
        Guid? sessionTenant = scenario == "different-session" ? database.OtherTenant
            : scenario == "missing-session" ? null : tenant;
        Guid? resolved = scenario == "different-resolved" ? database.OtherTenant
            : scenario == "missing-resolved" ? null : tenant;
        await database.SetTenantAsync(caller, sessionTenant);

        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            database.ReadAsync(caller, scenario == "empty-actor" ? Guid.Empty : database.Actor,
                global ? 2 : 1, tenant, scenario == "empty-session-id" ? Guid.Empty : database.Session, resolved));

        await Assert.That(error!.SqlState).IsEqualTo("42501");
        await Assert.That(error.MessageText).IsEqualTo("human_support_caller_mismatch");
    }

    [Test]
    [Arguments("session-insert")]
    [Arguments("session-update")]
    [Arguments("setting-insert")]
    [Arguments("setting-update")]
    public async Task AcquiredAnchorBlocksIndependentSourceWritesUntilRollback(string source)
    {
        await using var database = await SupportDatabase.CreateAsync(fixture);
        await database.SeedAsync();
        await using var caller = await database.OpenAsync(PrimaryDatabaseRole.Runtime);
        await using var writer = await database.OpenAsync(PrimaryDatabaseRole.Migrator);
        await database.SetTenantAsync(caller, database.Tenant);
        await Assert.That(await SupportDatabase.ScalarAsync<int>(caller, "SELECT pg_backend_pid()")
            == await SupportDatabase.ScalarAsync<int>(writer, "SELECT pg_backend_pid()")).IsFalse();
        await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await database.ReadAsync(caller, database.Actor, 1, database.Tenant, database.Session, database.Tenant, acquire: true);
        await SupportDatabase.ExecuteAsync(writer, "SET lock_timeout = '250ms'");
        string before = await database.SourceRowsAsync();

        // The completed acquisition holds the anchor before this independent command.
        // Writes target unselected rows, so a selected-session row lock cannot explain the conflict.
        var error = await Assert.ThrowsAsync<PostgresException>(() => database.WriteAsync(writer, source));

        await Assert.That(error!.SqlState).IsEqualTo("55P03");
        await Assert.That(await database.SourceRowsAsync()).IsEqualTo(before);
        await transaction.RollbackAsync();
        await database.WriteAsync(writer, source);
        await Assert.That(await database.SourceRowsAsync() == before).IsFalse();
    }

    [Test]
    public async Task RuntimeHasExecuteOnlyAccessAndCannotReadOrMutateOriginalSources()
    {
        await using var database = await SupportDatabase.CreateAsync(fixture);
        await database.SeedAsync();
        await using var runtime = await database.OpenAsync(PrimaryDatabaseRole.Runtime);
        string before = await database.SourceRowsAsync();
        foreach (var source in HumanSupportAccessSourceSql.Sources)
        {
            string table = database.Sql.Table(source.Type);
            foreach (string statement in new[]
            {
                $"SELECT {database.Sql.Column(source.Type, source.Columns[0])} FROM {table}",
                $"UPDATE {table} SET {database.Sql.Column(source.Type, source.Columns[0])} = {database.Sql.Column(source.Type, source.Columns[0])}",
                $"DELETE FROM {table}"
            })
            {
                var error = await Assert.ThrowsAsync<PostgresException>(() => SupportDatabase.ExecuteAsync(runtime, statement));
                await Assert.That(error!.SqlState).IsEqualTo("42501");
            }
        }
        await Assert.That(await database.SourceRowsAsync()).IsEqualTo(before);
    }

    [Test]
    [Arguments("column", "human_support_source_drift")]
    [Arguments("trigger", "human_support_writer_drift")]
    [Arguments("acl", "native_privilege_guard_not_ready")]
    [Arguments("routine", "human_support_routine_drift")]
    [Arguments("body", "human_support_routine_drift")]
    [Arguments("self-restamp", "human_support_body_drift")]
    [Arguments("rls-read", "human_support_rls_drift")]
    [Arguments("rls-update", "human_support_rls_drift")]
    [Arguments("rls-extra", "human_support_rls_drift")]
    public async Task AdmissionAndReinstallationRejectDriftWithoutRepair(string drift, string message)
    {
        await using var database = await SupportDatabase.CreateAsync(fixture);
        string observe = PostgresHumanSupportAccessSourceContract.ObserveFunctionSql(database.Migration);
        string table = database.Sql.Table(typeof(SupportAccessSession));
        const string signature = "(uuid,integer,uuid,uuid,uuid)";
        const string counterfeit = "BEGIN RETURN '{}'::jsonb; END;";
        string ddl = drift switch
        {
            "column" => $"ALTER TABLE {database.Sql.Table(typeof(SystemSetting))} ALTER COLUMN {database.Sql.Column(typeof(SystemSetting), "IsLocked")} DROP NOT NULL",
            "trigger" => $"ALTER TABLE {table} DISABLE TRIGGER zz_human_support_access_source_write",
            "acl" => $"GRANT EXECUTE ON FUNCTION {observe}{signature} TO PUBLIC",
            "routine" => $"ALTER FUNCTION {observe}{signature} STABLE",
            "body" or "self-restamp" => $"""
                CREATE OR REPLACE FUNCTION {observe}
                    (p_actor uuid,p_purpose integer,p_tenant uuid,p_session uuid,p_resolved uuid) RETURNS jsonb
                    LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                    AS $body${counterfeit}$body$;
                """ + (drift == "self-restamp" ? $"""
                COMMENT ON FUNCTION {observe}{signature} IS
                    'human-support-access-source:v1:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(counterfeit)))}';
                """ : ""),
            "rls-read" => $"ALTER POLICY human_support_access_source_read ON {table} USING (false)",
            "rls-update" => $"ALTER POLICY human_support_access_source_update ON {table} WITH CHECK (false)",
            "rls-extra" => $"CREATE POLICY counterfeit ON {table} FOR SELECT TO {PostgresHumanSupportAccessSourceContract.OwnerRole} USING (true)",
            _ => throw new ArgumentOutOfRangeException(nameof(drift))
        };
        await SupportDatabase.ExecuteAsync(database.Migration, ddl);
        string before = await database.CatalogAsync();
        await using var runtime = await database.OpenAsync(PrimaryDatabaseRole.Runtime);

        var admission = await Assert.ThrowsAsync<PostgresException>(() =>
            SupportDatabase.ExecuteAsync(runtime,
                PostgresHumanSupportAccessSourceContract.AdmissionSql(database.Migration, fixture.NativeQueueEnrollment)));
        await Assert.That(admission!.SqlState).IsEqualTo("55000");
        await Assert.That(admission.MessageText).IsEqualTo(message);
        var reinstall = await Assert.ThrowsAsync<PostgresException>(database.InstallAsync);
        await Assert.That(reinstall!.SqlState).IsEqualTo("55000");
        await Assert.That(reinstall.MessageText).IsEqualTo(message);
        await Assert.That(await database.CatalogAsync()).IsEqualTo(before);
    }

    private sealed class SupportDatabase(
        PostgreSqlContainerFixture fixture, PostgreSqlContainer container, ExploreDbContext migration) : IAsyncDisposable
    {
        internal ExploreDbContext Migration { get; } = migration;
        internal HumanPrincipalSourceSql Sql { get; } = new(migration);
        internal Guid Actor { get; } = Guid.CreateVersion7();
        internal Guid OtherActor { get; } = Guid.CreateVersion7();
        internal Guid Tenant { get; } = Guid.CreateVersion7();
        internal Guid OtherTenant { get; } = Guid.CreateVersion7();
        internal Guid Session { get; } = Guid.CreateVersion7();
        internal Guid OtherSession { get; } = Guid.CreateVersion7();
        internal Guid OtherTenantSession { get; } = Guid.CreateVersion7();
        internal Guid TargetUser { get; } = Guid.CreateVersion7();
        internal Guid Stamp { get; } = Guid.CreateVersion7();

        internal static async Task<SupportDatabase> CreateAsync(PostgreSqlContainerFixture fixture)
        {
            var original = fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator);
            // Native role custody is cluster-wide. Keep all DDL off the fixture's shared cluster.
            var container = new PostgreSqlBuilder("postgres:18-alpine")
                .WithDatabase("support_source_" + Guid.CreateVersion7().ToString("N"))
                .WithUsername(original.Username!)
                .WithPassword(original.Password!)
                .Build();
            try
            {
                await container.StartAsync();
                var migration = CreateContext(fixture, container, PrimaryDatabaseRole.Migrator);
                var database = new SupportDatabase(fixture, container, migration);
                try
                {
                    await migration.Database.OpenConnectionAsync();
                    await database.CreateSourcesAsync();
                    await database.InstallAsync();
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
            foreach (var role in new[]
            {
                PrimaryDatabaseRole.Runtime, PrimaryDatabaseRole.Global, PrimaryDatabaseRole.EmailPolicyInstanceMutation
            })
            {
                var original = fixture.GetNativeQueueOptions(role);
                await ExecuteAsync(Migration, $"""
                    CREATE ROLE {Sql.Quote(original.Username!)} LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE
                        NOINHERIT NOREPLICATION NOBYPASSRLS PASSWORD {Literal(original.Password!)};
                    """);
            }
            await ExecuteAsync(Migration, $"CREATE SCHEMA {Sql.Quote(Sql.Schema)}");
            // The native contract declares its complete source vocabulary. Do not migrate
            // unrelated entities or introduce application fences into this native test.
            foreach (var source in HumanSupportAccessSourceSql.Sources)
            {
                var entity = Migration.Model.FindEntityType(source.Type)!;
                string columns = string.Join(",", source.Columns.Select(name =>
                {
                    var property = entity.FindProperty(name)!;
                    return $"{Sql.Column(source.Type, name)} {property.GetRelationalTypeMapping().StoreType}"
                        + (property.IsNullable ? "" : " NOT NULL");
                }));
                await ExecuteAsync(Migration, $"CREATE TABLE {Sql.Table(source.Type)} ({columns})");
                foreach (var key in entity.GetKeys())
                    await ExecuteAsync(Migration, $"""
                        ALTER TABLE {Sql.Table(source.Type)} ADD
                            {(key.IsPrimaryKey() ? "PRIMARY KEY" : "UNIQUE")}
                            ({string.Join(",", key.Properties.Select(property => Sql.Column(source.Type, property.Name)))})
                        """);
                foreach (var index in entity.GetIndexes().Where(index => index.IsUnique && index.GetFilter() is null))
                    await ExecuteAsync(Migration, $"""
                        CREATE UNIQUE INDEX ON {Sql.Table(source.Type)}
                            ({string.Join(",", index.Properties.Select(property => Sql.Column(source.Type, property.Name)))})
                        """);
            }
            foreach (var table in PostgresTenantRowLevelSecurityModel.GetTenantTables(Migration.Model)
                .Where(table => HumanSupportAccessSourceSql.Sources.Any(source =>
                    Migration.Model.FindEntityType(source.Type)!.GetTableName() == table.TableName)))
                await ExecuteAsync(Migration, PostgresTenantRowLevelSecurityModel.BuildEnableRlsSql(table, Sql.Schema));
            await ExecuteAsync(Migration, $"""
                GRANT USAGE ON SCHEMA {Sql.Quote(Sql.Schema)} TO
                    {Sql.Quote(fixture.NativeQueueEnrollment.RuntimeLogin)}, {Sql.Quote(fixture.NativeQueueEnrollment.GlobalLogin)},
                    {Sql.Quote(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.EmailPolicyInstanceMutation).Username!)};
                """);
        }

        internal Task InstallAsync() =>
            PostgresHumanSupportAccessSourceContract.ApplyAsync(Migration, fixture.NativeQueueEnrollment);

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
            await using var command = Command(context, "SELECT pg_catalog.set_config('app.current_tenant_id', @tenant, false)");
            command.Parameters.Add(new NpgsqlParameter("tenant", tenant?.ToString("D") ?? ""));
            await command.ExecuteScalarAsync();
        }

        internal async Task<JsonElement> ReadAsync(ExploreDbContext context, Guid actor, int purpose,
            Guid? tenant, Guid? session, Guid? resolved, bool acquire = false)
        {
            string routine = acquire ? PostgresHumanSupportAccessSourceContract.AcquireFunctionSql(Migration)
                : PostgresHumanSupportAccessSourceContract.ObserveFunctionSql(Migration);
            await using var command = Command(context, $"SELECT {routine}(@actor,@purpose,@tenant,@session,@resolved)");
            command.Parameters.Add(new NpgsqlParameter("actor", NpgsqlDbType.Uuid) { Value = actor });
            command.Parameters.Add(new NpgsqlParameter("purpose", NpgsqlDbType.Integer) { Value = purpose });
            command.Parameters.Add(new NpgsqlParameter("tenant", NpgsqlDbType.Uuid) { Value = (object?)tenant ?? DBNull.Value });
            command.Parameters.Add(new NpgsqlParameter("session", NpgsqlDbType.Uuid) { Value = (object?)session ?? DBNull.Value });
            command.Parameters.Add(new NpgsqlParameter("resolved", NpgsqlDbType.Uuid) { Value = (object?)resolved ?? DBNull.Value });
            using var document = JsonDocument.Parse((string)(await command.ExecuteScalarAsync())!);
            return document.RootElement.Clone();
        }

        internal async Task SeedAsync()
        {
            await InsertSessionAsync(Migration, Session, Actor, Tenant);
            await InsertSessionAsync(Migration, OtherSession, OtherActor, Tenant);
            await InsertSessionAsync(Migration, OtherTenantSession, Actor, OtherTenant);
            foreach (string key in SupportKeys.Append("unrelated.support.setting"))
                await InsertAsync(Migration, typeof(SystemSetting), ("Id", Guid.CreateVersion7()),
                    ("SettingKey", key), ("Value", "support:" + key), ("SettingValueTypeId", 1),
                    ("IsLocked", true), ("Description", "support description"), ("Category", "support category"),
                    ("AllowedValues", "[\"true\",\"false\"]"));
        }

        private Task InsertSessionAsync(ExploreDbContext context, Guid id, Guid actor, Guid tenant) =>
            InsertAsync(context, typeof(SupportAccessSession), ("Id", id), ("ActorUserId", actor),
                ("TargetTenantId", tenant), ("TargetTenantUserId", TargetUser), ("StatusId", 1), ("ModeId", 1),
                ("StartedAtUtc", DateTimeOffset.UnixEpoch), ("ExpiresAtUtc", DateTimeOffset.UnixEpoch.AddHours(1)),
                ("ReasonCode", "support-investigation"), ("TicketReference", "support-ticket"), ("ConcurrencyStamp", Stamp));

        internal Task WriteAsync(ExploreDbContext context, string source) => source switch
        {
            "session-insert" => InsertSessionAsync(context, Guid.CreateVersion7(), OtherActor, OtherTenant),
            "session-update" => ExecuteAsync(context, $"""
                UPDATE {Sql.Table(typeof(SupportAccessSession))} SET {Sql.Column(typeof(SupportAccessSession), "StatusId")} = 2
                    WHERE {Sql.Column(typeof(SupportAccessSession), "Id")} = '{OtherSession:D}'::uuid
                """),
            "setting-insert" => InsertAsync(context, typeof(SystemSetting), ("Id", Guid.CreateVersion7()),
                ("SettingKey", "new.unrelated.support.setting"), ("Value", "new"), ("SettingValueTypeId", 1), ("IsLocked", false)),
            "setting-update" => ExecuteAsync(context, $"""
                UPDATE {Sql.Table(typeof(SystemSetting))} SET {Sql.Column(typeof(SystemSetting), "Value")} = 'changed'
                    WHERE {Sql.Column(typeof(SystemSetting), "SettingKey")} = 'unrelated.support.setting'
                """),
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };

        private async Task InsertAsync(ExploreDbContext context, Type type, params (string Name, object Value)[] values)
        {
            await using var command = Command(context, $"""
                INSERT INTO {Sql.Table(type)} ({string.Join(",", values.Select(value => Sql.Column(type, value.Name)))})
                VALUES ({string.Join(",", values.Select((_, index) => "@p" + index))})
                """);
            for (int index = 0; index < values.Length; index++)
            {
                var parameter = new NpgsqlParameter("p" + index, values[index].Value);
                if (Migration.Model.FindEntityType(type)!.FindProperty(values[index].Name)!
                    .GetRelationalTypeMapping().StoreType == "jsonb")
                    parameter.NpgsqlDbType = NpgsqlDbType.Jsonb;
                command.Parameters.Add(parameter);
            }
            await command.ExecuteNonQueryAsync();
        }

        internal Task<string> SourceRowsAsync() => ScalarAsync<string>(Migration,
            "SELECT jsonb_build_array(" + string.Join(",", HumanSupportAccessSourceSql.Sources.Select(source =>
                $"(SELECT jsonb_agg(to_jsonb(t) ORDER BY to_jsonb(t)::text) FROM {Sql.Table(source.Type)} t)")) + ")::text");

        internal Task<string> CatalogAsync() => ScalarAsync<string>(Migration, $"""
            SELECT jsonb_build_object(
                'relations', (SELECT jsonb_agg(to_jsonb(c) ORDER BY c.oid) FROM pg_class c
                    JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = {Literal(Sql.Schema)}),
                'attributes', (SELECT jsonb_agg(to_jsonb(a) ORDER BY a.attrelid,a.attnum) FROM pg_attribute a
                    JOIN pg_class c ON c.oid = a.attrelid JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname = {Literal(Sql.Schema)}),
                'routines', (SELECT jsonb_agg(to_jsonb(p) || jsonb_build_object('stamp',obj_description(p.oid,'pg_proc'))
                    ORDER BY p.oid) FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
                    WHERE n.nspname = {Literal(Sql.Schema)}),
                'triggers', (SELECT jsonb_agg(to_jsonb(t) ORDER BY t.oid) FROM pg_trigger t
                    JOIN pg_class c ON c.oid = t.tgrelid JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname = {Literal(Sql.Schema)}),
                'policies', (SELECT jsonb_agg(to_jsonb(p) || jsonb_build_object('stamp',obj_description(p.oid,'pg_policy'))
                    ORDER BY p.oid) FROM pg_policy p JOIN pg_class c ON c.oid = p.polrelid
                    JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = {Literal(Sql.Schema)}),
                'roles', (SELECT jsonb_agg(to_jsonb(r) || jsonb_build_object('stamp',shobj_description(r.oid,'pg_authid'))
                    ORDER BY r.oid) FROM pg_roles r),
                'memberships', (SELECT jsonb_agg(to_jsonb(m) ORDER BY m.roleid,m.member) FROM pg_auth_members m)
            )::text
            """);

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

        private static DbCommand Command(ExploreDbContext context, string sql)
        {
            var command = context.Database.GetDbConnection().CreateCommand();
            command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandTimeout = 15;
            command.CommandText = sql;
            return command;
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
