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
        var repository = new UserTenantMembershipRepository(global);

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
    public async Task UndeclaredExecutorCannotReadOrRepairTheMembershipProjection()
    {
        await using var migrator = Open(PrimaryDatabaseRole.Migrator);
        string signature = PostgresUserTenantMembershipContract.FunctionSql(migrator) + "(uuid)";
        string runtime = fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime).Username.Replace("\"", "\"\"", StringComparison.Ordinal);
        await migrator.Database.ExecuteSqlRawAsync($"GRANT EXECUTE ON FUNCTION {signature} TO \"{runtime}\"");
        try
        {
            var global = new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global));
            var repository = new UserTenantMembershipRepository(global);
            var rejected = await Assert.ThrowsAsync<RetryLimitExceededException>(async () =>
                await repository.GetActiveTenantsForUserAsync(Guid.CreateVersion7()));
            await Assert.That(rejected.InnerException is PostgresException
                { SqlState: "55000", MessageText: "native_routine_acl_drift" }).IsTrue();
            var reinstall = await Assert.ThrowsAsync<PostgresException>(async () =>
                await PostgresUserTenantMembershipContract.ApplyAsync(migrator));
            await Assert.That(reinstall.SqlState).IsEqualTo("55000");
        }
        finally
        {
            await migrator.Database.ExecuteSqlRawAsync($"REVOKE EXECUTE ON FUNCTION {signature} FROM \"{runtime}\"");
        }
        await PostgresUserTenantMembershipContract.ApplyAsync(migrator);
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
        Id = id, Pii = new UserPii { Email = $"{id:N}@example.test", FirstName = "Member", LastName = "Owner" }
    };

    private static Tenant Tenant(Guid id, TenantStatusEnum status) => new()
    {
        Id = id, FullName = "Membership tenant", Slug = $"membership-{id:N}",
        TenantStatusId = (int)status, TenantStatus = null!
    };

    private static TenantUser Member(Guid tenant, Guid user) => new()
    {
        Id = Guid.CreateVersion7(), TenantId = tenant, Tenant = null!, UserId = user, User = null!,
        StatusId = (int)TenantUserStatusEnum.Active,
        JoinedAt = new DateTime(2028, 4, 5, 12, 0, 0, DateTimeKind.Utc)
    };

    private sealed record FixedTenant(Guid TenantId) : ITenantContext;
}
