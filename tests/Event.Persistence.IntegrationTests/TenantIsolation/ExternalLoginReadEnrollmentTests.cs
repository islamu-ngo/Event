using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
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
    [Arguments(false)]
    [Arguments(true)]
    public async Task RuntimeCannotBorrowExternalIdentityDiscovery(bool throughGlobalLogin)
    {
        await using var context = Open();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var enrollment = fixture.NativeQueueEnrollment;
        string borrowed = throughGlobalLogin ? enrollment.GlobalLogin : PostgresExternalLoginAuthenticationContract.GlobalRole;
        await context.Database.ExecuteSqlRawAsync(
            $"GRANT {Quote(borrowed)} TO {Quote(enrollment.RuntimeLogin)} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE");

        await RequireEntryAndInstallerDenial(context, transaction, enrollment.RuntimeLogin);
    }

    [Test]
    [Arguments("global-owner")]
    [Arguments("global-migrator")]
    [Arguments("owner-flags")]
    [Arguments("migrator-flags")]
    [Arguments("executor-flags")]
    [Arguments("owner-edge")]
    [Arguments("migrator-edge")]
    [Arguments("executor-edge")]
    [Arguments("owner-write")]
    [Arguments("executor-select")]
    [Arguments("runtime-execute")]
    public async Task AuthorityDriftIsNotRepairedByReinstallation(string drift)
    {
        await using var context = Open();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var enrollment = fixture.NativeQueueEnrollment;
        string owner = Quote(PostgresExternalLoginAuthenticationContract.OwnerRole);
        string migrator = Quote(PostgresExternalLoginAuthenticationContract.MigratorRole);
        string executor = Quote(PostgresExternalLoginAuthenticationContract.GlobalRole);
        var entity = context.Model.FindEntityType(typeof(UserExternalLogin))!;
        string table = $"{Quote(entity.GetSchema()!)}.{Quote(entity.GetTableName()!)}";
        string function = PostgresExternalLoginAuthenticationContract.FunctionSql(context);
        string mutation = drift switch
        {
            "global-owner" => $"GRANT {owner} TO {Quote(enrollment.GlobalLogin)} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE",
            "global-migrator" => $"GRANT {migrator} TO {Quote(enrollment.GlobalLogin)} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE",
            "owner-flags" => $"ALTER ROLE {owner} BYPASSRLS",
            "migrator-flags" => $"ALTER ROLE {migrator} CREATEDB",
            "executor-flags" => $"ALTER ROLE {executor} LOGIN",
            "owner-edge" => $"GRANT {owner} TO {migrator} WITH INHERIT TRUE, SET TRUE, ADMIN FALSE",
            "migrator-edge" => $"GRANT {migrator} TO {Quote(enrollment.MigratorLogin)} WITH INHERIT TRUE, SET TRUE, ADMIN FALSE",
            "executor-edge" => $"GRANT {executor} TO {Quote(enrollment.GlobalLogin)} WITH INHERIT TRUE, SET TRUE, ADMIN FALSE",
            "owner-write" => $"GRANT UPDATE(provider_key) ON {table} TO {owner}",
            "executor-select" => $"GRANT SELECT ON {table} TO {executor}",
            "runtime-execute" => $"GRANT EXECUTE ON FUNCTION {function}(integer,text) TO {Quote(enrollment.RuntimeLogin)}",
            _ => throw new ArgumentOutOfRangeException(nameof(drift))
        };
        await context.Database.ExecuteSqlRawAsync(mutation);

        await RequireEntryAndInstallerDenial(context, transaction, enrollment.GlobalLogin);
    }

    [Test]
    public async Task ConfiguredGlobalReadsOnlyExactProviderAccountWithoutTableAccess()
    {
        Guid subject = Guid.CreateVersion7();
        Guid other = Guid.CreateVersion7();
        Guid selected = Guid.CreateVersion7();
        string account = $"account-{Guid.NewGuid():N}";
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
        await global.Database.OpenConnectionAsync();
        await using (var command = global.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText = $"SELECT id FROM {PostgresExternalLoginAuthenticationContract.FunctionSql(global)}(@provider,@account)";
            command.Parameters.Add(new NpgsqlParameter("provider", (int)AuthenticationProviderKind.Keycloak));
            command.Parameters.Add(new NpgsqlParameter("account", account));
            await using var reader = await command.ExecuteReaderAsync();
            await Assert.That(await reader.ReadAsync()).IsTrue();
            await Assert.That(reader.GetGuid(0)).IsEqualTo(selected);
            await Assert.That(await reader.ReadAsync()).IsFalse();
        }
        var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await global.UserExternalLogins.IgnoreQueryFilters().AnyAsync();
        });
        await Assert.That(denied.SqlState).IsEqualTo("42501");
    }

    [Test]
    public async Task InstallerCannotSubstituteAnotherDeclaredLogin()
    {
        await using var context = Open();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var enrollment = fixture.NativeQueueEnrollment;
        var wrong = enrollment with { GlobalLogin = enrollment.RuntimeLogin, RuntimeLogin = enrollment.GlobalLogin };

        var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
            await PostgresExternalLoginAuthenticationContract.ApplyAsync(context, wrong));

        await Assert.That(denied.SqlState).IsEqualTo("55000");
    }

    [Test]
    public async Task CleanUnenrolledSessionCannotUseExecutorRole()
    {
        await using var context = Open();
        await using var transaction = await context.Database.BeginTransactionAsync();
        await context.Database.ExecuteSqlRawAsync($"SET LOCAL ROLE {Quote(PostgresExternalLoginAuthenticationContract.GlobalRole)}");

        var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
            await context.Database.ExecuteSqlRawAsync(
                $"SELECT * FROM {PostgresExternalLoginAuthenticationContract.FunctionSql(context)}(1,'absent')"));

        await Assert.That(denied.SqlState).IsEqualTo("42501");
    }

    private async Task RequireEntryAndInstallerDenial(
        ExploreDbContext context, IDbContextTransaction transaction, string session)
    {
        await transaction.CreateSavepointAsync("before_entry");
        await context.Database.ExecuteSqlRawAsync($"SET LOCAL SESSION AUTHORIZATION {Quote(session)}");
        var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
            await context.Database.ExecuteSqlRawAsync(
                $"SELECT * FROM {PostgresExternalLoginAuthenticationContract.FunctionSql(context)}(1,'absent')"));
        await Assert.That(denied.SqlState).IsEqualTo("55000");
        await transaction.RollbackToSavepointAsync("before_entry");
        var reinstall = await Assert.ThrowsAsync<PostgresException>(async () =>
            await PostgresExternalLoginAuthenticationContract.ApplyAsync(context, fixture.NativeQueueEnrollment));
        await Assert.That(reinstall.SqlState).IsEqualTo("55000");
    }

    private ExploreDbContext Open()
    {
        var options = new DbContextOptionsBuilder<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        return new ExploreDbContext(options.Options);
    }

    private static User User(Guid id) => new()
    {
        Id = id, Pii = new UserPii { Email = $"{id:N}@example.test", FirstName = "External", LastName = "Identity" }
    };

    private static UserExternalLogin Binding(Guid id, Guid user, AuthenticationProviderKind provider, string key) => new()
    {
        Id = id, UserId = user, User = null!, AuthenticationProviderId = (int)provider,
        AuthenticationProvider = null!, ProviderKey = key, CreatedAt = DateTime.UtcNow
    };

    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
