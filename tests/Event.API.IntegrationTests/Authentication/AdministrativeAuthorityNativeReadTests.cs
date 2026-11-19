using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Authentication;

/// <summary>
/// Qualifies application-owned administrative scope reads under the actual
/// Global login without additional helper-role enrollment.
/// </summary>
[NotInParallel]
public sealed class AdministrativeAuthorityNativeReadTests
{
    [Test]
    public async Task ActualGlobalReadsPersistedAdministrativeScopesWithoutHelperEnrollment()
    {
        CancellationToken token = TestContext.Current!.Execution.CancellationToken;
        await using var postgres = new PostgreSqlContainerFixture();
        await postgres.InitializeAsync();
        var options = new DbContextOptionsBuilder<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(
            options, postgres.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        // Bound arrangement commands; the full graph still uses one Serializable save transaction.
        new NpgsqlDbContextOptionsBuilder(options).MaxBatchSize(1);
        await using var setup = new ExploreDbContext(options.Options);
        setup.EnableTenantFilterBypass("Administrative read arrangement under the dedicated test Migrator.");
        setup.BindTenantLease();

        DateTime now = new(2026, 11, 7, 0, 0, 0, DateTimeKind.Utc);
        var administrator = new User
        {
            Id = Guid.CreateVersion7(),
            Pii = new UserPii
            {
                Email = $"authority-{Guid.CreateVersion7():N}@example.test",
                FirstName = "Native",
                LastName = "Administrator"
            },
            CreatedAt = now
        };
        var other = new User
        {
            Id = Guid.CreateVersion7(),
            Pii = new UserPii
            {
                Email = $"authority-{Guid.CreateVersion7():N}@example.test",
                FirstName = "Other",
                LastName = "Administrator"
            },
            CreatedAt = now
        };
        setup.AddRange(administrator, other, new PlatformUserRole
        {
            Id = Guid.CreateVersion7(),
            UserId = administrator.Id,
            User = administrator,
            RoleId = (int)RoleEnum.Admin,
            Role = null!,
            GrantedAt = now
        });

        await setup.SaveChangesAsync(token);

        var globalOptions = postgres.GetNativeQueueOptions(PrimaryDatabaseRole.Global);
        var factory = new GlobalExploreDbContextFactory(globalOptions);
        await using (var selected = await factory.CreateDbContextAsync(token))
        {
            await selected.Database.OpenConnectionAsync(token);
            string login = await selected.Database.SqlQueryRaw<string>(
                "SELECT current_user::text AS \"Value\" WHERE current_user=session_user").SingleAsync(token);
            await Assert.That(login).IsEqualTo(globalOptions.Username);
            string role = await selected.Database.SqlQueryRaw<string>(
                "SELECT current_setting('role') AS \"Value\"").SingleAsync(token);
            await Assert.That(role).IsEqualTo("none");
        }

        var repository = new AdministrativeAuthorityRepository(factory);
        // Qualify the instance read before arranging unrelated tenant-scope graphs.
        await Assert.That(await repository.IsInstanceAdminAsync(administrator.Id, token)).IsTrue();
        await Assert.That(await repository.IsInstanceAdminAsync(other.Id, token)).IsFalse();

        // Two live scopes, another user's scope, a revoked grant, a suspended
        // membership, a deleted membership, and deleted organization/group members.
        var tenants = new List<Tenant>();
        var organizationIds = new List<Guid>();
        var groupIds = new List<Guid>();
        for (int index = 0; index < 7; index++)
        {
            Guid tenantId = Guid.CreateVersion7();
            var tenant = new Tenant
            {
                Id = tenantId,
                FullName = $"Native authority {index}",
                Slug = $"native-authority-{tenantId:N}",
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!,
                CreatedAt = now
            };
            tenants.Add(tenant);
            User subject = index == 2 ? other : administrator;
            var membership = new TenantUser
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenant.Id,
                Tenant = tenant,
                UserId = subject.Id,
                User = subject,
                StatusId = (int)(index == 4 ? TenantUserStatusEnum.Suspended : TenantUserStatusEnum.Active),
                IsDeleted = index == 5,
                DeletedAt = index == 5 ? now : null,
                JoinedAt = now,
                CreatedAt = now
            };
            setup.Add(new TenantUserRoleGrant
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenant.Id,
                Tenant = tenant,
                TenantUserId = membership.Id,
                TenantUser = membership,
                RoleId = (int)RoleEnum.TenantAdmin,
                Role = null!,
                RoleScopeId = (int)RoleScopeEnum.Tenant,
                GrantedAt = now,
                RevokedAt = index == 3 ? now : null,
                CreatedAt = now
            });

            if (index is 3 or 4 or 5)
                continue;

            var organization = new Organization
            {
                Id = Guid.CreateVersion7(),
                Pii = new OrganizationPii { FullName = $"Native organization {index}" },
                CreatedAt = now
            };
            var participation = new OrganizationTenant
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenant.Id,
                Tenant = tenant,
                OrganizationId = organization.Id,
                Organization = organization,
                ApprovalStatusId = (int)ApprovalStatusEnum.Approved,
                ApprovalStatus = null!,
                CreatedAt = now
            };
            organizationIds.Add(organization.Id);
            setup.Add(new OrganizationMember
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenant.Id,
                Tenant = tenant,
                OrganizationTenantId = participation.Id,
                OrganizationTenant = participation,
                UserId = subject.Id,
                User = subject,
                RoleId = (int)RoleEnum.OrgAdmin,
                Role = null!,
                IsDeleted = index == 6,
                DeletedAt = index == 6 ? now : null,
                CreatedAt = now
            });
            var group = new Group
            {
                Id = Guid.CreateVersion7(),
                FullName = $"Native group {index}",
                CreatedAt = now
            };
            var groupParticipation = new GroupTenant
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenant.Id,
                Tenant = tenant,
                GroupId = group.Id,
                Group = group,
                ApprovalStatusId = (int)ApprovalStatusEnum.Approved,
                ApprovalStatus = null!,
                CreatedAt = now
            };
            groupIds.Add(group.Id);
            setup.Add(new GroupMember
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenant.Id,
                Tenant = tenant,
                GroupTenantId = groupParticipation.Id,
                GroupTenant = groupParticipation,
                UserId = subject.Id,
                User = subject,
                RoleId = (int)RoleEnum.GroupAdmin,
                Role = null!,
                IsDeleted = index == 6,
                DeletedAt = index == 6 ? now : null,
                CreatedAt = now
            });
        }
        await setup.SaveChangesAsync(token);

        var tenantScopes = await repository.GetAdminTenantIdsAsync(administrator.Id, token);
        await Assert.That(tenantScopes.Order().SequenceEqual(
            new[] { tenants[0].Id, tenants[1].Id, tenants[6].Id }.Order())).IsTrue();
        var organizationScopes = await repository.GetAdminOrganizationIdsAsync(administrator.Id, token);
        await Assert.That(organizationScopes.Order().SequenceEqual(
            new[] { organizationIds[0], organizationIds[1] }.Order())).IsTrue();
        var groupScopes = await repository.GetAdminGroupIdsAsync(administrator.Id, token);
        await Assert.That(groupScopes.Order().SequenceEqual(
            new[] { groupIds[0], groupIds[1] }.Order())).IsTrue();
        var otherTenants = await repository.GetAdminTenantIdsAsync(other.Id, token);
        await Assert.That(otherTenants.SequenceEqual(new[] { tenants[2].Id })).IsTrue();
        var otherOrganizations = await repository.GetAdminOrganizationIdsAsync(other.Id, token);
        await Assert.That(otherOrganizations.SequenceEqual(new[] { organizationIds[2] })).IsTrue();
        var otherGroups = await repository.GetAdminGroupIdsAsync(other.Id, token);
        await Assert.That(otherGroups.SequenceEqual(new[] { groupIds[2] })).IsTrue();
        var scopedOrganizations = await repository.GetAdminOrganizationIdsAsync(
            administrator.Id, tenants[1].Id, token);
        await Assert.That(scopedOrganizations.SequenceEqual(new[] { organizationIds[1] })).IsTrue();
        var scopedGroups = await repository.GetAdminGroupIdsAsync(administrator.Id, tenants[1].Id, token);
        await Assert.That(scopedGroups.SequenceEqual(new[] { groupIds[1] })).IsTrue();
        await Assert.That(await repository.GetAdminOrganizationIdsAsync(
            administrator.Id, tenants[2].Id, token)).IsEmpty();
        await Assert.That(await repository.GetAdminGroupIdsAsync(
            administrator.Id, tenants[2].Id, token)).IsEmpty();
    }

}
