using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public class TenantUserRepositoryBypassTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task ExactTenantUserBypasses_WithAmbientTenant_ReturnOnlyExplicitTenantMembershipRows()
    {
        await fixture.ResetAsync();
        await using var seedContext = fixture.CreateDbContext();

        var tenantA = CreateTenant("tenant-user-a");
        var tenantB = CreateTenant("tenant-user-b");
        seedContext.Tenants.AddRange(tenantA, tenantB);
        await seedContext.SaveChangesAsync();

        var primaryUser = CreateUser("tenant-user-primary");
        var tenantBOnlyUser = CreateUser("tenant-user-b-only");
        var suspendedUser = CreateUser("tenant-user-suspended");
        var deletedUser = CreateUser("tenant-user-deleted");
        seedContext.Users.AddRange(primaryUser, tenantBOnlyUser, suspendedUser, deletedUser);
        await seedContext.SaveChangesAsync();

        var primaryActor = CreateActor(primaryUser.Id, "Primary User");
        var tenantBOnlyActor = CreateActor(tenantBOnlyUser.Id, "Tenant B Only");
        var suspendedActor = CreateActor(suspendedUser.Id, "Tenant A Suspended");
        var deletedActor = CreateActor(deletedUser.Id, "Tenant A Deleted");
        seedContext.Actors.AddRange(primaryActor, tenantBOnlyActor, suspendedActor, deletedActor);
        await seedContext.SaveChangesAsync();

        var tenantAUser = CreateTenantUser(tenantA.Id, primaryUser.Id, primaryActor.Id, TenantUserStatusEnum.Active);
        var tenantBUser = CreateTenantUser(tenantB.Id, primaryUser.Id, primaryActor.Id, TenantUserStatusEnum.Active);
        var tenantBOnlyTenantUser = CreateTenantUser(
            tenantB.Id,
            tenantBOnlyUser.Id,
            tenantBOnlyActor.Id,
            TenantUserStatusEnum.Active);
        var tenantASuspendedUser = CreateTenantUser(
            tenantA.Id,
            suspendedUser.Id,
            suspendedActor.Id,
            TenantUserStatusEnum.Suspended);
        var tenantADeletedUser = CreateTenantUser(
            tenantA.Id,
            deletedUser.Id,
            deletedActor.Id,
            TenantUserStatusEnum.Active);
        tenantADeletedUser.IsDeleted = true;
        tenantADeletedUser.DeletedAt = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

        seedContext.TenantUsers.AddRange(
            tenantAUser,
            tenantBUser,
            tenantBOnlyTenantUser,
            tenantASuspendedUser,
            tenantADeletedUser);
        await seedContext.SaveChangesAsync();

        await using var tenantBContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantB.Id));
        var visibleWithoutBypass = await tenantBContext.TenantUsers
            .AsNoTracking()
            .Select(tenantUser => tenantUser.Id)
            .ToListAsync();

        var repository = new TenantUserRepository(tenantBContext);

        var tenantAByUser = await repository.GetByTenantAndUserAsync(tenantA.Id, primaryUser.Id);
        var tenantAByActor = await repository.GetByTenantAndActorAsync(tenantA.Id, primaryActor.Id);
        var wrongTenantActorLookup = await repository.GetByTenantAndActorAsync(tenantA.Id, tenantBOnlyActor.Id);
        var primaryIsActiveInTenantA = await repository.IsActiveTenantUserAsync(tenantA.Id, primaryUser.Id);
        var primaryIsActiveInTenantB = await repository.IsActiveTenantUserAsync(tenantB.Id, primaryUser.Id);
        var suspendedIsActiveInTenantA = await repository.IsActiveTenantUserAsync(tenantA.Id, suspendedUser.Id);
        var deletedIsActiveInTenantA = await repository.IsActiveTenantUserAsync(tenantA.Id, deletedUser.Id);

        await Assert.That(visibleWithoutBypass).IsEquivalentTo([tenantBUser.Id, tenantBOnlyTenantUser.Id]);

        await Assert.That(tenantAByUser).IsNotNull();
        await Assert.That(tenantAByUser!.Id).IsEqualTo(tenantAUser.Id);
        await Assert.That(tenantAByUser.TenantId).IsEqualTo(tenantA.Id);
        await Assert.That(tenantAByUser.UserId).IsEqualTo(primaryUser.Id);

        await Assert.That(tenantAByActor).IsNotNull();
        await Assert.That(tenantAByActor!.Id).IsEqualTo(tenantAUser.Id);
        await Assert.That(tenantAByActor.ActorId).IsEqualTo(primaryActor.Id);
        await Assert.That(wrongTenantActorLookup).IsNull();

        await Assert.That(primaryIsActiveInTenantA).IsTrue();
        await Assert.That(primaryIsActiveInTenantB).IsTrue();
        await Assert.That(suspendedIsActiveInTenantA).IsFalse();
        await Assert.That(deletedIsActiveInTenantA).IsFalse();
    }

    private static Tenant CreateTenant(string slugPrefix)
    {
        return new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = $"Tenant User {slugPrefix}",
            Slug = $"{slugPrefix}-{Guid.NewGuid().ToString("N")[..8]}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!,
        };
    }

    private static User CreateUser(string emailPrefix)
    {
        return new User
        {
            Id = Guid.CreateVersion7(),
            Pii = new UserPii
            {
                Email = $"{emailPrefix}-{Guid.NewGuid():N}@example.com",
                FirstName = "Tenant",
                LastName = "User",
            },
            EmailVerified = true,
            CreatedAt = DateTime.UtcNow,
        };
    }

    private static Actor CreateActor(Guid userId, string displayName)
    {
        return new Actor
        {
            Id = Guid.CreateVersion7(),
            Pii = new ActorPii { DisplayName = displayName },
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            UserId = userId,
        };
    }

    private static TenantUser CreateTenantUser(
        Guid tenantId,
        Guid userId,
        Guid actorId,
        TenantUserStatusEnum status)
    {
        return new TenantUser
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = null!,
            UserId = userId,
            User = null!,
            ActorId = actorId,
            Actor = null,
            StatusId = (int)status,
            JoinedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        };
    }

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;
}
