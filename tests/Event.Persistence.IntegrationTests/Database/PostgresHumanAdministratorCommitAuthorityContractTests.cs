using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using Testcontainers.PostgreSql;

namespace Event.Persistence.IntegrationTests.Database;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class PostgresHumanAdministratorCommitAuthorityContractTests(PostgreSqlContainerFixture fixture)
{
    private const string Signature = "(uuid,integer,text,integer,uuid)";

    [Test]
    [Arguments(PrimaryDatabaseRole.Runtime, 1, false)]
    [Arguments(PrimaryDatabaseRole.Global, 2, false)]
    [Arguments(PrimaryDatabaseRole.Global, 5, false)]
    [Arguments(PrimaryDatabaseRole.EmailPolicyInstanceMutation, 3, false)]
    [Arguments(PrimaryDatabaseRole.EmailPolicyTenantMutation, 4, false)]
    [Arguments(PrimaryDatabaseRole.Runtime, 1, true)]
    [Arguments(PrimaryDatabaseRole.EmailPolicyTenantMutation, 4, true)]
    public async Task ReinstallationPreservesOriginalLoginAndOwnerTransactionWithoutIamProjection(
        PrimaryDatabaseRole role, int purpose, bool tenantFence)
    {
        await using var database = await AuthorityDatabase.CreateAsync(fixture);
        string before = await database.SourceRowsAsync();
        await database.InstallAsync();
        await Assert.That(await database.SourceRowsAsync()).IsEqualTo(before);
        await using var caller = await database.OpenAsync(role);
        Guid? tenant = role is PrimaryDatabaseRole.Runtime or PrimaryDatabaseRole.EmailPolicyTenantMutation
            ? database.Tenant : null;
        await database.SetTenantAsync(caller, tenant);
        await using var owner = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        await Assert.That(await database.FenceAsync(caller, tenantFence ? "tenant" : "instance",
            database.Actor, purpose, tenant)).IsTrue();
        await Assert.That(await database.FenceAsync(caller, "subject", database.Actor, purpose, tenant)).IsTrue();
        await Assert.That(ReferenceEquals(owner, caller.Database.CurrentTransaction)).IsTrue();
        await owner.RollbackAsync();

        var denied = await Assert.ThrowsAsync<PostgresException>(() =>
            AuthorityDatabase.ExecuteAsync(caller, $"SELECT * FROM {database.Table(typeof(User))}"));
        await Assert.That(denied!.SqlState).IsEqualTo("42501");
    }

    [Test]
    [Arguments("administrator", true)]
    [Arguments("ordinary", false)]
    [Arguments("deleted", false)]
    [Arguments("missing", false)]
    [Arguments("empty", false)]
    [Arguments("unbound", false)]
    [Arguments("different-provider", false)]
    [Arguments("different-subject", false)]
    [Arguments("provider-only", false)]
    [Arguments("account-only", false)]
    [Arguments("empty-account", false)]
    [Arguments("wrong-scope", false)]
    [Arguments("wrong-code", false)]
    public async Task InstanceAuthorityRequiresPersistedAdministratorAndCurrentProviderBinding(string scenario, bool allowed)
    {
        await using var database = await AuthorityDatabase.CreateAsync(fixture);
        Guid actor = scenario == "missing" ? Guid.CreateVersion7() : scenario == "empty" ? Guid.Empty : database.Actor;
        int? provider = (int)AuthenticationProviderKind.Local;
        string? account = database.Account;
        switch (scenario)
        {
            case "ordinary":
                await database.DeleteAsync(typeof(PlatformUserRole));
                break;
            case "deleted":
                await database.UpdateAsync(typeof(User), "IsDeleted", true);
                break;
            case "unbound":
                await database.DeleteAsync(typeof(UserExternalLogin));
                break;
            case "different-provider":
                provider++;
                break;
            case "different-subject":
                actor = database.OtherActor;
                break;
            case "provider-only":
                account = null;
                break;
            case "account-only":
                provider = null;
                break;
            case "empty-account":
                account = "";
                break;
            case "wrong-scope":
                await database.UpdateAsync(typeof(Role), "RoleScopeId", (int)RoleScopeEnum.Tenant,
                    database.PlatformRole);
                break;
            case "wrong-code":
                await database.UpdateAsync(typeof(Role), "MasterCode", "platform.ordinary", database.PlatformRole);
                break;
        }
        await using var caller = await database.OpenAsync(PrimaryDatabaseRole.Global);
        await using var owner = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        await Assert.That(await database.FenceAsync(caller, "instance", actor, 2, null, provider, account))
            .IsEqualTo(allowed);
        await owner.RollbackAsync();
    }

    [Test]
    [Arguments("administrator", true)]
    [Arguments("inactive-tenant", false)]
    [Arguments("suspended-member", false)]
    [Arguments("deleted-member", false)]
    [Arguments("revoked-grant", false)]
    [Arguments("missing-grant", false)]
    [Arguments("other-tenant", false)]
    [Arguments("other-member", false)]
    [Arguments("wrong-role", false)]
    public async Task TenantAuthorityRequiresActiveSameTenantMembershipAndUnrevokedAdministratorGrant(
        string scenario, bool allowed)
    {
        await using var database = await AuthorityDatabase.CreateAsync(fixture);
        Guid tenant = scenario == "other-tenant" ? database.OtherTenant : database.Tenant;
        Guid actor = scenario == "other-member" ? database.OtherActor : database.Actor;
        switch (scenario)
        {
            case "inactive-tenant":
                await database.UpdateAsync(typeof(TenantStatus), "IsActiveState", false);
                break;
            case "suspended-member":
                await database.UpdateAsync(typeof(TenantUser), "StatusId", (int)TenantUserStatusEnum.Suspended);
                break;
            case "deleted-member":
                await database.UpdateAsync(typeof(TenantUser), "IsDeleted", true);
                break;
            case "revoked-grant":
                await database.UpdateAsync(typeof(TenantUserRoleGrant), "RevokedAt", DateTime.UnixEpoch);
                break;
            case "missing-grant":
                await database.DeleteAsync(typeof(TenantUserRoleGrant));
                break;
            case "wrong-role":
                await database.UpdateAsync(typeof(TenantUserRoleGrant), "RoleId", database.OtherRole);
                break;
        }
        await using var caller = await database.OpenAsync(PrimaryDatabaseRole.Runtime);
        await database.SetTenantAsync(caller, tenant);
        await using var owner = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        await Assert.That(await database.FenceAsync(caller, "tenant", actor, 1, tenant)).IsEqualTo(allowed);
        await owner.RollbackAsync();
    }

    [Test]
    [Arguments("tenant")]
    [Arguments("scope")]
    public async Task CompositeForeignKeysRejectCrossTenantAndMismatchedRoleScopeGrants(string mismatch)
    {
        await using var database = await AuthorityDatabase.CreateAsync(fixture);
        var error = await Assert.ThrowsAsync<PostgresException>(() => mismatch == "tenant"
            ? database.UpdateAsync(typeof(TenantUserRoleGrant), "TenantId", database.OtherTenant)
            : database.UpdateAsync(typeof(TenantUserRoleGrant), "RoleScopeId", (int)RoleScopeEnum.Platform));

        await Assert.That(error!.SqlState).IsEqualTo("23503");
        await using var caller = await database.OpenAsync(PrimaryDatabaseRole.Runtime);
        await database.SetTenantAsync(caller, database.OtherTenant);
        await using var owner = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.That(await database.FenceAsync(caller, "tenant", database.Actor, 1, database.OtherTenant)).IsFalse();
        await owner.RollbackAsync();
    }

    [Test]
    [Arguments("ordinary", true)]
    [Arguments("bound", true)]
    [Arguments("deleted", false)]
    [Arguments("unbound", false)]
    [Arguments("missing", false)]
    public async Task SubjectContinuityDoesNotRequireAdministratorButStillRequiresCurrentIdentity(string scenario, bool allowed)
    {
        await using var database = await AuthorityDatabase.CreateAsync(fixture);
        await database.DeleteAsync(typeof(PlatformUserRole));
        await database.DeleteAsync(typeof(TenantUserRoleGrant));
        if (scenario == "deleted")
            await database.UpdateAsync(typeof(User), "IsDeleted", true);
        if (scenario == "unbound")
            await database.DeleteAsync(typeof(UserExternalLogin));
        Guid actor = scenario == "missing" ? Guid.CreateVersion7() : database.Actor;
        int? provider = scenario == "ordinary" ? null : (int)AuthenticationProviderKind.Local;
        string? account = scenario == "ordinary" ? null : database.Account;
        await using var caller = await database.OpenAsync(PrimaryDatabaseRole.EmailPolicyInstanceMutation);
        await using var owner = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        await Assert.That(await database.FenceAsync(caller, "subject", actor, 3, null, provider, account))
            .IsEqualTo(allowed);
        if (allowed)
        {
            await using var writer = await database.OpenAsync(PrimaryDatabaseRole.Migrator);
            var blocked = await Assert.ThrowsAsync<PostgresException>(() => AuthorityDatabase.ExecuteAsync(writer,
                $"SELECT {database.Column(typeof(User), "Id")} FROM {database.Table(typeof(User))} FOR UPDATE NOWAIT"));
            await Assert.That(blocked!.SqlState).IsEqualTo("55P03");
            await owner.RollbackAsync();
            await AuthorityDatabase.ExecuteAsync(writer,
                $"SELECT {database.Column(typeof(User), "Id")} FROM {database.Table(typeof(User))} FOR UPDATE NOWAIT");
            return;
        }
        await owner.RollbackAsync();
    }

    [Test]
    [Arguments(PrimaryDatabaseRole.Runtime, 2, false, IsolationLevel.Serializable)]
    [Arguments(PrimaryDatabaseRole.Global, 1, false, IsolationLevel.Serializable)]
    [Arguments(PrimaryDatabaseRole.Global, 2, true, IsolationLevel.Serializable)]
    [Arguments(PrimaryDatabaseRole.EmailPolicyInstanceMutation, 2, false, IsolationLevel.Serializable)]
    [Arguments(PrimaryDatabaseRole.EmailPolicyInstanceMutation, 3, true, IsolationLevel.Serializable)]
    [Arguments(PrimaryDatabaseRole.EmailPolicyTenantMutation, 1, true, IsolationLevel.Serializable)]
    [Arguments(PrimaryDatabaseRole.EmailPolicyTenantMutation, 3, false, IsolationLevel.Serializable)]
    [Arguments(PrimaryDatabaseRole.Runtime, 0, true, IsolationLevel.Serializable)]
    [Arguments(PrimaryDatabaseRole.Runtime, 1, true, IsolationLevel.ReadCommitted)]
    [Arguments(PrimaryDatabaseRole.Runtime, 1, true, IsolationLevel.RepeatableRead)]
    [Arguments(PrimaryDatabaseRole.Global, 2, false, IsolationLevel.ReadCommitted)]
    public async Task WrongOriginalLoginPurposeOrIsolationCannotAcquire(
        PrimaryDatabaseRole role, int purpose, bool tenantFence, IsolationLevel isolation)
    {
        await using var database = await AuthorityDatabase.CreateAsync(fixture);
        await using var caller = await database.OpenAsync(role);
        Guid? tenant = role is PrimaryDatabaseRole.Runtime or PrimaryDatabaseRole.EmailPolicyTenantMutation
            ? database.Tenant : null;
        await database.SetTenantAsync(caller, tenant);
        await using var owner = await caller.Database.BeginTransactionAsync(isolation);

        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            database.FenceAsync(caller, tenantFence ? "tenant" : "instance", database.Actor, purpose, tenant));

        await Assert.That(error!.SqlState).IsEqualTo("42501");
        await owner.RollbackAsync();
    }

    [Test]
    [Arguments("changed")]
    [Arguments("missing")]
    [Arguments("empty")]
    [Arguments("no-transaction")]
    public async Task OriginalSessionTenantAndSerializableOwnerCannotBeSubstituted(string scenario)
    {
        await using var database = await AuthorityDatabase.CreateAsync(fixture);
        await using var caller = await database.OpenAsync(PrimaryDatabaseRole.Runtime);
        Guid? tenant = scenario == "missing" ? null : scenario == "empty" ? Guid.Empty : database.Tenant;
        await database.SetTenantAsync(caller, scenario == "changed" ? database.OtherTenant : tenant);
        await using var owner = scenario == "no-transaction" ? null
            : await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            database.FenceAsync(caller, "tenant", database.Actor, 1, tenant));

        await Assert.That(error!.SqlState).IsEqualTo("42501");
        await Assert.That(error.MessageText).IsEqualTo("human_administrator_caller_mismatch");
        if (owner is not null)
            await owner.RollbackAsync();
    }

    [Test]
    [Arguments("user", false)]
    [Arguments("binding", false)]
    [Arguments("platform-grant", false)]
    [Arguments("platform-role", false)]
    [Arguments("tenant", true)]
    [Arguments("status", true)]
    [Arguments("member", true)]
    [Arguments("inactive-member", true)]
    [Arguments("grant", true)]
    [Arguments("tenant-role", true)]
    [Arguments("new-binding", false)]
    [Arguments("new-membership", false)]
    [Arguments("new-platform-grant", false)]
    [Arguments("new-tenant-grant", true)]
    public async Task IndependentContributorWritesConflictUntilOriginalOwnerRollsBack(string source, bool tenantFence)
    {
        await using var database = await AuthorityDatabase.CreateAsync(fixture);
        if (source == "inactive-member")
            await database.UpdateAsync(typeof(TenantUser), "StatusId", (int)TenantUserStatusEnum.Suspended);
        if (source == "new-tenant-grant")
            await database.DeleteAsync(typeof(TenantUserRoleGrant));
        await using var caller = await database.OpenAsync(tenantFence
            ? PrimaryDatabaseRole.EmailPolicyTenantMutation : PrimaryDatabaseRole.EmailPolicyInstanceMutation);
        Guid? tenant = tenantFence ? database.Tenant : null;
        await database.SetTenantAsync(caller, tenant);
        await using var owner = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.That(await database.FenceAsync(caller, tenantFence ? "tenant" : "instance",
            database.Actor, tenantFence ? 4 : 3, tenant, (int)AuthenticationProviderKind.Local, database.Account))
            .IsEqualTo(source is not ("inactive-member" or "new-tenant-grant"));
        await using var writer = await database.OpenAsync(PrimaryDatabaseRole.Migrator);
        await AuthorityDatabase.ExecuteAsync(writer, "SET lock_timeout = '250ms'");
        string before = await database.SourceRowsAsync();

        // The acquired native row/FK locks determine the conflict. No scheduler delay is involved.
        var error = await Assert.ThrowsAsync<PostgresException>(() => database.WriteAsync(writer, source));

        await Assert.That(error!.SqlState).IsEqualTo("55P03");
        await Assert.That(await database.SourceRowsAsync()).IsEqualTo(before);
        await owner.RollbackAsync();
        await database.WriteAsync(writer, source);
        await Assert.That(await database.SourceRowsAsync() == before).IsFalse();
    }

    [Test]
    [Arguments("public-execute", "native_privilege_guard_not_ready")]
    [Arguments("column-acl", "native_privilege_guard_not_ready")]
    [Arguments("role-set", "native_privilege_guard_not_ready")]
    [Arguments("body", "human_administrator_routine_drift")]
    [Arguments("self-restamp", "human_administrator_body_drift")]
    [Arguments("column", "human_administrator_source_drift")]
    [Arguments("foreign-key", "human_administrator_fk_drift")]
    [Arguments("deferrable-key", "human_administrator_fk_drift")]
    [Arguments("rls", "human_administrator_tenant_policy_drift")]
    [Arguments("tenant-policy", "human_administrator_tenant_policy_drift")]
    [Arguments("read-policy", "human_administrator_policy_drift")]
    [Arguments("extra-policy", "human_administrator_policy_drift")]
    public async Task AdmissionAndReinstallationRejectExactContractDriftWithoutRepair(string drift, string message)
    {
        await using var database = await AuthorityDatabase.CreateAsync(fixture);
        string function = PostgresHumanAdministratorCommitAuthorityContract.InstanceFunctionSql(database.Migration);
        string member = database.Table(typeof(TenantUser));
        const string counterfeit = "BEGIN RETURN true; END;";
        string ddl = drift switch
        {
            "public-execute" => $"GRANT EXECUTE ON FUNCTION {function}{Signature} TO PUBLIC",
            "column-acl" => $"GRANT SELECT ({database.Column(typeof(User), "Id")}) ON {database.Table(typeof(User))} TO {PostgresHumanAdministratorCommitAuthorityContract.InstanceRole}",
            "role-set" => $"GRANT {PostgresHumanAdministratorCommitAuthorityContract.InstanceRole} TO {AuthorityDatabase.Quote(fixture.NativeQueueEnrollment.EmailInstanceMutationLogin)} WITH SET TRUE",
            "body" or "self-restamp" => $"""
                CREATE OR REPLACE FUNCTION {function}
                    (p_actor uuid,p_provider integer,p_account text,p_purpose integer,p_tenant uuid)
                    RETURNS boolean LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                    AS $body${counterfeit}$body$;
                """ + (drift == "self-restamp" ? $"""
                COMMENT ON FUNCTION {function}{Signature} IS
                    'human-administrator-fence:v1:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(counterfeit)))}';
                """ : ""),
            "column" => $"ALTER TABLE {database.Table(typeof(User))} ALTER COLUMN {database.Column(typeof(User), "IsDeleted")} DROP NOT NULL",
            "foreign-key" => $"ALTER TABLE {database.Table(typeof(UserExternalLogin))} DROP CONSTRAINT authority_binding_user_fk",
            "deferrable-key" => $"ALTER TABLE {database.Table(typeof(UserExternalLogin))} ALTER CONSTRAINT authority_binding_user_fk DEFERRABLE",
            "rls" => $"ALTER TABLE {member} NO FORCE ROW LEVEL SECURITY",
            "tenant-policy" => $"ALTER POLICY {PostgresTenantRowLevelSecurityModel.PolicyName} ON {member} USING (true) WITH CHECK (true)",
            "read-policy" => $"ALTER POLICY human_administrator_fence_read ON {member} USING (false)",
            "extra-policy" => $"CREATE POLICY counterfeit ON {member} FOR SELECT TO {PostgresHumanAdministratorCommitAuthorityContract.OwnerRole} USING (true)",
            _ => throw new ArgumentOutOfRangeException(nameof(drift))
        };
        await AuthorityDatabase.ExecuteAsync(database.Migration, ddl);
        string before = await database.CatalogAsync();
        await using var caller = await database.OpenAsync(PrimaryDatabaseRole.Runtime);

        var admission = await Assert.ThrowsAsync<PostgresException>(() => AuthorityDatabase.ExecuteAsync(caller,
            PostgresHumanAdministratorCommitAuthorityContract.AdmissionSql(database.Migration, fixture.NativeQueueEnrollment)));
        await Assert.That(admission!.SqlState).IsEqualTo("55000");
        await Assert.That(admission.MessageText).IsEqualTo(message);
        var reinstall = await Assert.ThrowsAsync<PostgresException>(database.InstallAsync);
        await Assert.That(reinstall!.SqlState).IsEqualTo("55000");
        await Assert.That(reinstall.MessageText).IsEqualTo(message);
        await Assert.That(await database.CatalogAsync()).IsEqualTo(before);
        await Assert.ThrowsAsync<PostgresException>(() => AuthorityDatabase.ExecuteAsync(caller,
            PostgresHumanAdministratorCommitAuthorityContract.AdmissionSql(database.Migration, fixture.NativeQueueEnrollment)));
    }

    private sealed class AuthorityDatabase(
        PostgreSqlContainerFixture fixture, PostgreSqlContainer container, ExploreDbContext migration) : IAsyncDisposable
    {
        private static readonly (Type Type, string[] Columns)[] Sources =
        [
            (typeof(User), ["Id", "IsDeleted"]),
            (typeof(UserExternalLogin), ["Id", "UserId", "AuthenticationProviderId", "ProviderKey"]),
            (typeof(PlatformUserRole), ["Id", "UserId", "RoleId"]),
            (typeof(Role), ["Id", "RoleScopeId", "MasterCode"]),
            (typeof(Tenant), ["Id", "TenantStatusId"]),
            (typeof(TenantStatus), ["Id", "IsActiveState"]),
            (typeof(TenantUser), ["Id", "TenantId", "UserId", "StatusId", "IsDeleted"]),
            (typeof(TenantUserRoleGrant), ["Id", "TenantId", "TenantUserId", "RoleId", "RoleScopeId", "RevokedAt"])
        ];

        internal ExploreDbContext Migration { get; } = migration;
        internal Guid Actor { get; } = Guid.CreateVersion7();
        internal Guid OtherActor { get; } = Guid.CreateVersion7();
        internal Guid Tenant { get; } = Guid.CreateVersion7();
        internal Guid OtherTenant { get; } = Guid.CreateVersion7();
        private Guid Member { get; } = Guid.CreateVersion7();
        internal int PlatformRole => (int)RoleEnum.Admin;
        internal int OtherRole => (int)RoleEnum.TenantMember;
        internal string Account => Actor.ToString("D");
        private string Schema => Migration.GetService<IDbContextOptions>()
            .FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
            ?? Migration.Model.FindEntityType(typeof(User))!.GetSchema() ?? "public";

        internal static async Task<AuthorityDatabase> CreateAsync(PostgreSqlContainerFixture fixture)
        {
            var original = fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator);
            // Dedicated roles are cluster-wide. Keep original enrolled credentials, not shared-cluster DDL.
            var container = new PostgreSqlBuilder("postgres:18-alpine")
                .WithDatabase("administrator_authority_" + Guid.CreateVersion7().ToString("N"))
                .WithUsername(original.Username!)
                .WithPassword(original.Password!)
                .Build();
            try
            {
                await container.StartAsync();
                var context = CreateContext(fixture, container, PrimaryDatabaseRole.Migrator);
                try
                {
                    await context.Database.OpenConnectionAsync();
                    var database = new AuthorityDatabase(fixture, container, context);
                    await database.CreateSourcesAsync();
                    await database.InstallAsync();
                    await database.SeedAsync();
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
                PrimaryDatabaseRole.Runtime, PrimaryDatabaseRole.Global,
                PrimaryDatabaseRole.EmailPolicyInstanceMutation, PrimaryDatabaseRole.EmailPolicyTenantMutation
            })
            {
                var original = fixture.GetNativeQueueOptions(role);
                await ExecuteAsync(Migration, $"""
                    CREATE ROLE {Quote(original.Username!)} LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE
                        NOINHERIT NOREPLICATION NOBYPASSRLS PASSWORD {Literal(original.Password!)};
                    """);
            }
            await ExecuteAsync(Migration, $"CREATE SCHEMA {Quote(Schema)}");
            // Retain mapped candidate keys and every FK inside the bounded source set.
            // Do not import the managed commit fence or its unrelated hosting/context graph.
            foreach (var source in Sources)
            {
                var entity = Migration.Model.FindEntityType(source.Type)!;
                string columns = string.Join(",", Properties(entity).Select(property =>
                    $"{Column(source.Type, property.Name)} {property.GetRelationalTypeMapping().StoreType}"
                    + (property.IsNullable ? "" : " NOT NULL")));
                await ExecuteAsync(Migration, $"CREATE TABLE {Table(source.Type)} ({columns})");
                foreach (var key in entity.GetKeys())
                    await ExecuteAsync(Migration, $"""
                        ALTER TABLE {Table(source.Type)} ADD {(key.IsPrimaryKey() ? "PRIMARY KEY" : "UNIQUE")}
                            ({Columns(source.Type, key.Properties)})
                        """);
                foreach (var index in entity.GetIndexes().Where(index => index.IsUnique && index.GetFilter() is null))
                    await ExecuteAsync(Migration, $"""
                        CREATE UNIQUE INDEX ON {Table(source.Type)} ({Columns(source.Type, index.Properties)})
                        """);
            }
            foreach (var source in Sources)
            {
                foreach (var key in Migration.Model.FindEntityType(source.Type)!.GetForeignKeys()
                    .Where(key => Sources.Any(parent => parent.Type == key.PrincipalEntityType.ClrType)))
                {
                    string name = source.Type == typeof(UserExternalLogin) && key.PrincipalEntityType.ClrType == typeof(User)
                        ? "CONSTRAINT authority_binding_user_fk " : "";
                    string delete = key.DeleteBehavior switch
                    {
                        DeleteBehavior.Cascade => "CASCADE",
                        DeleteBehavior.SetNull => "SET NULL",
                        DeleteBehavior.Restrict => "RESTRICT",
                        _ => "NO ACTION"
                    };
                    await ExecuteAsync(Migration, $"""
                        ALTER TABLE {Table(source.Type)} ADD {name}FOREIGN KEY ({Columns(source.Type, key.Properties)})
                            REFERENCES {Table(key.PrincipalEntityType.ClrType)}
                            ({Columns(key.PrincipalEntityType.ClrType, key.PrincipalKey.Properties)}) ON DELETE {delete}
                        """);
                }
            }
            foreach (var table in PostgresTenantRowLevelSecurityModel.GetTenantTables(Migration.Model)
                .Where(table => Sources.Any(source => Migration.Model.FindEntityType(source.Type)!.GetTableName() == table.TableName)))
                await ExecuteAsync(Migration, PostgresTenantRowLevelSecurityModel.BuildEnableRlsSql(table, Schema));
        }

        private IEnumerable<IProperty> Properties(IEntityType entity) => entity.GetProperties().Where(property =>
            Sources.Single(source => source.Type == entity.ClrType).Columns.Contains(property.Name)
            || entity.GetKeys().Any(key => key.Properties.Contains(property))
            || entity.GetIndexes().Any(index => index.IsUnique && index.GetFilter() is null && index.Properties.Contains(property))
            || entity.GetForeignKeys().Any(key => Sources.Any(source => source.Type == key.PrincipalEntityType.ClrType)
                && key.Properties.Contains(property)));

        internal string Table(Type type) => Quote(Schema) + "." + Quote(Migration.Model.FindEntityType(type)!.GetTableName()!);
        internal string Column(Type type, string property)
        {
            var entity = Migration.Model.FindEntityType(type)!;
            return Quote(entity.FindProperty(property)!.GetColumnName(
                StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!);
        }

        private string Columns(Type type, IReadOnlyList<IProperty> properties) =>
            string.Join(",", properties.Select(property => Column(type, property.Name)));

        internal Task InstallAsync() =>
            PostgresHumanAdministratorCommitAuthorityContract.ApplyAsync(Migration, fixture.NativeQueueEnrollment);

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

        internal async Task<bool> FenceAsync(ExploreDbContext context, string kind, Guid actor, int purpose,
            Guid? tenant, int? provider = null, string? account = null)
        {
            string function = kind switch
            {
                "instance" => PostgresHumanAdministratorCommitAuthorityContract.InstanceFunctionSql(Migration),
                "tenant" => PostgresHumanAdministratorCommitAuthorityContract.TenantFunctionSql(Migration),
                "subject" => PostgresHumanAdministratorCommitAuthorityContract.SubjectFunctionSql(Migration),
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
            await using var command = Command(context, $"SELECT {function}(@actor,@provider,@account,@purpose,@tenant)");
            command.Parameters.Add(new NpgsqlParameter("actor", NpgsqlDbType.Uuid) { Value = actor });
            command.Parameters.Add(new NpgsqlParameter("provider", NpgsqlDbType.Integer) { Value = (object?)provider ?? DBNull.Value });
            command.Parameters.Add(new NpgsqlParameter("account", NpgsqlDbType.Text) { Value = (object?)account ?? DBNull.Value });
            command.Parameters.Add(new NpgsqlParameter("purpose", NpgsqlDbType.Integer) { Value = purpose });
            command.Parameters.Add(new NpgsqlParameter("tenant", NpgsqlDbType.Uuid) { Value = (object?)tenant ?? DBNull.Value });
            return (bool)(await command.ExecuteScalarAsync())!;
        }

        private async Task SeedAsync()
        {
            await InsertAsync(Migration, typeof(User), ("Id", Actor), ("IsDeleted", false));
            await InsertAsync(Migration, typeof(User), ("Id", OtherActor), ("IsDeleted", false));
            await InsertAsync(Migration, typeof(TenantStatus), ("Id", (int)TenantStatusEnum.Active), ("IsActiveState", true));
            await InsertAsync(Migration, typeof(TenantStatus), ("Id", (int)TenantStatusEnum.Suspended), ("IsActiveState", false));
            await InsertAsync(Migration, typeof(Tenant), ("Id", Tenant), ("TenantStatusId", (int)TenantStatusEnum.Active));
            await InsertAsync(Migration, typeof(Tenant), ("Id", OtherTenant), ("TenantStatusId", (int)TenantStatusEnum.Active));
            await InsertAsync(Migration, typeof(Role), ("Id", PlatformRole),
                ("RoleScopeId", (int)RoleScopeEnum.Platform), ("MasterCode", "platform.admin"));
            await InsertAsync(Migration, typeof(Role), ("Id", (int)RoleEnum.TenantAdmin),
                ("RoleScopeId", (int)RoleScopeEnum.Tenant), ("MasterCode", "tenant.admin"));
            await InsertAsync(Migration, typeof(Role), ("Id", OtherRole),
                ("RoleScopeId", (int)RoleScopeEnum.Tenant), ("MasterCode", "tenant.ordinary"));
            await InsertAsync(Migration, typeof(UserExternalLogin), ("UserId", Actor),
                ("AuthenticationProviderId", (int)AuthenticationProviderKind.Local), ("ProviderKey", Account));
            await InsertAsync(Migration, typeof(PlatformUserRole), ("UserId", Actor), ("RoleId", PlatformRole));
            await InsertAsync(Migration, typeof(TenantUser), ("Id", Member), ("TenantId", Tenant), ("UserId", Actor),
                ("StatusId", (int)TenantUserStatusEnum.Active), ("IsDeleted", false));
            await InsertGrantAsync(Migration);
        }

        private Task InsertGrantAsync(ExploreDbContext context) => InsertAsync(context, typeof(TenantUserRoleGrant),
            ("TenantId", Tenant), ("TenantUserId", Member), ("RoleId", (int)RoleEnum.TenantAdmin),
            ("RoleScopeId", (int)RoleScopeEnum.Tenant));

        internal Task DeleteAsync(Type type) => ExecuteAsync(Migration, $"DELETE FROM {Table(type)}");

        internal async Task UpdateAsync(Type type, string property, object value, object? id = null)
        {
            await using var command = Command(Migration,
                $"UPDATE {Table(type)} SET {Column(type, property)} = @value"
                + (id is null ? "" : $" WHERE {Column(type, "Id")} = @id"));
            command.Parameters.Add(new NpgsqlParameter("value", value));
            if (id is not null)
                command.Parameters.Add(new NpgsqlParameter("id", id));
            await command.ExecuteNonQueryAsync();
        }

        internal Task WriteAsync(ExploreDbContext context, string source)
        {
            if (source == "new-binding")
                return InsertAsync(context, typeof(UserExternalLogin), ("UserId", Actor),
                    ("AuthenticationProviderId", (int)AuthenticationProviderKind.Local), ("ProviderKey", "additional-binding"));
            if (source == "new-membership")
                return InsertAsync(context, typeof(TenantUser), ("TenantId", OtherTenant), ("UserId", Actor),
                    ("StatusId", (int)TenantUserStatusEnum.Active), ("IsDeleted", false));
            if (source == "new-platform-grant")
                return InsertAsync(context, typeof(PlatformUserRole), ("UserId", Actor), ("RoleId", OtherRole));
            if (source == "new-tenant-grant")
                return InsertGrantAsync(context);
            (Type Type, string Property, string Value, object? Id) mutation = source switch
            {
                "user" => (typeof(User), "IsDeleted", "true", Actor),
                "binding" => (typeof(UserExternalLogin), "ProviderKey", "'replacement-binding'", null),
                "platform-grant" => (typeof(PlatformUserRole), "RoleId", OtherRole.ToString(System.Globalization.CultureInfo.InvariantCulture), null),
                "platform-role" => (typeof(Role), "MasterCode", "'platform.changed'", PlatformRole),
                "tenant" => (typeof(Tenant), "TenantStatusId", ((int)TenantStatusEnum.Suspended).ToString(System.Globalization.CultureInfo.InvariantCulture), Tenant),
                "status" => (typeof(TenantStatus), "IsActiveState", "false", null),
                "member" => (typeof(TenantUser), "IsDeleted", "true", Member),
                "inactive-member" => (typeof(TenantUser), "StatusId", ((int)TenantUserStatusEnum.Active).ToString(System.Globalization.CultureInfo.InvariantCulture), Member),
                "grant" => (typeof(TenantUserRoleGrant), "RevokedAt", "CURRENT_TIMESTAMP", null),
                "tenant-role" => (typeof(Role), "MasterCode", "'tenant.changed'", (int)RoleEnum.TenantAdmin),
                _ => throw new ArgumentOutOfRangeException(nameof(source))
            };
            return ExecuteAsync(context,
                $"UPDATE {Table(mutation.Type)} SET {Column(mutation.Type, mutation.Property)} = {mutation.Value}"
                + (mutation.Id is null ? "" : $" WHERE {Column(mutation.Type, "Id")} = {Literal(mutation.Id.ToString()!)}"));
        }

        private async Task InsertAsync(ExploreDbContext context, Type type, params (string Name, object Value)[] values)
        {
            var supplied = values.ToDictionary(value => value.Name, value => value.Value);
            var properties = Properties(Migration.Model.FindEntityType(type)!)
                .Where(property => !property.IsNullable || supplied.ContainsKey(property.Name)).ToArray();
            await using var command = Command(context, $"""
                INSERT INTO {Table(type)} ({string.Join(",", properties.Select(property => Column(type, property.Name)))})
                VALUES ({string.Join(",", properties.Select((_, index) => "@p" + index))})
                """);
            for (int index = 0; index < properties.Length; index++)
            {
                var property = properties[index];
                if (!supplied.TryGetValue(property.Name, out object? value))
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
            "SELECT jsonb_build_array(" + string.Join(",", Sources.Select(source =>
                $"(SELECT COALESCE(jsonb_agg(to_jsonb(t) ORDER BY to_jsonb(t)::text),'[]'::jsonb) FROM {Table(source.Type)} t)"))
            + ")::text");

        internal Task<string> CatalogAsync() => ScalarAsync<string>(Migration, $"""
            SELECT jsonb_build_object(
                'relations', (SELECT jsonb_agg(to_jsonb(c) ORDER BY c.oid) FROM pg_class c
                    JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = {Literal(Schema)}),
                'attributes', (SELECT jsonb_agg(to_jsonb(a) ORDER BY a.attrelid,a.attnum) FROM pg_attribute a
                    JOIN pg_class c ON c.oid = a.attrelid JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname = {Literal(Schema)}),
                'constraints', (SELECT jsonb_agg(to_jsonb(c) ORDER BY c.oid) FROM pg_constraint c
                    JOIN pg_namespace n ON n.oid = c.connamespace WHERE n.nspname = {Literal(Schema)}),
                'routines', (SELECT jsonb_agg(to_jsonb(p) || jsonb_build_object('stamp',obj_description(p.oid,'pg_proc'))
                    ORDER BY p.oid) FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
                    WHERE n.nspname = {Literal(Schema)}),
                'policies', (SELECT jsonb_agg(to_jsonb(p) || jsonb_build_object('stamp',obj_description(p.oid,'pg_policy'))
                    ORDER BY p.oid) FROM pg_policy p JOIN pg_class c ON c.oid = p.polrelid
                    JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = {Literal(Schema)}),
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

        private static async Task<T> ScalarAsync<T>(ExploreDbContext context, string sql)
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

        internal static string Quote(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        private static string Literal(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

        public async ValueTask DisposeAsync()
        {
            try { await Migration.DisposeAsync(); }
            finally { await container.DisposeAsync(); }
        }
    }
}
