using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
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
public sealed class PostgresHumanPrincipalSourceContractTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Arguments(PrimaryDatabaseRole.Runtime, 1)]
    [Arguments(PrimaryDatabaseRole.Global, 2)]
    [Arguments(PrimaryDatabaseRole.Global, 3)]
    public async Task InstallationAndReinstallationPreserveOriginalLoginAcquisition(PrimaryDatabaseRole role, int purpose)
    {
        await using var database = await PrincipalDatabase.CreateAsync(fixture);
        await database.InstallAsync();
        await using var caller = await database.OpenAsync(role);
        Guid? tenant = role == PrimaryDatabaseRole.Runtime ? database.Tenant : null;
        await database.SetTenantAsync(caller, tenant);
        await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        var facts = await database.ReadAsync(caller, database.Actor, purpose, tenant, acquire: true);

        await Assert.That(facts.GetProperty("q0").GetArrayLength()).IsEqualTo(0);
        await Assert.That(facts.GetProperty("PermissionMappingsExist").GetBoolean()).IsFalse();
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task ObserveReturnsOnlySelectedActorFactsAndTenantPermissionLists()
    {
        await using var database = await PrincipalDatabase.CreateAsync(fixture);
        await database.SeedAsync();
        await using var runtime = await database.OpenAsync(PrimaryDatabaseRole.Runtime);
        await database.SetTenantAsync(runtime, database.Tenant);

        var first = await database.ReadAsync(runtime, database.Actor, 1, database.Tenant);
        await Assert.That(first.GetProperty("q0").EnumerateArray().Single().GetProperty("Id").GetGuid())
            .IsEqualTo(database.Actor);
        // Membership facts intentionally retain both tenants for the selected actor.
        // Permission lists, in contrast, are constrained to the selected tenant.
        await Assert.That(first.GetProperty("q4").GetArrayLength()).IsEqualTo(2);
        await Assert.That(first.GetProperty("q7").GetArrayLength()).IsEqualTo(2);
        await Assert.That(first.GetProperty("q11").EnumerateArray().Single().GetProperty("Value").GetGuid())
            .IsEqualTo(database.Organization);
        await Assert.That(first.GetProperty("q13").EnumerateArray().Single().GetProperty("Value").GetGuid())
            .IsEqualTo(database.Organization);
        await Assert.That(first.GetProperty("PermissionMappingsExist").GetBoolean()).IsFalse();
        await Assert.That(first.TryGetProperty("l0", out _)).IsFalse();

        var other = await database.ReadAsync(runtime, database.OtherActor, 1, database.Tenant);
        await Assert.That(other.GetProperty("q0").EnumerateArray().Single().GetProperty("Id").GetGuid())
            .IsEqualTo(database.OtherActor);
        await Assert.That(other.GetProperty("q4").GetArrayLength()).IsEqualTo(1);
        await Assert.That(other.GetProperty("q11").EnumerateArray().Single().GetProperty("Value").GetGuid())
            .IsEqualTo(database.OtherOrganization);

        await using var global = await database.OpenAsync(PrimaryDatabaseRole.Global);
        var instance = await database.ReadAsync(global, database.Actor, 2, null);
        await Assert.That(instance.GetProperty("q0").GetArrayLength()).IsEqualTo(1);
        await Assert.That(instance.GetProperty("q4").GetArrayLength()).IsEqualTo(2);
        await Assert.That(instance.GetProperty("q11").GetArrayLength()).IsEqualTo(0);
    }

    [Test]
    [Arguments("account")]
    [Arguments("membership")]
    [Arguments("binding")]
    [Arguments("organization")]
    [Arguments("local")]
    [Arguments("setting")]
    public async Task AcquisitionBlocksIndependentOriginalSourceWritesAndRollbackReleasesThem(string source)
    {
        await using var database = await PrincipalDatabase.CreateAsync(fixture);
        await database.SeedAsync();
        await using var caller = await database.OpenAsync(PrimaryDatabaseRole.Runtime);
        await using var writer = await database.OpenAsync(PrimaryDatabaseRole.Migrator);
        await database.SetTenantAsync(caller, database.Tenant);
        await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await database.ReadAsync(caller, database.Actor, 1, database.Tenant, acquire: true);
        await PrincipalDatabase.ExecuteAsync(writer, "SET lock_timeout = '250ms'");
        string before = await database.SourceRowsAsync();

        // Acquisition has completed on an independently opened physical session.
        // The held native anchor, not a scheduler delay, determines the lock conflict.
        var error = await Assert.ThrowsAsync<PostgresException>(() => database.WriteAsync(writer, source));

        await Assert.That(error!.SqlState).IsEqualTo("55P03");
        await Assert.That(await database.SourceRowsAsync()).IsEqualTo(before);
        await transaction.RollbackAsync();
        await database.WriteAsync(writer, source);
        await Assert.That(await database.SourceRowsAsync() == before).IsFalse();
    }

    [Test]
    [Arguments(PrimaryDatabaseRole.Runtime, 2, IsolationLevel.Serializable)]
    [Arguments(PrimaryDatabaseRole.Runtime, 3, IsolationLevel.Serializable)]
    [Arguments(PrimaryDatabaseRole.Global, 1, IsolationLevel.Serializable)]
    [Arguments(PrimaryDatabaseRole.Global, 4, IsolationLevel.Serializable)]
    [Arguments(PrimaryDatabaseRole.Runtime, 1, IsolationLevel.ReadCommitted)]
    [Arguments(PrimaryDatabaseRole.Runtime, 1, IsolationLevel.RepeatableRead)]
    [Arguments(PrimaryDatabaseRole.EmailPolicyInstanceMutation, 1, IsolationLevel.Serializable)]
    public async Task WrongLoginPurposeOrIsolationCannotAcquire(
        PrimaryDatabaseRole role, int purpose, IsolationLevel isolation)
    {
        await using var database = await PrincipalDatabase.CreateAsync(fixture);
        await using var caller = await database.OpenAsync(role);
        Guid? tenant = role != PrimaryDatabaseRole.Global || purpose == 1 ? database.Tenant : null;
        await database.SetTenantAsync(caller, tenant);
        await using var transaction = await caller.Database.BeginTransactionAsync(isolation);

        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            database.ReadAsync(caller, database.Actor, purpose, tenant, acquire: true));

        await Assert.That(error!.SqlState).IsEqualTo("42501");
        if (role != PrimaryDatabaseRole.EmailPolicyInstanceMutation)
            await Assert.That(error.MessageText).IsEqualTo("human_principal_caller_mismatch");
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task RuntimeCannotSubstituteTheOriginalSessionTenant()
    {
        await using var database = await PrincipalDatabase.CreateAsync(fixture);
        await using var caller = await database.OpenAsync(PrimaryDatabaseRole.Runtime);
        await database.SetTenantAsync(caller, database.OtherTenant);

        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            database.ReadAsync(caller, database.Actor, 1, database.Tenant));

        await Assert.That(error!.SqlState).IsEqualTo("42501");
        await Assert.That(error.MessageText).IsEqualTo("human_principal_caller_mismatch");
    }

    [Test]
    [Arguments("column", "human_principal_source_drift")]
    [Arguments("trigger", "human_principal_writer_drift")]
    [Arguments("privilege", "native_privilege_guard_not_ready")]
    [Arguments("body", "human_principal_body_drift")]
    public async Task AdmissionAndReinstallationRejectDriftWithoutRepair(string drift, string message)
    {
        await using var database = await PrincipalDatabase.CreateAsync(fixture);
        string observe = PostgresHumanPrincipalSourceContract.ObserveFunctionSql(database.Migration);
        const string signature = "(uuid,integer,text,integer,uuid,text)";
        const string counterfeit = "BEGIN RETURN '{}'::jsonb; END;";
        string ddl = drift switch
        {
            "column" => $"ALTER TABLE {database.Sql.Table(typeof(User))} ALTER COLUMN {database.Sql.Column(typeof(User), nameof(User.IsDeleted))} DROP NOT NULL",
            "trigger" => $"ALTER TABLE {database.Sql.Table(typeof(User))} DISABLE TRIGGER human_principal_source_write",
            "privilege" => $"GRANT EXECUTE ON FUNCTION {observe}{signature} TO PUBLIC",
            "body" => $"""
                CREATE OR REPLACE FUNCTION {observe}
                    (p_actor uuid,p_provider integer,p_account text,p_purpose integer,p_tenant uuid,p_stamp text)
                    RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                    AS $body${counterfeit}$body$;
                COMMENT ON FUNCTION {observe}{signature} IS
                    'human-principal-source:v1:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(counterfeit)))}';
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(drift))
        };
        await PrincipalDatabase.ExecuteAsync(database.Migration, ddl);
        string before = await database.CatalogAsync();
        await using var runtime = await database.OpenAsync(PrimaryDatabaseRole.Runtime);

        var admission = await Assert.ThrowsAsync<PostgresException>(() =>
            PrincipalDatabase.ExecuteAsync(runtime,
                PostgresHumanPrincipalSourceContract.AdmissionSql(database.Migration, fixture.NativeQueueEnrollment)));
        await Assert.That(admission!.SqlState).IsEqualTo("55000");
        await Assert.That(admission.MessageText).IsEqualTo(message);
        var reinstall = await Assert.ThrowsAsync<PostgresException>(database.InstallAsync);
        await Assert.That(reinstall!.SqlState).IsEqualTo("55000");
        await Assert.That(reinstall.MessageText).IsEqualTo(message);
        await Assert.That(await database.CatalogAsync()).IsEqualTo(before);
    }

    [Test]
    [Arguments("role")]
    [Arguments("anchor")]
    [Arguments("routine")]
    [Arguments("trigger")]
    public async Task PartialProvisioningWriterFootprintCannotMasqueradeAsWholeAbsence(string footprint)
    {
        await using var database = await PrincipalDatabase.CreateAsync(fixture);
        string ddl = footprint switch
        {
            "role" => $"CREATE ROLE {PostgresManagedProviderProvisioningContract.OwnerRole} NOLOGIN NOINHERIT",
            "anchor" => $"CREATE TABLE {database.Sql.Name(PostgresManagedProviderProvisioningContract.SourceTable)} (id integer)",
            "routine" => $"""
                CREATE FUNCTION {database.Sql.Name("human_provisioning_changed")}() RETURNS trigger
                    LANGUAGE plpgsql AS $body$BEGIN RETURN NULL; END;$body$
                """,
            "trigger" => $"""
                CREATE FUNCTION {database.Sql.Name("unrelated_writer")}() RETURNS trigger
                    LANGUAGE plpgsql AS $body$BEGIN RETURN NULL; END;$body$;
                CREATE TRIGGER human_provisioning_changed BEFORE INSERT ON {database.Sql.Table(typeof(User))}
                    FOR EACH STATEMENT EXECUTE FUNCTION {database.Sql.Name("unrelated_writer")}()
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(footprint))
        };
        await PrincipalDatabase.ExecuteAsync(database.Migration, ddl);
        string before = await database.CatalogAsync();
        await using var runtime = await database.OpenAsync(PrimaryDatabaseRole.Runtime);

        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            database.ReadAsync(runtime, database.Actor, 2, null));

        await Assert.That(error!.SqlState).IsEqualTo("55000");
        await Assert.That(await database.CatalogAsync()).IsEqualTo(before);
    }

    private sealed class PrincipalDatabase(
        PostgreSqlContainerFixture fixture, PostgreSqlContainer container, ExploreDbContext migration) : IAsyncDisposable
    {
        private static readonly (Type Type, string[] Columns)[] Sources =
            [.. HumanPrincipalSourceSql.Sources, .. HumanPrincipalSourceSql.LocalSources];
        internal ExploreDbContext Migration { get; } = migration;
        internal HumanPrincipalSourceSql Sql { get; } = new(migration);
        internal Guid Actor { get; } = Guid.CreateVersion7();
        internal Guid OtherActor { get; } = Guid.CreateVersion7();
        internal Guid Tenant { get; } = Guid.CreateVersion7();
        internal Guid OtherTenant { get; } = Guid.CreateVersion7();
        internal Guid Organization { get; } = Guid.CreateVersion7();
        internal Guid OtherOrganization { get; } = Guid.CreateVersion7();

        internal static async Task<PrincipalDatabase> CreateAsync(PostgreSqlContainerFixture fixture)
        {
            var migrator = fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator);
            // Principal roles have cluster-wide ownership assertions. A database/schema in
            // the shared cluster would inherit those roles and could poison its authority.
            // Reuse the original injected credentials, but put all DDL in a disposable cluster.
            var container = new PostgreSqlBuilder("postgres:18-alpine")
                .WithDatabase("principal_source_" + Guid.CreateVersion7().ToString("N"))
                .WithUsername(migrator.Username!)
                .WithPassword(migrator.Password!)
                .Build();
            try
            {
                await container.StartAsync();
                var context = CreateContext(fixture, container, PrimaryDatabaseRole.Migrator);
                var database = new PrincipalDatabase(fixture, container, context);
                try
                {
                    await context.Database.OpenConnectionAsync();
                    await database.CreateSourcesAsync();
                    await database.InstallAsync();
                    return database;
                }
                catch
                {
                    await context.DisposeAsync();
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
                PrimaryDatabaseRole.Global, PrimaryDatabaseRole.Runtime,
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
            // Only the native primitive's declared sources are needed. Preserve their mapped
            // columns, candidate keys and internal foreign keys, not the unrelated host graph.
            foreach (var source in Sources)
            {
                var entity = Migration.Model.FindEntityType(source.Type)!;
                string columns = string.Join(",", Properties(entity).Select(property =>
                    $"{Sql.Column(source.Type, property.Name)} {property.GetRelationalTypeMapping().StoreType}"
                    + (property.IsNullable ? "" : " NOT NULL")));
                await ExecuteAsync(Migration, $"CREATE TABLE {Sql.Table(source.Type)} ({columns})");
                foreach (var key in entity.GetKeys())
                    await ExecuteAsync(Migration, $"""
                        ALTER TABLE {Sql.Table(source.Type)} ADD
                            {(key.IsPrimaryKey() ? "PRIMARY KEY" : "UNIQUE")} ({Columns(source.Type, key.Properties)})
                        """);
                foreach (var index in entity.GetIndexes().Where(index => index.IsUnique && index.GetFilter() is null))
                    await ExecuteAsync(Migration, $"""
                        CREATE UNIQUE INDEX ON {Sql.Table(source.Type)} ({Columns(source.Type, index.Properties)})
                        """);
            }
            foreach (var source in Sources)
            {
                foreach (var key in Migration.Model.FindEntityType(source.Type)!.GetForeignKeys()
                    .Where(key => Sources.Any(parent => parent.Type == key.PrincipalEntityType.ClrType)))
                {
                    string delete = key.DeleteBehavior switch
                    {
                        DeleteBehavior.Cascade => "CASCADE",
                        DeleteBehavior.SetNull => "SET NULL",
                        DeleteBehavior.Restrict => "RESTRICT",
                        _ => "NO ACTION"
                    };
                    await ExecuteAsync(Migration, $"""
                        ALTER TABLE {Sql.Table(source.Type)} ADD FOREIGN KEY ({Columns(source.Type, key.Properties)})
                            REFERENCES {Sql.Table(key.PrincipalEntityType.ClrType)}
                            ({Columns(key.PrincipalEntityType.ClrType, key.PrincipalKey.Properties)}) ON DELETE {delete}
                        """);
                }
            }
            foreach (var table in PostgresTenantRowLevelSecurityModel.GetTenantTables(Migration.Model)
                .Where(table => Sources.Any(source => Migration.Model.FindEntityType(source.Type)!.GetTableName() == table.TableName)))
                await ExecuteAsync(Migration, PostgresTenantRowLevelSecurityModel.BuildEnableRlsSql(table, Sql.Schema));
            await ExecuteAsync(Migration, $"""
                GRANT USAGE ON SCHEMA {Sql.Quote(Sql.Schema)} TO
                    {Sql.Quote(fixture.NativeQueueEnrollment.RuntimeLogin)}, {Sql.Quote(fixture.NativeQueueEnrollment.GlobalLogin)};
                """);
        }

        private IEnumerable<IProperty> Properties(IEntityType entity) =>
            entity.GetProperties().Where(property =>
                Sources.Single(source => source.Type == entity.ClrType).Columns.Contains(property.Name)
                || entity.GetKeys().Any(key => key.Properties.Contains(property))
                || entity.GetIndexes().Any(index => index.IsUnique && index.GetFilter() is null && index.Properties.Contains(property))
                || entity.GetForeignKeys().Any(key => Sources.Any(source => source.Type == key.PrincipalEntityType.ClrType)
                    && key.Properties.Contains(property)));

        private string Columns(Type type, IReadOnlyList<IProperty> properties) =>
            string.Join(",", properties.Select(property => Sql.Column(type, property.Name)));

        internal Task InstallAsync() =>
            PostgresHumanPrincipalSourceContract.ApplyAsync(Migration, fixture.NativeQueueEnrollment);

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

        internal async Task<JsonElement> ReadAsync(
            ExploreDbContext context, Guid actor, int purpose, Guid? tenant, bool acquire = false)
        {
            string routine = acquire ? PostgresHumanPrincipalSourceContract.AcquireFunctionSql(Migration)
                : PostgresHumanPrincipalSourceContract.ObserveFunctionSql(Migration);
            await using var command = Command(context, $"SELECT {routine}(@actor,NULL,NULL,@purpose,@tenant,NULL)");
            command.Parameters.Add(new NpgsqlParameter("actor", NpgsqlDbType.Uuid) { Value = actor });
            command.Parameters.Add(new NpgsqlParameter("purpose", NpgsqlDbType.Integer) { Value = purpose });
            command.Parameters.Add(new NpgsqlParameter("tenant", NpgsqlDbType.Uuid) { Value = (object?)tenant ?? DBNull.Value });
            using var document = JsonDocument.Parse((string)(await command.ExecuteScalarAsync())!);
            return document.RootElement.Clone();
        }

        internal async Task SeedAsync()
        {
            await InsertAsync(Migration, typeof(User), (nameof(User.Id), Actor), (nameof(User.IsDeleted), false));
            await InsertAsync(Migration, typeof(User), (nameof(User.Id), OtherActor), (nameof(User.IsDeleted), false));
            await InsertAsync(Migration, typeof(TenantStatus), ("Id", 1), ("IsActiveState", true));
            await InsertAsync(Migration, typeof(Tenant), ("Id", Tenant), ("TenantStatusId", 1));
            await InsertAsync(Migration, typeof(Tenant), ("Id", OtherTenant), ("TenantStatusId", 1));
            await InsertAsync(Migration, typeof(Role), ("Id", (int)RoleEnum.OrgAdmin));
            foreach (var pair in new[] { (Actor, Tenant), (Actor, OtherTenant), (OtherActor, Tenant) })
                await InsertAsync(Migration, typeof(TenantUser), ("UserId", pair.Item1), ("TenantId", pair.Item2));
            foreach (var row in new[]
            {
                (Actor, Tenant, Organization),
                (Actor, OtherTenant, Guid.CreateVersion7()),
                (OtherActor, Tenant, OtherOrganization)
            })
            {
                Guid placement = Guid.CreateVersion7();
                await InsertAsync(Migration, typeof(Organization), ("Id", row.Item3), ("IsDeleted", false));
                await InsertAsync(Migration, typeof(OrganizationTenant), ("Id", placement), ("TenantId", row.Item2),
                    ("OrganizationId", row.Item3), ("IsDeleted", false));
                await InsertAsync(Migration, typeof(OrganizationMember), ("UserId", row.Item1), ("TenantId", row.Item2),
                    ("OrganizationTenantId", placement), ("RoleId", (int)RoleEnum.OrgAdmin), ("IsDeleted", false));
            }
        }

        internal Task WriteAsync(ExploreDbContext context, string source) => source switch
        {
            "account" => ExecuteAsync(context,
                $"UPDATE {Sql.Table(typeof(User))} SET {Sql.Column(typeof(User), nameof(User.IsDeleted))} = true"),
            "membership" => InsertAsync(context, typeof(TenantUser), ("UserId", OtherActor), ("TenantId", OtherTenant)),
            "binding" => InsertAsync(context, typeof(UserExternalLogin), ("UserId", Actor),
                ("AuthenticationProviderId", (int)AuthenticationProviderKind.Local), ("ProviderKey", Actor.ToString("D"))),
            "organization" => ExecuteAsync(context,
                $"UPDATE {Sql.Table(typeof(OrganizationMember))} SET {Sql.Column(typeof(OrganizationMember), "IsDeleted")} = true"),
            "local" => InsertAsync(context, typeof(Explore.Persistence.Identity.LocalIdentityUser), ("Id", Actor)),
            "setting" => InsertAsync(context, typeof(SystemSetting),
                ("SettingKey", GovernanceSettingKeys.Email.DeliveryEnabled), ("Value", "true")),
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };

        private async Task InsertAsync(ExploreDbContext context, Type type, params (string Name, object Value)[] values)
        {
            var supplied = values.ToDictionary(value => value.Name, value => value.Value);
            var properties = Properties(Migration.Model.FindEntityType(type)!)
                .Where(property => !property.IsNullable || supplied.ContainsKey(property.Name)).ToArray();
            await using var command = Command(context, $"""
                INSERT INTO {Sql.Table(type)} ({string.Join(",", properties.Select(property => Sql.Column(type, property.Name)))})
                VALUES ({string.Join(",", properties.Select((_, index) => "@p" + index))})
                """);
            for (int index = 0; index < properties.Length; index++)
            {
                var property = properties[index];
                object value;
                if (!supplied.TryGetValue(property.Name, out value!))
                {
                    Type storage = property.GetRelationalTypeMapping().Converter?.ProviderClrType ?? property.ClrType;
                    storage = Nullable.GetUnderlyingType(storage) ?? storage;
                    value = storage == typeof(Guid) ? Guid.CreateVersion7()
                        : storage == typeof(string) ? Guid.CreateVersion7().ToString("N")
                        : storage == typeof(bool) ? false
                        : storage == typeof(DateTime) ? DateTime.UnixEpoch
                        : storage == typeof(long) ? 1L : 1;
                }
                command.Parameters.Add(new NpgsqlParameter("p" + index, value));
            }
            await command.ExecuteNonQueryAsync();
        }

        internal Task<string> SourceRowsAsync() => ScalarAsync<string>(Migration,
            "SELECT pg_catalog.jsonb_build_array(" + string.Join(",", Sources.Select(source =>
                $"(SELECT COALESCE(jsonb_agg(to_jsonb(t) ORDER BY to_jsonb(t)::text),'[]'::jsonb) FROM {Sql.Table(source.Type)} t)"))
            + ")::text");

        internal Task<string> CatalogAsync() => ScalarAsync<string>(Migration, $"""
            SELECT pg_catalog.jsonb_build_object(
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
                'roles', (SELECT jsonb_agg(to_jsonb(r) ORDER BY r.oid) FROM pg_roles r),
                'memberships', (SELECT jsonb_agg(to_jsonb(m) ORDER BY m.roleid,m.member) FROM pg_auth_members m)
            )::text
            """);

        internal static async Task ExecuteAsync(ExploreDbContext context, string sql)
        {
            await using var command = Command(context, sql);
            await command.ExecuteNonQueryAsync();
        }

        private static async Task<T> ScalarAsync<T>(ExploreDbContext context, string sql)
        {
            await using var command = Command(context, sql);
            return (T)(await command.ExecuteScalarAsync())!;
        }

        private static DbCommand Command(ExploreDbContext context, string sql)
        {
            var command = context.Database.GetDbConnection().CreateCommand();
            command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText = sql;
            command.CommandTimeout = 15;
            return command;
        }

        private static ExploreDbContext CreateContext(
            PostgreSqlContainerFixture fixture, PostgreSqlContainer container, PrimaryDatabaseRole role)
        {
            var endpoint = new NpgsqlConnectionStringBuilder(container.GetConnectionString());
            var options = fixture.GetNativeQueueOptions(role) with
            {
                Host = endpoint.Host, Port = endpoint.Port, Database = endpoint.Database
            };
            var builder = TestDbContextOptions.Create<ExploreDbContext>();
            PrimaryDatabaseProviderComposition.ConfigureApplication(builder, options);
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
