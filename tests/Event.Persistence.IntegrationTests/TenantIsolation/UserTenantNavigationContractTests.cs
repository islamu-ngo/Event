using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Schema;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class UserTenantNavigationContractTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Arguments("table-read")]
    [Arguments("write")]
    [Arguments("extra-column")]
    [Arguments("grant-option")]
    [Arguments("missing-column")]
    [Arguments("bypass")]
    [Arguments("schema-create")]
    [Arguments("system-column")]
    [Arguments("system-grant-option")]
    [Arguments("set-only-write")]
    [Arguments("admin-only-write")]
    [Arguments("admin-only-read")]
    [Arguments("nested-admin-read")]
    public async Task ReaderAndInstallerRejectGlobalDriftWithoutRepair(string drift)
    {
        await using var context = Open();
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            var mapping = Mapping(context);
            string login = Quote(fixture.NativeQueueEnrollment.GlobalLogin);
            string group = Quote("navigation_drift_" + Guid.CreateVersion7().ToString("N"));
            string bridge = Quote("navigation_bridge_" + Guid.CreateVersion7().ToString("N"));
            string change = drift switch
            {
                "table-read" => $"GRANT SELECT ON {mapping.Table} TO {login}",
                "write" => $"GRANT UPDATE ({mapping.Preference}) ON {mapping.Table} TO {login}",
                "extra-column" => $"GRANT SELECT ({mapping.Audit}) ON {mapping.Table} TO {login}",
                "grant-option" => $"GRANT SELECT ({mapping.Id}) ON {mapping.Table} TO {login} WITH GRANT OPTION",
                "missing-column" => $"REVOKE SELECT ({mapping.Id}) ON {mapping.Table} FROM {login}",
                "bypass" => $"ALTER ROLE {login} BYPASSRLS",
                "schema-create" => $"GRANT CREATE ON SCHEMA {mapping.Schema} TO {login}",
                "system-column" => $"GRANT SELECT (xmin) ON {mapping.Table} TO {login}",
                "system-grant-option" => $"GRANT SELECT (xmin) ON {mapping.Table} TO {login} WITH GRANT OPTION",
                "set-only-write" => $"CREATE ROLE {group}; GRANT UPDATE ({mapping.Preference}) ON {mapping.Table} TO {group}; GRANT {group} TO {login} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE",
                "admin-only-write" => $"CREATE ROLE {group}; GRANT UPDATE ({mapping.Preference}) ON {mapping.Table} TO {group}; GRANT {group} TO {login} WITH INHERIT FALSE, SET FALSE, ADMIN TRUE",
                "admin-only-read" => $"CREATE ROLE {group}; GRANT SELECT ({mapping.Id}) ON {mapping.Table} TO {group}; GRANT {group} TO {login} WITH INHERIT FALSE, SET FALSE, ADMIN TRUE",
                "nested-admin-read" => $"CREATE ROLE {group}; CREATE ROLE {bridge}; GRANT SELECT ({mapping.Id}) ON {mapping.Table} TO {group}; GRANT {group} TO {bridge} WITH INHERIT FALSE, SET FALSE, ADMIN TRUE; GRANT {bridge} TO {login} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE",
                _ => throw new ArgumentOutOfRangeException(nameof(drift))
            };
            await context.Database.ExecuteSqlRawAsync(change);
            await AssertDriftPersistsAsync(context, transaction);
        });
    }

    [Test]
    [Arguments("column")]
    [Arguments("global-membership")]
    [Arguments("delete")]
    [Arguments("truncate")]
    [Arguments("trigger")]
    [Arguments("maintain")]
    [Arguments("system-column")]
    [Arguments("set-only-read")]
    [Arguments("admin-only-read")]
    public async Task ReaderAndInstallerRejectRuntimeAuthorityDrift(string drift)
    {
        await using var context = Open();
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            var mapping = Mapping(context);
            string runtime = Quote(fixture.NativeQueueEnrollment.RuntimeLogin);
            string group = Quote("navigation_runtime_" + Guid.CreateVersion7().ToString("N"));
            string change = drift switch
            {
                "column" => $"GRANT SELECT ({mapping.Id}) ON {mapping.Table} TO {runtime}",
                "global-membership" => $"GRANT {Quote(fixture.NativeQueueEnrollment.GlobalLogin)} TO {runtime} WITH INHERIT FALSE, SET FALSE, ADMIN TRUE",
                "delete" => $"GRANT DELETE ON {mapping.Table} TO {runtime}",
                "truncate" => $"GRANT TRUNCATE ON {mapping.Table} TO {runtime}",
                "trigger" => $"GRANT TRIGGER ON {mapping.Table} TO {runtime}",
                "maintain" => $"GRANT MAINTAIN ON {mapping.Table} TO {runtime}",
                "system-column" => $"GRANT SELECT (xmin) ON {mapping.Table} TO {runtime}",
                "set-only-read" => $"CREATE ROLE {group}; GRANT SELECT ({mapping.Id}) ON {mapping.Table} TO {group}; GRANT {group} TO {runtime} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE",
                "admin-only-read" => $"CREATE ROLE {group}; GRANT SELECT ({mapping.Id}) ON {mapping.Table} TO {group}; GRANT {group} TO {runtime} WITH INHERIT FALSE, SET FALSE, ADMIN TRUE",
                _ => throw new ArgumentOutOfRangeException(nameof(drift))
            };
            await context.Database.ExecuteSqlRawAsync(change);
            await AssertDriftPersistsAsync(context, transaction);
        });
    }

    [Test]
    public async Task CopiedColumnRightsDoNotEnrollAnotherLogin()
    {
        await using var context = Open();
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            var mapping = Mapping(context);
            string copied = Quote("navigation_copy_" + Guid.CreateVersion7().ToString("N"));
            await context.Database.ExecuteSqlRawAsync($"""
                CREATE ROLE {copied} LOGIN;
                GRANT USAGE ON SCHEMA {mapping.Schema} TO {copied};
                GRANT SELECT ({mapping.Id},{mapping.Deleted},{mapping.Preference}) ON {mapping.Table} TO {copied};
                SET LOCAL SESSION AUTHORIZATION {copied};
                """);
            // Column rights alone really allow the projection, but do not identify the enrolled Global reader.
            await context.Users.IgnoreAutoIncludes().AsNoTracking().Select(user => user.Id).Take(1).ToArrayAsync();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                PostgresUserTenantNavigationReadPermissions.RequireReadyAsync(context, fixture.NativeQueueEnrollment));
        });
    }

    [Test]
    public async Task ChangedEnrollmentCannotAdmitOriginalGlobalLogin()
    {
        await using var context = Open();
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            await context.Database.ExecuteSqlRawAsync(
                $"SET LOCAL SESSION AUTHORIZATION {Quote(fixture.NativeQueueEnrollment.GlobalLogin)}");
            var changed = fixture.NativeQueueEnrollment with
            {
                GlobalLogin = fixture.NativeQueueEnrollment.EmailInstanceMutationLogin,
                EmailInstanceMutationLogin = fixture.NativeQueueEnrollment.GlobalLogin
            };
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                PostgresUserTenantNavigationReadPermissions.RequireReadyAsync(context, changed));
        });
    }

    [Test]
    public async Task RoleSubstitutionCannotReplaceOriginalGlobalLogin()
    {
        await using var context = Open();
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            string role = Quote("navigation_role_" + Guid.CreateVersion7().ToString("N"));
            await context.Database.ExecuteSqlRawAsync($"""
                CREATE ROLE {role};
                GRANT {role} TO {Quote(fixture.NativeQueueEnrollment.GlobalLogin)} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE;
                SET LOCAL SESSION AUTHORIZATION {Quote(fixture.NativeQueueEnrollment.GlobalLogin)};
                SET LOCAL ROLE {role};
                """);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                PostgresUserTenantNavigationReadPermissions.RequireReadyAsync(context, fixture.NativeQueueEnrollment));
        });
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task InstallerRequiresOriginalConfiguredMigrator(bool substituteRole)
    {
        await using var context = Open();
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            await context.Database.ExecuteSqlRawAsync(substituteRole
                ? $"SET LOCAL ROLE {Quote(fixture.NativeQueueEnrollment.GlobalLogin)}"
                : $"SET LOCAL SESSION AUTHORIZATION {Quote(fixture.NativeQueueEnrollment.GlobalLogin)}");
            var denied = await Assert.ThrowsAsync<PostgresException>(() =>
                PostgresUserTenantNavigationReadPermissions.ApplyAsync(context, fixture.NativeQueueEnrollment));
            await Assert.That(denied.SqlState).IsEqualTo("42501");
        });
    }

    [Test]
    [Arguments("role")]
    [Arguments("routine")]
    public async Task ObsoleteInstallationIsRejectedWithoutDestructiveCleanup(string obsolete)
    {
        await using var context = Open();
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            string schema = Mapping(context).Schema;
            await context.Database.ExecuteSqlRawAsync(obsolete == "role"
                ? "CREATE ROLE event_user_navigation_owner"
                : $"CREATE FUNCTION {schema}.user_tenant_navigation_read(uuid) RETURNS uuid LANGUAGE sql AS 'SELECT $1'");
            await AssertDriftPersistsAsync(context, transaction);
        });
    }

    [Test]
    public async Task ExplicitMissingEnrollmentIsNotPostgresFallback()
    {
        var factory = new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global));
        var repository = new UserTenantNavigationRepository(factory, null);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => repository.GetNavigationUserAsync(Guid.CreateVersion7(), default));
    }

    [Test]
    public async Task QuotedEnrollmentNamesAreParsedAsValues()
    {
        await using var context = Open();
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            string name = "nav$navigation_permissions$'\\" + Guid.CreateVersion7().ToString("N")[..10];
            await context.Database.ExecuteSqlRawAsync(
                $"ALTER ROLE {Quote(fixture.NativeQueueEnrollment.RuntimeLogin)} RENAME TO {Quote(name)}");
            var enrollment = fixture.NativeQueueEnrollment with { RuntimeLogin = name };
            await PostgresUserTenantNavigationReadPermissions.ApplyAsync(context, enrollment);
            await context.Database.ExecuteSqlRawAsync(
                $"SET LOCAL SESSION AUTHORIZATION {Quote(enrollment.GlobalLogin)}");
            await PostgresUserTenantNavigationReadPermissions.RequireReadyAsync(context, enrollment);
        });
    }

    [Test]
    public async Task RuntimeCannotAcquireDormantTableOwnerAuthorityAfterOrdinaryAclRevocation()
    {
        await using var context = Open();
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            var mapping = Mapping(context);
            string roleName = "navigation_dormant_owner_" + Guid.CreateVersion7().ToString("N");
            string role = Quote(roleName);
            await context.Database.ExecuteSqlRawAsync($"""
                CREATE ROLE {role};
                GRANT CREATE ON SCHEMA {mapping.Schema} TO {role};
                ALTER TABLE {mapping.Table} OWNER TO {role};
                REVOKE CREATE ON SCHEMA {mapping.Schema} FROM {role};
                REVOKE ALL PRIVILEGES ON TABLE {mapping.Table} FROM {role};
                GRANT {role} TO {Quote(fixture.NativeQueueEnrollment.RuntimeLogin)} WITH INHERIT FALSE, SET FALSE, ADMIN TRUE;
                """);
            // This is recoverable owner authority, not an ordinary privilege grant already covered by other cases.
            await using (var command = context.Database.GetDbConnection().CreateCommand())
            {
                command.Transaction = transaction.GetDbTransaction();
                command.CommandText = """
                    SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_class c JOIN pg_catalog.pg_roles r ON r.oid=c.relowner
                        WHERE c.oid=pg_catalog.to_regclass(@table) AND r.rolname=@owner)
                        AND pg_catalog.pg_has_role(@runtime,@owner,'MEMBER')
                        AND NOT pg_catalog.has_table_privilege(@owner,@table,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER,MAINTAIN')
                        AND NOT pg_catalog.has_any_column_privilege(@owner,@table,'SELECT,INSERT,UPDATE,REFERENCES')
                        AND pg_catalog.has_table_privilege(@owner,@table,'SELECT WITH GRANT OPTION')
                    """;
                command.Parameters.Add(new NpgsqlParameter("table", mapping.Table));
                command.Parameters.Add(new NpgsqlParameter("owner", roleName));
                command.Parameters.Add(new NpgsqlParameter("runtime", fixture.NativeQueueEnrollment.RuntimeLogin));
                await Assert.That(await command.ExecuteScalarAsync()).IsEqualTo(true);
            }
            await AssertDriftPersistsAsync(context, transaction);
        });
    }

    [Test]
    public async Task ExactQuotedGlobalEnrollmentRetainsItsDirectColumnGrants()
    {
        await using var context = Open();
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            string name = "Nav$navigation_permissions$'\\" + Guid.CreateVersion7().ToString("N")[..10];
            await context.Database.ExecuteSqlRawAsync(
                $"ALTER ROLE {Quote(fixture.NativeQueueEnrollment.GlobalLogin)} RENAME TO {Quote(name)}");
            var enrollment = fixture.NativeQueueEnrollment with { GlobalLogin = name };
            await PostgresUserTenantNavigationReadPermissions.ApplyAsync(context, enrollment);
            await context.Database.ExecuteSqlRawAsync($"SET LOCAL SESSION AUTHORIZATION {Quote(name)}");
            await PostgresUserTenantNavigationReadPermissions.RequireReadyAsync(context, enrollment);
            await context.Users.IgnoreAutoIncludes().AsNoTracking()
                .Select(user => new { user.Id, user.IsDeleted, user.LastActiveTenantId }).Take(1).ToArrayAsync();
        });
    }

    private async Task AssertDriftPersistsAsync(ExploreDbContext context, IDbContextTransaction transaction)
    {
        await transaction.CreateSavepointAsync("before_reader");
        await context.Database.ExecuteSqlRawAsync(
            $"SET LOCAL SESSION AUTHORIZATION {Quote(fixture.NativeQueueEnrollment.GlobalLogin)}");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            PostgresUserTenantNavigationReadPermissions.RequireReadyAsync(context, fixture.NativeQueueEnrollment));
        await transaction.RollbackToSavepointAsync("before_reader");
        await transaction.CreateSavepointAsync("before_installer");
        var denied = await Assert.ThrowsAsync<PostgresException>(() =>
            PostgresUserTenantNavigationReadPermissions.ApplyAsync(context, fixture.NativeQueueEnrollment));
        await Assert.That(denied.SqlState).IsEqualTo("55000");
        await transaction.RollbackToSavepointAsync("before_installer");
        await context.Database.ExecuteSqlRawAsync(
            $"SET LOCAL SESSION AUTHORIZATION {Quote(fixture.NativeQueueEnrollment.GlobalLogin)}");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            PostgresUserTenantNavigationReadPermissions.RequireReadyAsync(context, fixture.NativeQueueEnrollment));
    }

    private ExploreDbContext Open()
    {
        var builder = new DbContextOptionsBuilder<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder, fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        return new ExploreDbContext(builder.Options);
    }

    private static (string Schema, string Table, string Id, string Deleted, string Preference, string Audit) Mapping(ExploreDbContext context)
    {
        var entity = context.Model.FindEntityType(typeof(User))!;
        var mapping = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        string schema = Quote(entity.GetSchema() ?? "public");
        string Column(string property) => Quote(entity.FindProperty(property)!.GetColumnName(mapping)!);
        return (schema, schema + "." + Quote(entity.GetTableName()!), Column(nameof(User.Id)),
            Column(nameof(User.IsDeleted)), Column(nameof(User.LastActiveTenantId)), Column(nameof(User.CreatedAt)));
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
