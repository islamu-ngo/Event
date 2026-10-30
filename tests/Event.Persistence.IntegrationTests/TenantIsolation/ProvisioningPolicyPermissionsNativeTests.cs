using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

/// <summary>Real login, column and row boundaries for the nonsecret projection, not human authorization.</summary>
[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class ProvisioningPolicyPermissionsNativeTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task GlobalReadsTheCompleteImageAndUpdatesOnlyItsFourMutableColumns()
    {
        await using var scope = await CreateScopeAsync();
        await PostgresProvisioningPolicyPermissions.ApplyAsync(scope.Owner, fixture.NativeQueueEnrollment);
        await using var global = scope.CreateContext(PrimaryDatabaseRole.Global);
        await global.Database.OpenConnectionAsync();
        await global.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await global.Database.BeginTransactionAsync();
            var connection = (NpgsqlConnection)global.Database.GetDbConnection();
            int backend = connection.ProcessID;
            await PostgresProvisioningPolicyPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment);
            var rows = await global.Set<ManagedTenantProvisioningPolicySetting>().AsNoTracking().ToArrayAsync();
            ManagedTenantProvisioningPolicyInventory.RequireComplete(rows);
            int changed = await global.Set<ManagedTenantProvisioningPolicySetting>()
                .Where(row => row.SettingKey == GovernanceSettingKeys.Deployment.Mode)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsPresent, true)
                    .SetProperty(row => row.IsLocked, true).SetProperty(row => row.Value, "\"MultiTenant\"")
                    .SetProperty(row => row.Revision, row => row.Revision + 1));
            await Assert.That(changed).IsEqualTo(1);
            await PostgresProvisioningPolicyPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment);
            await Assert.That(global.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await Assert.That(connection.ProcessID).IsEqualTo(backend);
            await transaction.RollbackAsync();
        });
        await Assert.That(await scope.Owner.Set<ManagedTenantProvisioningPolicySetting>().AsNoTracking()
            .Where(row => row.SettingKey == GovernanceSettingKeys.Deployment.Mode)
            .Select(row => row.Revision).SingleAsync()).IsEqualTo(0L);
    }

    [Test]
    public async Task EmailInstanceSeesExactlyEightLockOnlyRowsAndCannotTouchOtherPolicy()
    {
        await using var scope = await CreateScopeAsync();
        await PostgresProvisioningPolicyPermissions.ApplyAsync(scope.Owner, fixture.NativeQueueEnrollment);
        await using var email = scope.CreateContext(PrimaryDatabaseRole.EmailPolicyInstanceMutation);
        await email.Database.OpenConnectionAsync();
        await PostgresProvisioningPolicyPermissions.RequireCurrentAsync(email, fixture.NativeQueueEnrollment);
        var rows = await email.Set<ManagedTenantProvisioningPolicySetting>().AsNoTracking()
            .OrderBy(row => row.SettingKey).ToArrayAsync();
        string[] expected = EmailSettingDefinitions.All.Select(row => row.Key).Order(StringComparer.Ordinal).ToArray();
        await Assert.That(expected.Length).IsEqualTo(8);
        await Assert.That(rows.Select(row => row.SettingKey).SequenceEqual(expected, StringComparer.Ordinal)).IsTrue();
        await Assert.That(rows.All(row => row.Value is null)).IsTrue();
        await email.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await email.Database.BeginTransactionAsync();
            await Assert.That(await email.Set<ManagedTenantProvisioningPolicySetting>()
                .Where(row => row.SettingKey == EmailSettingDefinitions.SmtpHost.Key)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsPresent, true)
                    .SetProperty(row => row.IsLocked, true).SetProperty(row => row.Revision, row => row.Revision + 1)))
                .IsEqualTo(1);
            await Assert.That(await email.Set<ManagedTenantProvisioningPolicySetting>()
                .Where(row => row.SettingKey == GovernanceSettingKeys.Deployment.Mode)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsLocked, true))).IsEqualTo(0);
            await transaction.RollbackAsync();
        });
        await DeniedAsync(email, $"UPDATE {scope.Table} SET value=NULL WHERE setting_key=@key",
            new NpgsqlParameter("key", EmailSettingDefinitions.SmtpHost.Key));
    }

    [Test]
    public async Task InstalledAclContainsOnlyTheNineteenDeclaredNonownerColumnPrivileges()
    {
        await using var scope = await CreateScopeAsync();
        await PostgresProvisioningPolicyPermissions.ApplyAsync(scope.Owner, fixture.NativeQueueEnrollment);
        string[] read = ["id", "setting_key", "is_present", "is_locked", "value", "revision"];
        string[] expected = [
            .. read.Select(column => $"global:{column}:SELECT"),
            .. new[] { "is_present", "is_locked", "value", "revision" }.Select(column => $"global:{column}:UPDATE"),
            .. read.Select(column => $"email:{column}:SELECT"),
            .. new[] { "is_present", "is_locked", "revision" }.Select(column => $"email:{column}:UPDATE")
        ];
        await using var command = scope.Owner.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            WITH source AS (SELECT oid,relowner,relacl FROM pg_catalog.pg_class WHERE oid=pg_catalog.to_regclass(@table)),
            actual AS (
                SELECT acl.*,a.attname::text AS column_name FROM source s
                JOIN pg_catalog.pg_attribute a ON a.attrelid=s.oid
                CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl WHERE NOT a.attisdropped
                UNION ALL
                SELECT acl.*,NULL::text FROM source s CROSS JOIN LATERAL pg_catalog.aclexplode(s.relacl) acl)
            SELECT COALESCE(string_agg(
                CASE WHEN r.rolname=@global THEN 'global' WHEN r.rolname=@email THEN 'email' ELSE COALESCE(r.rolname,'PUBLIC') END
                || ':' || COALESCE(a.column_name,'TABLE') || ':' || a.privilege_type
                || CASE WHEN a.is_grantable OR a.grantor<>s.relowner THEN ':INVALID_GRANT' ELSE '' END,
                '|' ORDER BY
                (CASE WHEN r.rolname=@global THEN 'global' WHEN r.rolname=@email THEN 'email' ELSE COALESCE(r.rolname,'PUBLIC') END) COLLATE "C",
                a.column_name COLLATE "C",a.privilege_type COLLATE "C"),'')
            FROM actual a CROSS JOIN source s LEFT JOIN pg_catalog.pg_roles r ON r.oid=a.grantee
            WHERE a.grantee<>s.relowner
            """;
        command.Parameters.Add(new NpgsqlParameter("table", scope.Table));
        command.Parameters.Add(new NpgsqlParameter("global", fixture.NativeQueueEnrollment.GlobalLogin));
        command.Parameters.Add(new NpgsqlParameter("email", fixture.NativeQueueEnrollment.EmailInstanceMutationLogin));
        await Assert.That(await command.ExecuteScalarAsync())
            .IsEqualTo(string.Join('|', expected.Order(StringComparer.Ordinal)));
    }

    [Test]
    [Arguments(PrimaryDatabaseRole.Runtime)]
    [Arguments(PrimaryDatabaseRole.EmailPolicyTenantMutation)]
    public async Task UnapprovedLoginsCannotReadOrWriteProjection(PrimaryDatabaseRole role)
    {
        await using var scope = await CreateScopeAsync();
        await PostgresProvisioningPolicyPermissions.ApplyAsync(scope.Owner, fixture.NativeQueueEnrollment);
        await using var caller = scope.CreateContext(role);
        await caller.Database.OpenConnectionAsync();
        await DeniedAsync(caller, $"SELECT id FROM {scope.Table}");
        await DeniedAsync(caller, $"UPDATE {scope.Table} SET revision=revision+1");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            PostgresProvisioningPolicyPermissions.RequireCurrentAsync(caller, fixture.NativeQueueEnrollment));
    }

    [Test]
    [Arguments("insert")]
    [Arguments("delete")]
    [Arguments("id")]
    [Arguments("key")]
    [Arguments("xmin")]
    public async Task GlobalCannotAcquireUndeclaredTableOrColumnAuthority(string operation)
    {
        await using var scope = await CreateScopeAsync();
        await PostgresProvisioningPolicyPermissions.ApplyAsync(scope.Owner, fixture.NativeQueueEnrollment);
        await using var global = scope.CreateContext(PrimaryDatabaseRole.Global);
        await global.Database.OpenConnectionAsync();
        string sql = operation switch
        {
            "insert" => $"INSERT INTO {scope.Table}(id,setting_key,is_present,is_locked,value,revision) VALUES (@id,'unknown',false,false,NULL,0)",
            "delete" => $"DELETE FROM {scope.Table}",
            "id" => $"UPDATE {scope.Table} SET id=id",
            "key" => $"UPDATE {scope.Table} SET setting_key=setting_key",
            "xmin" => $"SELECT xmin FROM {scope.Table}",
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
        await DeniedAsync(global, sql, new NpgsqlParameter("id", Guid.CreateVersion7()));
    }

    [Test]
    public async Task InstallationIsAtomicOnTheOriginalCallerTransactionAndExactlyReapplicable()
    {
        await using var scope = await CreateScopeAsync();
        string before = await CatalogueAsync(scope.Owner, scope.Table);
        await scope.Owner.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await scope.Owner.Database.BeginTransactionAsync();
            var connection = (NpgsqlConnection)scope.Owner.Database.GetDbConnection();
            int backend = connection.ProcessID;
            await PostgresProvisioningPolicyPermissions.ApplyAsync(scope.Owner, fixture.NativeQueueEnrollment);
            string installed = await CatalogueAsync(scope.Owner, scope.Table);
            await Assert.That(installed == before).IsFalse();
            await PostgresProvisioningPolicyPermissions.ApplyAsync(scope.Owner, fixture.NativeQueueEnrollment);
            await Assert.That(await CatalogueAsync(scope.Owner, scope.Table)).IsEqualTo(installed);
            await Assert.That(scope.Owner.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await Assert.That(connection.ProcessID).IsEqualTo(backend);
            await transaction.RollbackAsync();
        });
        await Assert.That(await CatalogueAsync(scope.Owner, scope.Table)).IsEqualTo(before);
    }

    [Test]
    [Arguments("missing")]
    [Arguments("table")]
    [Arguments("grant_option")]
    [Arguments("public")]
    [Arguments("email_value")]
    [Arguments("rls")]
    [Arguments("borrow_global")]
    [Arguments("borrow_owner")]
    public async Task InstalledPrivilegeDriftIsRejectedWithoutRepair(string drift)
    {
        await using var scope = await CreateScopeAsync();
        await PostgresProvisioningPolicyPermissions.ApplyAsync(scope.Owner, fixture.NativeQueueEnrollment);
        string global = Quote(fixture.NativeQueueEnrollment.GlobalLogin);
        string email = Quote(fixture.NativeQueueEnrollment.EmailInstanceMutationLogin);
        string alteration = drift switch
        {
            "missing" => $"REVOKE UPDATE(revision) ON {scope.Table} FROM {global}",
            "table" => $"GRANT SELECT ON {scope.Table} TO {global}",
            "grant_option" => $"GRANT SELECT(id) ON {scope.Table} TO {global} WITH GRANT OPTION",
            "public" => $"GRANT SELECT(id) ON {scope.Table} TO PUBLIC",
            "email_value" => $"GRANT UPDATE(value) ON {scope.Table} TO {email}",
            "rls" => $"ALTER TABLE {scope.Table} DISABLE ROW LEVEL SECURITY",
            "borrow_global" => $"GRANT {global} TO {Quote(fixture.NativeQueueEnrollment.RuntimeLogin)} WITH INHERIT FALSE, SET FALSE, ADMIN TRUE",
            "borrow_owner" => $"GRANT {Quote(fixture.NativeQueueEnrollment.MigratorLogin)} TO {global} WITH INHERIT FALSE, SET FALSE, ADMIN TRUE",
            _ => throw new ArgumentOutOfRangeException(nameof(drift))
        };
        await scope.Owner.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await scope.Owner.Database.BeginTransactionAsync();
            await scope.Owner.Database.ExecuteSqlRawAsync(alteration);
            string damaged = await CatalogueAsync(scope.Owner, scope.Table);
            var denied = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PostgresProvisioningPolicyPermissions.ApplyAsync(scope.Owner, fixture.NativeQueueEnrollment));
            await Assert.That(denied.Message).IsEqualTo("provisioning_policy_permissions_drift");
            await Assert.That(await CatalogueAsync(scope.Owner, scope.Table)).IsEqualTo(damaged);
            await Assert.That(scope.Owner.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await transaction.RollbackAsync();
        });
    }

    [Test]
    public async Task InstallerRejectsTheConfiguredGlobalIdentityWithoutIssuingGrants()
    {
        await using var scope = await CreateScopeAsync();
        string before = await CatalogueAsync(scope.Owner, scope.Table);
        await using var global = scope.CreateContext(PrimaryDatabaseRole.Global);
        var denied = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            PostgresProvisioningPolicyPermissions.ApplyAsync(global, fixture.NativeQueueEnrollment));
        await Assert.That(denied.Message).IsEqualTo("provisioning_policy_installer_wrong_identity");
        await Assert.That(await CatalogueAsync(scope.Owner, scope.Table)).IsEqualTo(before);
    }

    [Test]
    public async Task OrdinaryMigratorMaintainsTheSeedImageUnderForcedRlsWithoutDeleteAuthority()
    {
        string login = "policy_migrator_" + Guid.CreateVersion7().ToString("N");
        string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var options = fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator) with { Username = login, Password = password };
        await using var administrator = new NpgsqlConnection(PrimaryDatabaseConfiguration.BuildConnectionString(
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator)).ConnectionString);
        await administrator.OpenAsync();
        await using (var create = administrator.CreateCommand())
        {
            create.CommandText = $"CREATE ROLE {Quote(login)} LOGIN NOSUPERUSER NOBYPASSRLS NOINHERIT NOCREATEROLE NOCREATEDB NOREPLICATION PASSWORD '{password}'";
            await create.ExecuteNonQueryAsync();
        }
        try
        {
            await using var scope = await CreateScopeAsync(options, initialize: false);
            var enrollment = fixture.NativeQueueEnrollment with { MigratorLogin = login };
            await PostgresProvisioningPolicyPermissions.ApplyAsync(scope.Owner, enrollment);
            await using (var proof = scope.Owner.Database.GetDbConnection().CreateCommand())
            {
                proof.CommandText = """
                    SELECT session_user=@login AND current_user=session_user
                        AND NOT r.rolsuper AND NOT r.rolbypassrls AND NOT r.rolcreatedb AND NOT r.rolcreaterole
                        AND c.relowner=r.oid AND c.relrowsecurity AND c.relforcerowsecurity
                    FROM pg_catalog.pg_roles r CROSS JOIN pg_catalog.pg_class c
                    WHERE r.rolname=session_user AND c.oid=pg_catalog.to_regclass(@table)
                    """;
                proof.Parameters.Add(new NpgsqlParameter("login", login));
                proof.Parameters.Add(new NpgsqlParameter("table", scope.Table));
                await Assert.That(await proof.ExecuteScalarAsync()).IsEqualTo(true);
            }
            await scope.Owner.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                scope.Owner.ChangeTracker.Clear();
                await using var transaction = await scope.Owner.Database.BeginTransactionAsync();
                var connection = (NpgsqlConnection)scope.Owner.Database.GetDbConnection();
                int backend = connection.ProcessID;
                await scope.Owner.SaveProvisioningPolicyInitializationAsync(
                    ManagedTenantProvisioningPolicyInventory.Keys.Select(ManagedTenantProvisioningPolicySetting.CreateAbsent).ToArray(),
                    CancellationToken.None);
                await Assert.That(await scope.Owner.Set<ManagedTenantProvisioningPolicySetting>()
                    .Where(row => row.SettingKey == GovernanceSettingKeys.Deployment.Mode)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsPresent, true)
                        .SetProperty(row => row.IsLocked, true).SetProperty(row => row.Value, "\"MultiTenant\"")
                        .SetProperty(row => row.Revision, 1L))).IsEqualTo(1);
                // A table owner retains DDL and column ACL authority; RLS denies DELETE, not column ownership.
                await Assert.That(await scope.Owner.Set<ManagedTenantProvisioningPolicySetting>().ExecuteDeleteAsync()).IsEqualTo(0);
                await Assert.That(await scope.Owner.Set<ManagedTenantProvisioningPolicySetting>().CountAsync())
                    .IsEqualTo(ManagedTenantProvisioningPolicyInventory.Keys.Length);
                await Assert.That(scope.Owner.Database.CurrentTransaction).IsSameReferenceAs(transaction);
                await Assert.That(connection.ProcessID).IsEqualTo(backend);
                await transaction.RollbackAsync();
            });
            await Assert.That(await scope.Owner.Set<ManagedTenantProvisioningPolicySetting>().CountAsync()).IsEqualTo(0);
        }
        finally
        {
            await using var drop = administrator.CreateCommand();
            drop.CommandText = "DROP ROLE " + Quote(login);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private async Task<Scope> CreateScopeAsync(PrimaryDatabaseConnectionOptions? migration = null, bool initialize = true)
    {
        string schema = "policy_permissions_" + Guid.CreateVersion7().ToString("N");
        if (migration is not null)
        {
            await using var administrator = new NpgsqlConnection(PrimaryDatabaseConfiguration.BuildConnectionString(
                fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator)).ConnectionString);
            await administrator.OpenAsync();
            await using var create = administrator.CreateCommand();
            create.CommandText = "CREATE SCHEMA " + Quote(schema) + " AUTHORIZATION " + Quote(migration.Username!);
            await create.ExecuteNonQueryAsync();
        }
        var scope = new Scope(fixture, schema, migration);
        try
        {
            await scope.Owner.Database.OpenConnectionAsync();
            IModel model = scope.Owner.GetService<IDesignTimeModel>().Model;
            var entity = model.FindEntityType(typeof(ManagedTenantProvisioningPolicySetting))!;
            await Assert.That(entity.GetSchema()).IsEqualTo(schema);
            string tableName = entity.GetTableName()!;
            MigrationOperation[] operations = scope.Owner.GetService<IMigrationsModelDiffer>()
                .GetDifferences(null, model.GetRelationalModel())
                .Where(operation => operation is EnsureSchemaOperation ensure && ensure.Name == schema
                    || operation is CreateTableOperation table && table.Name == tableName && table.Schema == schema
                    || operation is CreateIndexOperation index && index.Table == tableName && index.Schema == schema)
                .ToArray();
            await Assert.That(operations.OfType<CreateTableOperation>().Count()).IsEqualTo(1);
            foreach (var command in scope.Owner.GetService<IMigrationsSqlGenerator>().Generate(operations, model))
                await command.ExecuteNonQueryAsync(scope.Owner.GetService<IRelationalConnection>());
            if (!initialize) return scope;
            await scope.Owner.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                scope.Owner.ChangeTracker.Clear();
                await using var transaction = await scope.Owner.Database.BeginTransactionAsync();
                var existing = await scope.Owner.Set<ManagedTenantProvisioningPolicySetting>()
                    .AsNoTracking().ToArrayAsync();
                if (existing.Length == 0)
                    await scope.Owner.SaveProvisioningPolicyInitializationAsync(
                        ManagedTenantProvisioningPolicyInventory.Keys.Select(ManagedTenantProvisioningPolicySetting.CreateAbsent).ToArray(),
                        CancellationToken.None);
                else
                    ManagedTenantProvisioningPolicyInventory.RequireComplete(existing);
                await transaction.CommitAsync();
                scope.Owner.ChangeTracker.Clear();
            });
            return scope;
        }
        catch
        {
            await scope.DisposeAsync();
            throw;
        }
    }

    private static async Task DeniedAsync(ExploreDbContext context, string sql, params NpgsqlParameter[] parameters)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        foreach (var parameter in parameters) command.Parameters.Add(parameter);
        var denied = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        await Assert.That(denied.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
    }

    private static async Task<string> CatalogueAsync(ExploreDbContext context, string table)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = """
            SELECT jsonb_build_object('owner',c.relowner,'acl',c.relacl::text,'rls',c.relrowsecurity,
                'force',c.relforcerowsecurity,
                'columns',(SELECT jsonb_agg(jsonb_build_array(a.attnum,a.attacl::text) ORDER BY a.attnum)
                    FROM pg_catalog.pg_attribute a WHERE a.attrelid=c.oid AND a.attnum>0 AND NOT a.attisdropped),
                'policies',(SELECT jsonb_agg(jsonb_build_array(p.polname,p.polcmd,p.polpermissive,
                    p.polroles::text,p.polqual::text,p.polwithcheck::text,
                    pg_catalog.obj_description(p.oid,'pg_policy')) ORDER BY p.polname)
                    FROM pg_catalog.pg_policy p WHERE p.polrelid=c.oid))::text
            FROM pg_catalog.pg_class c WHERE c.oid=pg_catalog.to_regclass(@table)
            """;
        command.Parameters.Add(new NpgsqlParameter("table", table));
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static string Quote(string identifier)
    {
        using var builder = new NpgsqlCommandBuilder();
        return builder.QuoteIdentifier(identifier);
    }

    private sealed class Scope(PostgreSqlContainerFixture fixture, string schema, PrimaryDatabaseConnectionOptions? migration = null) : IAsyncDisposable
    {
        public ExploreDbContext Owner { get; } = Build(fixture, schema, PrimaryDatabaseRole.Migrator, migration);
        public string Table => Quote(schema) + "." + Quote(
            Owner.Model.FindEntityType(typeof(ManagedTenantProvisioningPolicySetting))!.GetTableName()!);
        public ExploreDbContext CreateContext(PrimaryDatabaseRole role) => Build(fixture, schema, role);

        private static ExploreDbContext Build(PostgreSqlContainerFixture fixture, string schema, PrimaryDatabaseRole role,
            PrimaryDatabaseConnectionOptions? migration = null)
        {
            var options = TestDbContextOptions.Create<ExploreDbContext>();
            PrimaryDatabaseProviderComposition.ConfigureApplication(options,
                migration is null ? fixture.GetNativeQueueOptions(role, schema) : migration with { Schema = schema });
            // Migrator composition normally retains the canonical snapshot schema; this probe owns a model-local schema.
            ((IDbContextOptionsBuilderInfrastructure)options).AddOrUpdateExtension(
                new RelationalNamespaceOptionsExtension(schema, schema));
            var context = new ExploreDbContext(options.Options);
            context.BindTenantLease();
            return context;
        }

        public async ValueTask DisposeAsync()
        {
            string dropOwnedSchema = "DROP SCHEMA IF EXISTS " + Quote(schema) + " CASCADE";
            try { await Owner.Database.ExecuteSqlRawAsync(dropOwnedSchema); }
            finally { await Owner.DisposeAsync(); }
        }
    }
}
