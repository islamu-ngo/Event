using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class AdministrativeAuthorityReaderTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task InstanceAuthorityTracksExactPersistedGrantWithoutRuntimeMembershipReads()
    {
        await using var migration = Open(PrimaryDatabaseRole.Migrator);
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Pii = new() { Email = "native-admin@example.test", FirstName = "Native", LastName = "Administrator" }
        };
        var role = await migration.Roles.SingleAsync(item => item.MasterCode == "platform.admin");
        migration.Users.Add(user);
        await migration.SaveChangesAsync();
        var reader = new AdministrativeAuthorityRepository(new GlobalExploreDbContextFactory(
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global)));
        await Assert.That(await reader.IsInstanceAdminAsync(user.Id)).IsFalse();
        var grant = new PlatformUserRole
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            User = user,
            RoleId = role.Id,
            Role = role
        };
        migration.PlatformUserRoles.Add(grant);
        await migration.SaveChangesAsync();
        try
        {
            await Assert.That(await reader.IsInstanceAdminAsync(user.Id)).IsTrue();
            await Assert.That(await reader.IsInstanceAdminAsync(Guid.CreateVersion7())).IsFalse();
            migration.PlatformUserRoles.Remove(grant);
            await migration.SaveChangesAsync();
            await Assert.That(await reader.IsInstanceAdminAsync(user.Id)).IsFalse();
            await using var runtime = Open(PrimaryDatabaseRole.Runtime);
            var denied = await Assert.ThrowsAsync<Npgsql.PostgresException>(() =>
                runtime.PlatformUserRoles.AsNoTracking().ToListAsync());
            await Assert.That(denied!.SqlState).IsEqualTo(Npgsql.PostgresErrorCodes.InsufficientPrivilege);
        }
        finally
        {
            await migration.PlatformUserRoles.Where(row => row.Id == grant.Id).ExecuteDeleteAsync();
            await migration.Users.Where(row => row.Id == user.Id).ExecuteDeleteAsync();
        }
    }

    private ExploreDbContext Open(PrimaryDatabaseRole role)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(role));
        return new(options.Options);
    }
}
