using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class ExternalLoginReadEnrollmentTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task EfFirstGlobalReadsOnlyTheRequestedProviderAccountThroughEf()
    {
        Guid subject = Guid.CreateVersion7();
        Guid other = Guid.CreateVersion7();
        Guid selected = Guid.CreateVersion7();
        string account = $"ef-account-{Guid.CreateVersion7():N}";
        await using (var seed = Open())
        {
            seed.Users.AddRange(User(subject), User(other));
            seed.UserExternalLogins.AddRange(
                Binding(selected, subject, AuthenticationProviderKind.Keycloak, account),
                Binding(Guid.CreateVersion7(), other, AuthenticationProviderKind.Local, account),
                Binding(Guid.CreateVersion7(), other, AuthenticationProviderKind.Keycloak, $"other-{account}"));
            await seed.SaveChangesAsync();
        }

        var factory = new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global));
        await using var global = await factory.CreateDbContextAsync();
        UserExternalLogin? binding = await global.UserExternalLogins.AsNoTracking()
            .SingleOrDefaultAsync(row => row.AuthenticationProviderId == (int)AuthenticationProviderKind.Keycloak
                && row.ProviderKey == account);

        await Assert.That(binding?.Id).IsEqualTo(selected);
        await Assert.That(binding!.UserId).IsEqualTo(subject);
        await Assert.That(binding.User).IsNull();
        await Assert.That(global.ChangeTracker.Entries().Any()).IsFalse();
        await Assert.That(await global.UserExternalLogins.AsNoTracking().AnyAsync(row =>
            row.AuthenticationProviderId == (int)AuthenticationProviderKind.Keycloak
                && row.ProviderKey == $"missing-{account}")).IsFalse();
        var repository = new ExternalLoginAuthenticationRepository(factory, fixture.NativeQueueEnrollment);
        await Assert.That((await repository.GetByProviderAndKey(new(AuthenticationProviderKind.Keycloak, account)))?.Id)
            .IsEqualTo(selected);
        await Assert.That((await repository.GetByProviderAndKey(new(AuthenticationProviderKind.Local, account)))?.UserId)
            .IsEqualTo(other);
        await Assert.That(await repository.GetByProviderAndKey(new(AuthenticationProviderKind.Keycloak, $"missing-{account}")))
            .IsNull();
    }

    [Test]
    public async Task EfFirstDeploymentHasNoExternalLoginBusinessRoutine()
    {
        await using var context = Open();
        await context.Database.OpenConnectionAsync();
        var entity = context.Model.FindEntityType(typeof(UserExternalLogin))!;
        string routine = $"{Quote(entity.GetSchema()!)}.{Quote(entity.GetTableName()! + "_for_authentication")}(integer,text)";
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT pg_catalog.to_regprocedure(@routine) IS NULL";
        command.Parameters.Add(new NpgsqlParameter("routine", routine));

        await Assert.That(await command.ExecuteScalarAsync()).IsEqualTo(true);
    }

    [Test]
    public async Task EfFirstGlobalHasOnlyTheFixedBindingColumnReadGrant()
    {
        var factory = new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global));
        await using var global = await factory.CreateDbContextAsync();
        await global.Database.OpenConnectionAsync();
        var entity = global.Model.FindEntityType(typeof(UserExternalLogin))!;
        string table = $"{Quote(entity.GetSchema()!)}.{Quote(entity.GetTableName()!)}";
        await using var command = global.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT NOT pg_catalog.has_table_privilege(current_user, @table, 'SELECT')
                AND NOT pg_catalog.has_table_privilege(current_user, @table, 'INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                AND NOT pg_catalog.has_any_column_privilege(current_user, @table, 'INSERT,UPDATE,REFERENCES')
                AND NOT EXISTS (
                    SELECT 1 FROM unnest(@columns) AS expected(name)
                    WHERE NOT pg_catalog.has_column_privilege(current_user, @table, expected.name, 'SELECT')
                        OR pg_catalog.has_column_privilege(current_user, @table, expected.name, 'SELECT WITH GRANT OPTION'))
            """;
        command.Parameters.Add(new NpgsqlParameter("table", table));
        command.Parameters.Add(new NpgsqlParameter("columns", new[]
        {
            "id", "user_id", "authentication_provider_id", "provider_key", "provider_display_name",
            "created_at", "created_by", "updated_at", "updated_by"
        }));

        await Assert.That(await command.ExecuteScalarAsync()).IsEqualTo(true);
    }

    [Test]
    public async Task EfFirstGlobalReadGrantCannotMutateBindings()
    {
        var factory = new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global));
        await using var global = await factory.CreateDbContextAsync();
        var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
            await global.UserExternalLogins.Where(row => row.Id == Guid.Empty)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ProviderKey, "forbidden")));

        await Assert.That(denied.SqlState).IsEqualTo("42501");
    }

    [Test]
    public async Task EfFirstRuntimeCannotReadGlobalBindingColumns()
    {
        await using var runtime = fixture.CreateEnrolledTenantlessDbContext();
        await Assert.That(await runtime.Database.SqlQueryRaw<string>("SELECT session_user::text AS \"Value\"")
            .SingleAsync()).IsEqualTo(fixture.NativeQueueEnrollment.RuntimeLogin);
        var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
            await runtime.UserExternalLogins.AsNoTracking().AnyAsync());

        await Assert.That(denied.SqlState).IsEqualTo("42501");
    }

    [Test]
    [Arguments("table-read")]
    [Arguments("write")]
    [Arguments("grant-option")]
    [Arguments("missing-column")]
    [Arguments("bypass-rls")]
    [Arguments("schema-create")]
    [Arguments("system-column")]
    [Arguments("system-grant-option")]
    [Arguments("set-only-write")]
    [Arguments("admin-only-write")]
    [Arguments("admin-only-read")]
    [Arguments("nested-admin-read")]
    public async Task ReaderAndInstallerRejectPrivilegeDriftWithoutRepair(string drift)
    {
        await using var context = Open();
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            var enrollment = fixture.NativeQueueEnrollment;
            string global = Quote(enrollment.GlobalLogin);
            var entity = context.Model.FindEntityType(typeof(UserExternalLogin))!;
            string table = $"{Quote(entity.GetSchema()!)}.{Quote(entity.GetTableName()!)}";
            string reachable = Quote($"external_login_write_{Guid.CreateVersion7():N}");
            string intermediate = Quote($"external_login_delegate_{Guid.CreateVersion7():N}");
            const string readColumns = "id,user_id,authentication_provider_id,provider_key,provider_display_name,created_at,created_by,updated_at,updated_by";
            string mutation = drift switch
            {
                "table-read" => $"GRANT SELECT ON {table} TO {global}",
                "write" => $"GRANT UPDATE(provider_key) ON {table} TO {global}",
                "grant-option" => $"GRANT SELECT(provider_key) ON {table} TO {global} WITH GRANT OPTION",
                "missing-column" => $"REVOKE SELECT(provider_key) ON {table} FROM {global}",
                "bypass-rls" => $"ALTER ROLE {global} BYPASSRLS",
                "schema-create" => $"GRANT CREATE ON SCHEMA {Quote(entity.GetSchema()!)} TO {global}",
                "system-column" => $"GRANT SELECT(xmin) ON {table} TO {global}",
                "system-grant-option" => $"GRANT SELECT(xmin) ON {table} TO {global} WITH GRANT OPTION",
                "set-only-write" => $"CREATE ROLE {reachable} NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS; "
                    + $"GRANT UPDATE(provider_key) ON {table} TO {reachable}; "
                    + $"GRANT {reachable} TO {global} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE",
                "admin-only-write" => $"CREATE ROLE {reachable} NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS; "
                    + $"GRANT UPDATE(provider_key) ON {table} TO {reachable}; "
                    + $"GRANT {reachable} TO {global} WITH INHERIT FALSE, SET FALSE, ADMIN TRUE",
                "admin-only-read" => $"CREATE ROLE {reachable} NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS; "
                    + $"GRANT SELECT({readColumns}) ON {table} TO {reachable}; "
                    + $"GRANT {reachable} TO {global} WITH INHERIT FALSE, SET FALSE, ADMIN TRUE",
                "nested-admin-read" => $"CREATE ROLE {reachable} NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS; "
                    + $"CREATE ROLE {intermediate} NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS; "
                    + $"GRANT SELECT({readColumns}) ON {table} TO {reachable}; "
                    + $"GRANT {reachable} TO {intermediate} WITH INHERIT FALSE, SET FALSE, ADMIN TRUE; "
                    + $"GRANT {intermediate} TO {global} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE",
                _ => throw new ArgumentOutOfRangeException(nameof(drift))
            };
            await context.Database.ExecuteSqlRawAsync(mutation);
            await transaction.CreateSavepointAsync("before_entry");
            await context.Database.ExecuteSqlRawAsync($"SET LOCAL SESSION AUTHORIZATION {global}");
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await PostgresExternalLoginReadPermissions.RequireReadyAsync(context, enrollment));
            await transaction.RollbackToSavepointAsync("before_entry");
            var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
                await PostgresExternalLoginReadPermissions.ApplyAsync(context, enrollment));
            await Assert.That(denied.SqlState).IsEqualTo("55000");
        });
    }

    [Test]
    [Arguments("column")]
    [Arguments("global-inherit")]
    [Arguments("delete")]
    [Arguments("truncate")]
    [Arguments("trigger")]
    [Arguments("maintain")]
    [Arguments("system-column")]
    [Arguments("set-only-read")]
    [Arguments("admin-only-read")]
    public async Task ReaderAndInstallerRejectRuntimeAuthority(string drift)
    {
        await using var context = Open();
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            var enrollment = fixture.NativeQueueEnrollment;
            var entity = context.Model.FindEntityType(typeof(UserExternalLogin))!;
            string table = $"{Quote(entity.GetSchema()!)}.{Quote(entity.GetTableName()!)}";
            string runtime = Quote(enrollment.RuntimeLogin);
            string reachable = Quote($"external_login_read_{Guid.CreateVersion7():N}");
            string mutation = drift switch
            {
                "column" => $"GRANT SELECT(provider_key) ON {table} TO {runtime}",
                "global-inherit" => $"GRANT {Quote(enrollment.GlobalLogin)} TO {runtime} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE",
                "delete" => $"GRANT DELETE ON {table} TO {runtime}",
                "truncate" => $"GRANT TRUNCATE ON {table} TO {runtime}",
                "trigger" => $"GRANT TRIGGER ON {table} TO {runtime}",
                "maintain" => $"GRANT MAINTAIN ON {table} TO {runtime}",
                "system-column" => $"GRANT SELECT(xmin) ON {table} TO {runtime}",
                "set-only-read" => $"CREATE ROLE {reachable} NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS; "
                    + $"GRANT SELECT(provider_key) ON {table} TO {reachable}; "
                    + $"GRANT {reachable} TO {runtime} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE",
                "admin-only-read" => $"CREATE ROLE {reachable} NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS; "
                    + $"GRANT SELECT(provider_key) ON {table} TO {reachable}; "
                    + $"GRANT {reachable} TO {runtime} WITH INHERIT FALSE, SET FALSE, ADMIN TRUE",
                _ => throw new ArgumentOutOfRangeException(nameof(drift))
            };
            await context.Database.ExecuteSqlRawAsync(mutation);
            await transaction.CreateSavepointAsync("before_entry");
            await context.Database.ExecuteSqlRawAsync($"SET LOCAL SESSION AUTHORIZATION {Quote(enrollment.GlobalLogin)}");
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await PostgresExternalLoginReadPermissions.RequireReadyAsync(context, enrollment));
            await transaction.RollbackToSavepointAsync("before_entry");
            var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
                await PostgresExternalLoginReadPermissions.ApplyAsync(context, enrollment));
            await Assert.That(denied.SqlState).IsEqualTo("55000");
        });
    }

    [Test]
    public async Task InstallerCannotSubstituteAnotherDeclaredLogin()
    {
        await using var context = Open();
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            var enrollment = fixture.NativeQueueEnrollment;
            var wrong = enrollment with { GlobalLogin = enrollment.RuntimeLogin, RuntimeLogin = enrollment.GlobalLogin };

            var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
                await PostgresExternalLoginReadPermissions.ApplyAsync(context, wrong));

            await Assert.That(denied.SqlState).IsEqualTo("55000");
        });
    }

    [Test]
    public async Task ReaderRejectsRoleSubstitution()
    {
        await using var context = Open();
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            await context.Database.ExecuteSqlRawAsync($"SET LOCAL ROLE {Quote(fixture.NativeQueueEnrollment.GlobalLogin)}");
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await PostgresExternalLoginReadPermissions.RequireReadyAsync(context, fixture.NativeQueueEnrollment));
        });
    }

    [Test]
    public async Task ReaderRejectsAnotherLoginWithCopiedReadPermissions()
    {
        await using var context = Open();
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            var entity = context.Model.FindEntityType(typeof(UserExternalLogin))!;
            string table = $"{Quote(entity.GetSchema()!)}.{Quote(entity.GetTableName()!)}";
            string other = Quote(fixture.NativeQueueEnrollment.EmailInstanceMutationLogin);
            await context.Database.ExecuteSqlRawAsync($"""
            GRANT SELECT(id,user_id,authentication_provider_id,provider_key,provider_display_name,
                created_at,created_by,updated_at,updated_by) ON {table} TO {other};
            SET LOCAL SESSION AUTHORIZATION {other};
            """);
            await Assert.That(await context.Database.SqlQueryRaw<string>("SELECT session_user::text AS \"Value\"")
                .SingleAsync()).IsEqualTo(fixture.NativeQueueEnrollment.EmailInstanceMutationLogin);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await PostgresExternalLoginReadPermissions.RequireReadyAsync(context, fixture.NativeQueueEnrollment));
        });
    }

    [Test]
    public async Task InstallerRequiresTheDeclaredMigratorSession()
    {
        await using var context = Open();
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            await context.Database.ExecuteSqlRawAsync($"SET LOCAL SESSION AUTHORIZATION {Quote(fixture.NativeQueueEnrollment.GlobalLogin)}");
            var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
                await PostgresExternalLoginReadPermissions.ApplyAsync(context, fixture.NativeQueueEnrollment));
            await Assert.That(denied.SqlState).IsEqualTo("42501");
        });
    }

    [Test]
    public async Task PostgreSqlReaderRejectsMissingEnrollment()
    {
        var factory = new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global));
        var repository = new ExternalLoginAuthenticationRepository(factory, null);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await repository.GetByProviderAndKey(new(AuthenticationProviderKind.Keycloak, "missing")));
    }

    [Test]
    [Arguments("event_external_login_authentication_owner")]
    [Arguments("event_external_login_authentication_migrator")]
    [Arguments("event_external_login_authentication_global")]
    public async Task ObsoleteInstallationRequiresExplicitReprovisioningWithoutCleanup(string role)
    {
        await using var context = Open();
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            await context.Database.ExecuteSqlRawAsync($"CREATE ROLE {Quote(role)} NOLOGIN");
            await transaction.CreateSavepointAsync("before_install");
            var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
                await PostgresExternalLoginReadPermissions.ApplyAsync(context, fixture.NativeQueueEnrollment));
            await Assert.That(denied.SqlState).IsEqualTo("55000");
            await Assert.That(denied.MessageText).IsEqualTo("external_login_requires_fresh_development_database");
            await transaction.RollbackToSavepointAsync("before_install");
            await Assert.That(await context.Database.SqlQuery<bool>(
                $"SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = {role}) AS \"Value\"")
                .SingleAsync()).IsTrue();
        });
    }

    private ExploreDbContext Open()
    {
        var options = new DbContextOptionsBuilder<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        return new ExploreDbContext(options.Options);
    }

    private static User User(Guid id) => new()
    {
        Id = id,
        Pii = new UserPii { Email = $"{id:N}@example.test", FirstName = "External", LastName = "Identity" }
    };

    private static UserExternalLogin Binding(Guid id, Guid user, AuthenticationProviderKind provider, string key) => new()
    {
        Id = id,
        UserId = user,
        User = null!,
        AuthenticationProviderId = (int)provider,
        AuthenticationProvider = null!,
        ProviderKey = key,
        CreatedAt = DateTime.UtcNow
    };

    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
