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
public class TenantUserRoleGrantRepositoryBypassTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task RoleGrantBypasses_WithAmbientTenant_ReturnOnlyExplicitTenantAndUserMembershipRows()
    {
        await fixture.ResetAsync();
        await using var seedContext = fixture.CreateDbContext();

        var tenantA = CreateTenant("role-grant-a");
        var tenantB = CreateTenant("role-grant-b");
        seedContext.Tenants.AddRange(tenantA, tenantB);
        await seedContext.SaveChangesAsync();

        var primaryUser = CreateUser("role-grant-primary");
        var otherUser = CreateUser("role-grant-other");
        seedContext.Users.AddRange(primaryUser, otherUser);
        await seedContext.SaveChangesAsync();

        var tenantUserA = CreateTenantUser(tenantA.Id, primaryUser.Id);
        var tenantUserB = CreateTenantUser(tenantB.Id, primaryUser.Id);
        var otherTenantUserA = CreateTenantUser(tenantA.Id, otherUser.Id);
        seedContext.TenantUsers.AddRange(tenantUserA, tenantUserB, otherTenantUserA);
        await seedContext.SaveChangesAsync();

        var tenantAAdminGrant = CreateGrant(tenantA.Id, tenantUserA.Id, RoleEnum.TenantAdmin);
        var tenantBMemberGrant = CreateGrant(tenantB.Id, tenantUserB.Id, RoleEnum.TenantMember);
        var otherUserTenantAGrant = CreateGrant(tenantA.Id, otherTenantUserA.Id, RoleEnum.TenantMember);
        var revokedTenantAGrant = CreateGrant(tenantA.Id, tenantUserA.Id, RoleEnum.TenantModerator);
        revokedTenantAGrant.RevokedAt = new DateTime(2026, 1, 4, 0, 0, 0, DateTimeKind.Utc);
        seedContext.TenantUserRoleGrants.AddRange(
            tenantAAdminGrant,
            tenantBMemberGrant,
            otherUserTenantAGrant,
            revokedTenantAGrant);
        await seedContext.SaveChangesAsync();

        await using var tenantBContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantB.Id));
        var visibleWithoutBypass = await tenantBContext.TenantUserRoleGrants
            .AsNoTracking()
            .Select(grant => grant.Id)
            .ToListAsync();

        var repository = new TenantUserRoleGrantRepository(tenantBContext);
        var tenantAForPrimaryUser = await repository.GetByTenantAndUser(tenantA.Id, primaryUser.Id);
        var tenantAAdminByRole = await repository.GetByTenantUserAndRole(
            tenantA.Id,
            tenantUserA.Id,
            (int)RoleEnum.TenantAdmin);
        var tenantAGrants = await repository.GetByTenant(tenantA.Id);
        var primaryUserMemberships = await repository.GetByUserId(primaryUser.Id);
        var primaryUserHasTenantAGrant = await repository.HasActiveTenantUserRoleGrant(tenantA.Id, primaryUser.Id);
        var primaryUserIsTenantAAdmin = await repository.IsTenantAdmin(tenantA.Id, primaryUser.Id);
        var primaryUserIsTenantBAdmin = await repository.IsTenantAdmin(tenantB.Id, primaryUser.Id);

        await Assert.That(visibleWithoutBypass).IsEquivalentTo([tenantBMemberGrant.Id]);

        await Assert.That(tenantAForPrimaryUser).IsNotNull();
        await Assert.That(tenantAForPrimaryUser!.Id).IsEqualTo(tenantAAdminGrant.Id);
        await Assert.That(tenantAForPrimaryUser.TenantId).IsEqualTo(tenantA.Id);

        await Assert.That(tenantAAdminByRole).IsNotNull();
        await Assert.That(tenantAAdminByRole!.Id).IsEqualTo(tenantAAdminGrant.Id);

        await Assert.That(tenantAGrants.Select(grant => grant.Id))
            .IsEquivalentTo([tenantAAdminGrant.Id, otherUserTenantAGrant.Id]);
        await Assert.That(primaryUserMemberships.Select(grant => grant.Id))
            .IsEquivalentTo([tenantAAdminGrant.Id, tenantBMemberGrant.Id]);

        await Assert.That(primaryUserHasTenantAGrant).IsTrue();
        await Assert.That(primaryUserIsTenantAAdmin).IsTrue();
        await Assert.That(primaryUserIsTenantBAdmin).IsFalse();
    }

    private static Tenant CreateTenant(string slugPrefix)
    {
        return new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = $"Tenant Role Grant {slugPrefix}",
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
                FirstName = "Role",
                LastName = "Grant",
            },
            EmailVerified = true,
            CreatedAt = DateTime.UtcNow,
        };
    }

    private static TenantUser CreateTenantUser(Guid tenantId, Guid userId)
    {
        return new TenantUser
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = null!,
            UserId = userId,
            User = null!,
            StatusId = (int)TenantUserStatusEnum.Active,
            JoinedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        };
    }

    private static TenantUserRoleGrant CreateGrant(Guid tenantId, Guid tenantUserId, RoleEnum role)
    {
        return new TenantUserRoleGrant
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = null!,
            TenantUserId = tenantUserId,
            TenantUser = null!,
            RoleId = (int)role,
            Role = null!,
            RoleScopeId = (int)RoleScopeEnum.Tenant,
            GrantedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        };
    }

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;
}
