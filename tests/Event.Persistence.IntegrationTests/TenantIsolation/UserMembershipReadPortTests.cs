using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class UserMembershipReadPortTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExactUserProjectionDoesNotBorrowAbsentOrForeignRequestTenancy(bool foreignTenant)
    {
        Guid userId = Guid.CreateVersion7();
        Guid otherUser = Guid.CreateVersion7();
        Guid a = Guid.CreateVersion7();
        Guid b = Guid.CreateVersion7();
        Guid inactive = Guid.CreateVersion7();
        await using var migrator = Open(PrimaryDatabaseRole.Migrator);
        await PostgresUserTenantMembershipContract.ApplyAsync(migrator, fixture.NativeQueueEnrollment);
        migrator.Users.AddRange(User(userId), User(otherUser));
        migrator.Tenants.AddRange(Tenant(a, TenantStatusEnum.Active), Tenant(b, TenantStatusEnum.Active),
            Tenant(inactive, TenantStatusEnum.Suspended));
        migrator.TenantUsers.AddRange(Member(a, userId), Member(b, userId),
            Member(inactive, userId), Member(b, otherUser));
        await migrator.SaveChangesAsync();
        await using var request = Open(PrimaryDatabaseRole.Runtime);
        if (foreignTenant) request.TenantContext = new FixedTenant(inactive);
        request.BindTenantLease();
        var global = new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global));
        var repository = new UserTenantMembershipRepository(global,
            new TenantExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime)));

        var result = await repository.GetActiveTenantsForUserAsync(userId);

        await Assert.That(result.Select(row => row.TenantId)).IsEquivalentTo([a, b]);
        await Assert.That(result.All(row => row.UserId == userId
            && row.StatusId == (int)TenantUserStatusEnum.Active
            && row.Tenant.Slug == $"membership-{row.TenantId:N}"
            && row.Tenant.TenantStatus.Id == (int)TenantStatusEnum.Active)).IsTrue();
        await Assert.That(await repository.GetActiveTenantsForUserAsync(Guid.CreateVersion7())).IsEmpty();
        await Assert.That(await repository.GetActiveTenantsForUserAsync(Guid.Empty)).IsEmpty();
        await Assert.That(request.TenantFilterTenantId).IsEqualTo(foreignTenant ? inactive : (Guid?)null);
        await request.Database.OpenConnectionAsync();
        await using var forbidden = request.Database.GetDbConnection().CreateCommand();
        forbidden.CommandText = $"SELECT * FROM {PostgresUserTenantMembershipContract.FunctionSql(request)}(@user)";
        forbidden.Parameters.Add(new NpgsqlParameter("user", userId));
        var denied = await Assert.ThrowsAsync<PostgresException>(async () => { await forbidden.ExecuteScalarAsync(); });
        await Assert.That(denied.SqlState).IsEqualTo("42501");
    }

    [Test]
    [Arguments("routine-executor")]
    [Arguments("runtime-via-login")]
    [Arguments("runtime-via-login-set-only")]
    [Arguments("runtime-owner")]
    [Arguments("runtime-migrator")]
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
    public async Task AuthorityDriftFailsEntryAndInstallerWithoutRepair(string drift)
    {
        await using var migrator = Open(PrimaryDatabaseRole.Migrator);
        await using var transaction = await migrator.Database.BeginTransactionAsync();
        var enrollment = fixture.NativeQueueEnrollment;
        string signature = PostgresUserTenantMembershipContract.FunctionSql(migrator) + "(uuid)";
        string runtime = Quote(enrollment.RuntimeLogin), global = Quote(enrollment.GlobalLogin);
        string owner = PostgresUserTenantMembershipContract.OwnerRole;
        string executor = PostgresUserTenantMembershipContract.GlobalRole;
        string migration = PostgresUserTenantMembershipContract.MigratorRole;
        var entity = migrator.Model.FindEntityType(typeof(TenantUser))!;
        var sql = migrator.GetService<ISqlGenerationHelper>();
        string schema = migrator.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
            ?? entity.GetSchema() ?? "public";
        string members = sql.DelimitIdentifier(entity.GetTableName()!, schema);
        string grant = drift switch
        {
            "routine-executor" => $"GRANT EXECUTE ON FUNCTION {signature} TO {runtime}",
            "runtime-via-login" => $"GRANT {global} TO {runtime} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE",
            "runtime-via-login-set-only" => $"GRANT {global} TO {runtime} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE",
            "runtime-owner" => $"GRANT {owner} TO {runtime} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE",
            "runtime-migrator" => $"GRANT {migration} TO {runtime} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE",
            "global-owner" => $"GRANT {owner} TO {global} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE",
            "global-migrator" => $"GRANT {migration} TO {global} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE",
            "owner-flags" => $"ALTER ROLE {owner} BYPASSRLS",
            "migrator-flags" => $"ALTER ROLE {migration} INHERIT",
            "executor-flags" => $"ALTER ROLE {executor} LOGIN",
            "owner-edge" => $"GRANT {owner} TO {migration} WITH INHERIT TRUE, SET TRUE, ADMIN FALSE",
            "migrator-edge" => $"GRANT {migration} TO {Quote(enrollment.MigratorLogin)} WITH INHERIT FALSE, SET TRUE, ADMIN TRUE",
            "executor-edge" => $"GRANT {executor} TO {global} WITH INHERIT TRUE, SET TRUE, ADMIN FALSE",
            "owner-write" => $"GRANT UPDATE ON {members} TO {owner}",
            "executor-select" => $"GRANT SELECT ON {members} TO {executor}",
            _ => throw new ArgumentOutOfRangeException(nameof(drift))
        };
        await migrator.Database.ExecuteSqlRawAsync(grant);
        await transaction.CreateSavepointAsync("before_entry");
        await migrator.Database.ExecuteSqlRawAsync($"SET LOCAL SESSION AUTHORIZATION {global}");
        await using (var command = migrator.Database.GetDbConnection().CreateCommand())
        {
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = $"SELECT * FROM {PostgresUserTenantMembershipContract.FunctionSql(migrator)}(@user)";
            command.Parameters.Add(new NpgsqlParameter("user", Guid.CreateVersion7()));
            var denied = await Assert.ThrowsAsync<PostgresException>(async () => { await command.ExecuteNonQueryAsync(); });
            await Assert.That(denied.SqlState).IsEqualTo("55000");
        }
        await transaction.RollbackToSavepointAsync("before_entry");
        await transaction.CreateSavepointAsync("before_install");
        var reinstall = await Assert.ThrowsAsync<PostgresException>(async () =>
            await PostgresUserTenantMembershipContract.ApplyAsync(migrator, enrollment));
        await Assert.That(reinstall.SqlState).IsEqualTo("55000");
        await transaction.RollbackToSavepointAsync("before_install");
        // A second installation must still reject the unmodified drift.
        await Assert.ThrowsAsync<PostgresException>(async () =>
            await PostgresUserTenantMembershipContract.ApplyAsync(migrator, enrollment));
    }

    [Test]
    public async Task UnenrolledSessionCannotBorrowTheCleanExecutorRole()
    {
        await using var migrator = Open(PrimaryDatabaseRole.Migrator);
        await using var transaction = await migrator.Database.BeginTransactionAsync();
        await migrator.Database.ExecuteSqlRawAsync($"SET LOCAL ROLE {PostgresUserTenantMembershipContract.GlobalRole}");
        await using var command = migrator.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = $"SELECT * FROM {PostgresUserTenantMembershipContract.FunctionSql(migrator)}(@user)";
        command.Parameters.Add(new NpgsqlParameter("user", Guid.CreateVersion7()));
        var denied = await Assert.ThrowsAsync<PostgresException>(async () => { await command.ExecuteNonQueryAsync(); });
        await Assert.That(denied.SqlState).IsEqualTo("42501");
    }

    [Test]
    public async Task InstallerRejectsDifferentConfiguredGlobalLogin()
    {
        await using var migrator = Open(PrimaryDatabaseRole.Migrator);
        var changed = fixture.NativeQueueEnrollment with
        {
            GlobalLogin = fixture.NativeQueueEnrollment.EmailInstanceMutationLogin,
            EmailInstanceMutationLogin = fixture.NativeQueueEnrollment.GlobalLogin
        };
        var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
            await PostgresUserTenantMembershipContract.ApplyAsync(migrator, changed));
        await Assert.That(denied.SqlState).IsEqualTo("55000");
    }

    private ExploreDbContext Open(PrimaryDatabaseRole role)
    {
        var options = new DbContextOptionsBuilder<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(role));
        if (role == PrimaryDatabaseRole.Runtime) options.AddInterceptors(PostgresTenantSessionInterceptor.Instance);
        return new ExploreDbContext(options.Options);
    }

    private static User User(Guid id) => new()
    {
        Id = id,
        Pii = new UserPii { Email = $"{id:N}@example.test", FirstName = "Member", LastName = "Owner" }
    };

    private static Tenant Tenant(Guid id, TenantStatusEnum status) => new()
    {
        Id = id,
        FullName = "Membership tenant",
        Slug = $"membership-{id:N}",
        TenantStatusId = (int)status,
        TenantStatus = null!
    };

    private static TenantUser Member(Guid tenant, Guid user) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenant,
        Tenant = null!,
        UserId = user,
        User = null!,
        StatusId = (int)TenantUserStatusEnum.Active,
        JoinedAt = new DateTime(2028, 4, 5, 12, 0, 0, DateTimeKind.Utc)
    };

    private sealed record FixedTenant(Guid TenantId) : ITenantContext;

    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
