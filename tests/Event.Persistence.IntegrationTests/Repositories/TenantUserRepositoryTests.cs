using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class TenantUserRepositoryTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task TenantUsers_ShouldAllowSameGlobalUserInMultipleTenantsButOnlyOncePerTenant()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var tenantA = await SeedTenantAsync(context, "tenant-user-a");
        var tenantB = await SeedTenantAsync(context, "tenant-user-b");
        var user = NewUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        context.TenantUsers.Add(NewTenantUser(tenantA.Id, user.Id, TenantUserStatusEnum.Active));
        context.TenantUsers.Add(NewTenantUser(tenantB.Id, user.Id, TenantUserStatusEnum.Suspended));
        await context.SaveChangesAsync();

        context.ChangeTracker.Clear();
        context.TenantUsers.Add(NewTenantUser(tenantA.Id, user.Id, TenantUserStatusEnum.Active));

        await Assert.ThrowsAsync<DbUpdateException>(async () => await context.SaveChangesAsync());
    }

    [Test]
    public async Task TenantUserRoleGrantAuthority_ShouldRequireActiveTenantUserStatePerTenant()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var activeTenant = await SeedTenantAsync(context, "authority-active");
        var suspendedTenant = await SeedTenantAsync(context, "authority-suspended");
        var bannedTenant = await SeedTenantAsync(context, "authority-banned");
        var removedTenant = await SeedTenantAsync(context, "authority-removed");
        var user = NewUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var activeTenantUser = NewTenantUser(activeTenant.Id, user.Id, TenantUserStatusEnum.Active);
        var suspendedTenantUser = NewTenantUser(suspendedTenant.Id, user.Id, TenantUserStatusEnum.Suspended);
        var bannedTenantUser = NewTenantUser(bannedTenant.Id, user.Id, TenantUserStatusEnum.Banned);
        var removedTenantUser = NewTenantUser(removedTenant.Id, user.Id, TenantUserStatusEnum.Removed);
        context.TenantUsers.AddRange(activeTenantUser, suspendedTenantUser, bannedTenantUser, removedTenantUser);
        context.TenantUserRoleGrants.AddRange(
            NewTenantAdmin(activeTenant.Id, activeTenantUser.Id),
            NewTenantAdmin(suspendedTenant.Id, suspendedTenantUser.Id),
            NewTenantAdmin(bannedTenant.Id, bannedTenantUser.Id),
            NewTenantAdmin(removedTenant.Id, removedTenantUser.Id));
        await context.SaveChangesAsync();
        var repository = new TenantUserRoleGrantRepository(context);

        await Assert.That(await repository.HasActiveTenantUserRoleGrant(activeTenant.Id, user.Id)).IsTrue();
        await Assert.That(await repository.IsTenantAdmin(activeTenant.Id, user.Id)).IsTrue();
        await Assert.That(await repository.HasActiveTenantUserRoleGrant(suspendedTenant.Id, user.Id)).IsFalse();
        await Assert.That(await repository.IsTenantAdmin(suspendedTenant.Id, user.Id)).IsFalse();
        await Assert.That(await repository.HasActiveTenantUserRoleGrant(bannedTenant.Id, user.Id)).IsFalse();
        await Assert.That(await repository.IsTenantAdmin(bannedTenant.Id, user.Id)).IsFalse();
        await Assert.That(await repository.HasActiveTenantUserRoleGrant(removedTenant.Id, user.Id)).IsFalse();
        await Assert.That(await repository.IsTenantAdmin(removedTenant.Id, user.Id)).IsFalse();
    }

    [Test]
    public async Task TenantUserRoleGrantAuthority_ShouldIgnoreSoftDeletedTenantUserWithoutChangingGlobalUser()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var tenant = await SeedTenantAsync(context, "authority-deleted");
        var user = NewUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var tenantUser = NewTenantUser(tenant.Id, user.Id, TenantUserStatusEnum.Active);
        tenantUser.IsDeleted = true;
        tenantUser.DeletedAt = DateTime.UtcNow;
        context.TenantUsers.Add(tenantUser);
        context.TenantUserRoleGrants.Add(NewTenantAdmin(tenant.Id, tenantUser.Id));
        await context.SaveChangesAsync();
        var repository = new TenantUserRoleGrantRepository(context);

        await Assert.That(await repository.HasActiveTenantUserRoleGrant(tenant.Id, user.Id)).IsFalse();
        await Assert.That(await repository.IsTenantAdmin(tenant.Id, user.Id)).IsFalse();
        var persistedUser = await context.Users.IgnoreQueryFilters().SingleAsync(x => x.Id == user.Id);
        await Assert.That(persistedUser.IsDeleted).IsFalse();
        await Assert.That(persistedUser.Pii.Email).IsEqualTo(user.Pii.Email);
    }

    [Test]
    public async Task TenantUserRoleGrant_ShouldRejectNonTenantScopedRole()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var tenant = await SeedTenantAsync(context, "role-grant-role-scope");
        var user = NewUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var tenantUser = NewTenantUser(tenant.Id, user.Id, TenantUserStatusEnum.Active);
        context.TenantUsers.Add(tenantUser);
        await context.SaveChangesAsync();

        context.TenantUserRoleGrants.Add(new TenantUserRoleGrant
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Tenant = null!,
            TenantUserId = tenantUser.Id,
            TenantUser = null!,
            RoleId = (int)RoleEnum.Admin,
            Role = null!,
            RoleScopeId = (int)RoleScopeEnum.Tenant,
            GrantedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        });

        await Assert.ThrowsAsync<DbUpdateException>(async () => await context.SaveChangesAsync());
    }

    [Test]
    public async Task TenantUserRoleGrant_ShouldRejectCrossTenantTenantUserReference()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var tenantA = await SeedTenantAsync(context, "role-grant-source");
        var tenantB = await SeedTenantAsync(context, "role-grant-target");
        var user = NewUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var tenantUser = NewTenantUser(tenantA.Id, user.Id, TenantUserStatusEnum.Active);
        context.TenantUsers.Add(tenantUser);
        await context.SaveChangesAsync();

        context.TenantUserRoleGrants.Add(NewTenantAdmin(tenantB.Id, tenantUser.Id));

        await Assert.ThrowsAsync<DbUpdateException>(async () => await context.SaveChangesAsync());
    }

    private static async Task<Tenant> SeedTenantAsync(ExploreDbContext context, string slugPrefix)
    {
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            FullName = $"Tenant User {slugPrefix}",
            Slug = $"tenant-user-{slugPrefix}-{Guid.NewGuid().ToString("N")[..8]}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!,
        };

        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();
        return tenant;
    }

    private static User NewUser() =>
        new()
        {
            Id = Guid.NewGuid(),
            Pii = new UserPii
            {
                Email = $"tenant-user-{Guid.NewGuid():N}@example.com",
                FirstName = "Amina",
                LastName = "Admin",
            },
            EmailVerified = true,
            CreatedAt = DateTime.UtcNow,
        };

    private static TenantUser NewTenantUser(Guid tenantId, Guid userId, TenantUserStatusEnum status) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Tenant = null!,
            UserId = userId,
            User = null!,
            StatusId = (int)status,
            JoinedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        };

    private static TenantUserRoleGrant NewTenantAdmin(Guid tenantId, Guid tenantUserId) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Tenant = null!,
            TenantUserId = tenantUserId,
            TenantUser = null!,
            RoleId = (int)RoleEnum.TenantAdmin,
            Role = null!,
            RoleScopeId = (int)RoleScopeEnum.Tenant,
            GrantedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        };
}
