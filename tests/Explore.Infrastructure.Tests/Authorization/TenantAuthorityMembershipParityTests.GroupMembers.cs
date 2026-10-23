using Explore.Application.Authorization;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Authorization;

public sealed partial class TenantAuthorityMembershipParityTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Machine_group_authority_projects_the_same_bound_group_scopes_as_local_checks(bool userOwned)
    {
        await using var database = new AuthorityDatabase();
        await database.InitializeAsync(MembershipState.Revoked);
        var groupId = await SeedGroupAuthorityAsync(database, "administrator");
        var machine = Substitute.For<IMachinePrincipalAccessor>();
        machine.IsMachineCaller.Returns(true);
        machine.Current.Returns(new ApiKeyPrincipalContext("group-membership-key", database.TenantId,
            userOwned ? ExternalApiKeyOwnerType.User : ExternalApiKeyOwnerType.Group,
            userOwned ? database.UserId : groupId, ["groups:write"]));
        using var localCache = new MemoryCache(new MemoryCacheOptions());
        var local = database.CreateProvider("local", localCache, machine, new CategoryPolicyBoundary());
        using var remoteCache = new MemoryCache(new MemoryCacheOptions());
        var remote = new CategoryPolicyBoundary();
        var cerbos = database.CreateProvider("cerbos", remoteCache, machine, remote);
        foreach (var action in new[] { AuthorizationActions.Create, AuthorizationActions.Update, AuthorizationActions.Delete })
        {
            await Assert.That((await local.AuthorizeAsync(GroupMemberRequest(database.TenantId, groupId, action))).IsAllowed).IsTrue();
            await Assert.That((await local.AuthorizeAsync(GroupMemberRequest(database.ForeignTenantId, groupId, action))).IsAllowed).IsFalse();
        }
        await cerbos.AuthorizeAsync(GroupMemberRequest(database.TenantId, groupId, AuthorizationActions.Update));
        foreach (var action in new[] { "Create", "Update", "Delete" })
        {
            var scopes = remote.Requests.Single().Principal.Attr[$"groupMember{action}Scopes"].ListValue.Values
                .Select(value => value.StringValue).ToArray();
            await Assert.That(scopes).Contains($"{database.TenantId:D}/{groupId:D}");
            await Assert.That(scopes).DoesNotContain($"{database.ForeignTenantId:D}/{groupId:D}");
        }
    }

    [Test]
    [Arguments("administrator")]
    [Arguments("delegated")]
    [Arguments("ordinary")]
    [Arguments("inactive-permission")]
    [Arguments("deleted-membership")]
    [Arguments("organization-administrator")]
    public async Task Group_membership_single_and_batch_use_the_exact_group_permission(string scenario)
    {
        await using var database = new AuthorityDatabase();
        await database.InitializeAsync(MembershipState.Revoked);
        var groupId = await SeedGroupAuthorityAsync(database, scenario);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = database.CreateProvider("local", cache, Substitute.For<IMachinePrincipalAccessor>(), new CategoryPolicyBoundary());
        var foreignGroup = Guid.CreateVersion7();
        foreach (var action in new[] { AuthorizationActions.Create, AuthorizationActions.Update, AuthorizationActions.Delete })
        {
            var expected = scenario == "administrator" || scenario == "delegated" && action == AuthorizationActions.Update;
            var own = GroupMemberRequest(database.TenantId, groupId, action);
            var other = GroupMemberRequest(database.TenantId, foreignGroup, action);
            var wrongTenant = GroupMemberRequest(database.ForeignTenantId, groupId, action);
            await Assert.That((await provider.AuthorizeAsync(own)).IsAllowed).IsEqualTo(expected);
            var decisions = await provider.AuthorizeBatchAsync([own, other, wrongTenant]);
            await Assert.That(decisions[0].IsAllowed).IsEqualTo(expected);
            await Assert.That(decisions[1].IsAllowed).IsFalse();
            await Assert.That(decisions[2].IsAllowed).IsFalse();
        }
    }

    [Test]
    [Arguments("administrator")]
    [Arguments("delegated")]
    [Arguments("inactive-permission")]
    [Arguments("deleted-membership")]
    public async Task Cerbos_group_permissions_project_persisted_tenant_group_and_action_without_testing_a_fake_policy(string scenario)
    {
        await using var database = new AuthorityDatabase();
        await database.InitializeAsync(MembershipState.Revoked);
        var groupId = await SeedGroupAuthorityAsync(database, scenario);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var remote = new CategoryPolicyBoundary();
        var provider = database.CreateProvider("cerbos", cache, Substitute.For<IMachinePrincipalAccessor>(), remote);
        await provider.AuthorizeAsync(GroupMemberRequest(database.TenantId, groupId, AuthorizationActions.Update));
        var principal = remote.Requests.Single().Principal;
        foreach (var action in new[] { "Create", "Update", "Delete" })
        {
            var scopes = principal.Attr[$"groupMember{action}Scopes"].ListValue.Values.Select(value => value.StringValue).ToArray();
            var expected = scenario == "administrator" || scenario == "delegated" && action == "Update";
            await Assert.That(scopes.Contains($"{database.TenantId:D}/{groupId:D}")).IsEqualTo(expected);
            await Assert.That(scopes).DoesNotContain($"{database.ForeignTenantId:D}/{groupId:D}");
        }
    }

    private static AuthorizationRequest GroupMemberRequest(Guid tenantId, Guid groupId, string action) => new(
        AuthorizationCapabilityCatalog.Require(ResourceKinds.GroupMember, action), Guid.CreateVersion7().ToString("D"),
        Facts: new GroupMemberAuthorizationFacts(tenantId, groupId, null, null));

    private static async Task<Guid> SeedGroupAuthorityAsync(AuthorityDatabase database, string scenario)
    {
        var context = database.Context;
        context.RoleScopes.AddRange(
            new RoleScope { Id = (int)RoleScopeEnum.Group, MasterCode = "group", FullName = "Group" },
            new RoleScope { Id = (int)RoleScopeEnum.Organization, MasterCode = "organization", FullName = "Organization" });
        foreach (var role in new[] { RoleEnum.GroupAdmin, RoleEnum.GroupModerator, RoleEnum.GroupMember, RoleEnum.OrgAdmin })
            context.Roles.Add(new Role
            {
                Id = (int)role,
                MasterCode = role.ToString(),
                FullName = role.ToString(),
                Scope = role == RoleEnum.OrgAdmin ? RoleScopeEnum.Organization : RoleScopeEnum.Group
            });
        var approval = new ApprovalStatus { Id = (int)ApprovalStatusEnum.Approved, MasterCode = "approved", FullName = "Approved" };
        context.ApprovalStatuses.Add(approval);
        var group = new Group { Id = Guid.CreateVersion7(), FullName = "Membership authority" };
        var participation = new GroupTenant
        {
            Id = Guid.CreateVersion7(),
            TenantId = database.TenantId,
            Tenant = null!,
            Group = group,
            GroupId = group.Id,
            ApprovalStatus = approval,
            ApprovalStatusId = approval.Id
        };
        context.GroupMembers.Add(new GroupMember
        {
            Id = Guid.CreateVersion7(),
            TenantId = database.TenantId,
            Tenant = null!,
            GroupTenant = participation,
            GroupTenantId = participation.Id,
            UserId = database.UserId,
            User = null!,
            Role = null!,
            RoleId = (int)(scenario is "administrator" or "deleted-membership" ? RoleEnum.GroupAdmin
                : scenario is "delegated" or "inactive-permission" ? RoleEnum.GroupModerator : RoleEnum.GroupMember),
            IsDeleted = scenario == "deleted-membership"
        });
        var permission = new Permission
        {
            Id = 9101,
            MasterCode = PermissionCodes.GroupMemberUpdate,
            ResourceKind = ResourceKinds.GroupMember,
            Action = AuthorizationActions.Update,
            FullName = "Change group membership",
            GroupName = "Group",
            Scope = RoleScopeEnum.Group,
            IsActive = scenario != "inactive-permission"
        };
        context.Set<RolePermission>().Add(new RolePermission
        {
            RoleId = (int)RoleEnum.GroupModerator,
            Role = null!,
            Permission = permission,
            PermissionId = permission.Id
        });
        if (scenario == "organization-administrator")
        {
            var organization = new Organization { Id = Guid.CreateVersion7(), Pii = new OrganizationPii { FullName = "Unrelated organization" } };
            var organizationTenant = new OrganizationTenant
            {
                Id = Guid.CreateVersion7(),
                TenantId = database.TenantId,
                Tenant = null!,
                Organization = organization,
                OrganizationId = organization.Id,
                ApprovalStatus = approval,
                ApprovalStatusId = approval.Id
            };
            context.OrganizationMembers.Add(new OrganizationMember
            {
                Id = Guid.CreateVersion7(),
                TenantId = database.TenantId,
                Tenant = null!,
                OrganizationTenant = organizationTenant,
                OrganizationTenantId = organizationTenant.Id,
                UserId = database.UserId,
                User = null!,
                Role = null!,
                RoleId = (int)RoleEnum.OrgAdmin
            });
        }
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        return group.Id;
    }
}
