using Explore.Application.Contracts.Services;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Event.Persistence.IntegrationTests;

[NotInParallel("EventResourcePersistence")]
[ClassDataSource<EventResourcePersistenceTests.TestDatabase>(Shared = SharedType.PerClass)]
public sealed class EventResourceParentModerationPersistenceTests(EventResourcePersistenceTests.TestDatabase database)
{
    private static readonly DateTime Now = new(2040, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    [Arguments("active", true)]
    [Arguments("wrong-tenant", false)]
    [Arguments("revoked", false)]
    [Arguments("inactive", false)]
    [Arguments("deleted", false)]
    [Arguments("tenant-moderator", false)]
    [Arguments("platform-admin", true)]
    [Arguments("platform-moderator", false)]
    [Arguments("machine", false)]
    public async Task OnlyNativePersistedScopedAdministratorsSatisfyModerationEligibility(string state, bool expected)
    {
        var scope = await database.SeedScopeAsync();
        Guid userId;
        var resource = EventResourcePersistenceTests.CreateDraft(scope.TenantAId, scope.EventAId);
        await using (var seed = database.CreateContext())
        {
            userId = (await seed.Actors.Where(value => value.Id == scope.ActorId).Select(value => value.UserId).SingleAsync())!.Value;
            var member = new TenantUser
            {
                Id = Guid.CreateVersion7(), TenantId = state == "wrong-tenant" ? scope.TenantBId : scope.TenantAId,
                Tenant = null!, UserId = userId, User = null!, ActorId = scope.ActorId,
                StatusId = state == "inactive" ? (int)TenantUserStatusEnum.Suspended : (int)TenantUserStatusEnum.Active,
                IsDeleted = state == "deleted", CreatedAt = Now
            };
            seed.AddRange(member, resource);
            if (state.StartsWith("platform-", StringComparison.Ordinal))
                seed.PlatformUserRoles.Add(new PlatformUserRole
                {
                    Id = Guid.CreateVersion7(), UserId = userId, User = null!,
                    RoleId = state == "platform-admin" ? (int)RoleEnum.Admin : (int)RoleEnum.Moderator,
                    Role = null!, GrantedAt = Now
                });
            else
                seed.TenantUserRoleGrants.Add(new TenantUserRoleGrant
                {
                    Id = Guid.CreateVersion7(), TenantId = member.TenantId, Tenant = null!, TenantUserId = member.Id,
                    TenantUser = member, RoleId = state == "tenant-moderator" ? (int)RoleEnum.TenantModerator : (int)RoleEnum.TenantAdmin,
                    Role = null!, RoleScopeId = (int)RoleScopeEnum.Tenant, GrantedAt = Now,
                    RevokedAt = state == "revoked" ? Now : null
                });
            await seed.SaveChangesAsync();
        }
        await using var context = database.CreateContext();
        var facts = (await Reader(context).ReadAsync(new(scope.TenantAId, resource.Id, userId, state == "machine", "moderate"),
            new(Now), default))!;
        await Assert.That(facts.Management.Moderation.IsEffectiveAt(new(Now))).IsEqualTo(expected);
        var view = (await Reader(context).ReadAsync(new(scope.TenantAId, resource.Id, userId, state == "machine", "view"),
            new(Now), default))!;
        await Assert.That(view.Management.Moderation.IsEffectiveAt(new(Now))).IsEqualTo(expected);
        await Assert.That(facts.Management.ManagementCeiling).IsTrue();
        await Assert.That(facts.Management.PublicationCeiling).IsTrue();
        await Assert.That(facts.Access.PayloadSafetySatisfied).IsFalse();
        await Assert.That(facts.Management.Permissions).IsEmpty();
        if (state != "machine")
        {
            await Assert.That(facts.ParentModeration).IsNotNull();
            var parent = facts.ParentModeration!.Evaluate(new(""), new(Now));
            await Assert.That(parent.Principal.CanModerate(scope.TenantAId)).IsEqualTo(expected);
            await Assert.That(parent.Resource.UserId).IsEqualTo(userId);
            await Assert.That(parent.Resource.ActorId).IsEqualTo(scope.ActorId);
            await Assert.That(parent.Principal.EventAssignments.Single().EventId).IsEqualTo(scope.EventAId);
        }
        else await Assert.That(facts.ParentModeration).IsNull();
    }

    [Test]
    public async Task FreshModerationReadsObserveRevocationDespitePretrackedGrantAndMembership()
    {
        var scope = await database.SeedScopeAsync();
        Guid userId;
        Guid grantId = Guid.CreateVersion7();
        var resource = EventResourcePersistenceTests.CreateDraft(scope.TenantAId, scope.EventAId);
        await using (var seed = database.CreateContext())
        {
            userId = (await seed.Actors.Where(value => value.Id == scope.ActorId).Select(value => value.UserId).SingleAsync())!.Value;
            var member = new TenantUser
            {
                Id = Guid.CreateVersion7(), TenantId = scope.TenantAId, Tenant = null!, UserId = userId,
                User = null!, ActorId = scope.ActorId, StatusId = (int)TenantUserStatusEnum.Active, CreatedAt = Now
            };
            seed.AddRange(member, resource, new TenantUserRoleGrant
            {
                Id = grantId, TenantId = member.TenantId, Tenant = null!, TenantUserId = member.Id, TenantUser = member,
                RoleId = (int)RoleEnum.TenantAdmin, Role = null!, RoleScopeId = (int)RoleScopeEnum.Tenant, GrantedAt = Now
            });
            await seed.SaveChangesAsync();
        }
        await using var stale = database.CreateContext();
        var tracked = await stale.TenantUserRoleGrants.Include(value => value.TenantUser).SingleAsync(value => value.Id == grantId);
        var reader = Reader(stale);
        var request = new EventResourceAuthorityRequest(scope.TenantAId, resource.Id, userId, false, "moderate");
        await Assert.That((await reader.ReadAsync(request, new(Now), default))!.Management.Moderation.IsCurrent).IsTrue();
        await using (var writer = database.CreateContext())
            await writer.TenantUserRoleGrants.Where(value => value.Id == grantId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.RevokedAt, (DateTime?)Now));
        await Assert.That(tracked.RevokedAt).IsNull();
        var current = (await reader.ReadAsync(request, new(Now), default))!;
        await Assert.That(current.Management.Moderation.IsCurrent).IsFalse();
        await Assert.That(current.ParentModeration!.Evaluate(new(""), new(Now)).Principal.AdminTenantIds).IsEmpty();
    }

    [Test]
    public async Task NativeAssignmentBatchIncludesSiblingParentsAndReevaluatesWindowsWithoutAnotherRead()
    {
        var scope = await database.SeedScopeAsync();
        Guid userId;
        var first = EventResourcePersistenceTests.CreateDraft(scope.TenantAId, scope.EventAId);
        var sibling = EventResourcePersistenceTests.CreateDraft(scope.TenantAId, scope.EventCId);
        await using (var seed = database.CreateContext())
        {
            userId = (await seed.Actors.Where(value => value.Id == scope.ActorId).Select(value => value.UserId).SingleAsync())!.Value;
            seed.AddRange(first, sibling,
                EventRoleAssignment.Create(scope.TenantAId, scope.EventAId, userId, (int)RoleEnum.EventManager,
                    EventRoleAssignmentStatus.Active, Now.AddMinutes(-1), Now.AddSeconds(1), userId),
                EventRoleAssignment.Create(scope.TenantAId, scope.EventCId, userId, (int)RoleEnum.CheckInStaff,
                    EventRoleAssignmentStatus.Active, Now.AddSeconds(1), Now.AddHours(1), userId));
            await seed.SaveChangesAsync();
        }
        await using var context = database.CreateContext();
        var reader = Reader(context);
        var snapshots = await reader.ReadBatchAsync([
            new(scope.TenantAId, first.Id, userId, false, "moderate"),
            new(scope.TenantAId, sibling.Id, userId, false, "moderate")], new(Now), default);
        var frozen = snapshots[0]!.ParentModeration!;
        var before = frozen.Evaluate(new(""), new(Now)).Principal;
        var after = frozen.Evaluate(new(""), new(Now.AddSeconds(1))).Principal;
        await Assert.That(before).IsEqualTo(snapshots[1]!.ParentModeration!.Evaluate(new(""), new(Now)).Principal);
        await Assert.That(before.EventAssignments.Single(value => value.EventId == scope.EventAId).Roles)
            .IsEquivalentTo(new[] { "event.manager" });
        await Assert.That(before.EventAssignments.Single(value => value.EventId == scope.EventCId).Roles).IsEmpty();
        await Assert.That(after.EventAssignments.Single(value => value.EventId == scope.EventAId).Roles).IsEmpty();
        await Assert.That(after.EventAssignments.Single(value => value.EventId == scope.EventCId).Roles)
            .IsEquivalentTo(new[] { "event.check_in_staff" });
        await Assert.That(before == after).IsFalse();
        await Assert.That(snapshots[0]!.Management.Moderation.IsCurrent).IsFalse();
        var mixed = await reader.ReadBatchAsync([
            new(scope.TenantAId, first.Id, userId, false, "moderate"),
            new(scope.TenantAId, sibling.Id, userId, false, "view")], new(Now), default);
        await Assert.That(mixed[0]!.ParentModeration!.Evaluate(new(""), new(Now)).Principal.EventAssignments.Single().EventId)
            .IsEqualTo(scope.EventAId);
    }

    [Test]
    public async Task FrozenOrganizationGroupAndParentFactsMatchNativePersistedPrincipalSemantics()
    {
        var scope = await database.SeedScopeAsync();
        Guid userId;
        Guid organizationId = Guid.CreateVersion7(), groupId = Guid.CreateVersion7(), organizerId = Guid.CreateVersion7();
        var resource = EventResourcePersistenceTests.CreateDraft(scope.TenantAId, scope.EventAId);
        await using (var seed = database.CreateContext())
        {
            userId = (await seed.Actors.Where(value => value.Id == scope.ActorId).Select(value => value.UserId).SingleAsync())!.Value;
            var organization = new Organization
            {
                Id = organizationId, Pii = new() { FullName = "Principal organization" }, ConcurrencyStamp = Guid.CreateVersion7()
            };
            var group = new Group { Id = groupId, FullName = "Principal group", ConcurrencyStamp = Guid.CreateVersion7() };
            var orgTenant = new OrganizationTenant
            {
                Id = Guid.CreateVersion7(), TenantId = scope.TenantAId, Tenant = null!, OrganizationId = organizationId,
                Organization = organization, ApprovalStatusId = (int)ApprovalStatusEnum.Approved, ApprovalStatus = null!,
                IsOrganizerEligible = true, ConcurrencyStamp = Guid.CreateVersion7()
            };
            var groupTenant = new GroupTenant
            {
                Id = Guid.CreateVersion7(), TenantId = scope.TenantAId, Tenant = null!, GroupId = groupId, Group = group,
                ApprovalStatusId = (int)ApprovalStatusEnum.Approved, ApprovalStatus = null!, IsOrganizerEligible = true,
                ConcurrencyStamp = Guid.CreateVersion7()
            };
            var organizer = new Actor
            {
                Id = organizerId, ActorTypeId = (int)ActorTypeEnum.Group, ActorType = null!, GroupId = groupId,
                Pii = new() { DisplayName = "Group organizer" }
            };
            seed.AddRange(resource, orgTenant, groupTenant, organizer, new OrganizationMember
            {
                Id = Guid.CreateVersion7(), TenantId = scope.TenantAId, Tenant = null!, OrganizationTenantId = orgTenant.Id,
                OrganizationTenant = orgTenant, UserId = userId, User = null!, RoleId = (int)RoleEnum.OrgAdmin, Role = null!
            }, new GroupMember
            {
                Id = Guid.CreateVersion7(), TenantId = scope.TenantAId, Tenant = null!, GroupTenantId = groupTenant.Id,
                GroupTenant = groupTenant, UserId = userId, User = null!, RoleId = (int)RoleEnum.GroupAdmin, Role = null!
            });
            var parent = await seed.Events.SingleAsync(value => value.Id == scope.EventAId);
            parent.OrganizerActorId = organizerId;
            parent.SubmittedByUserId = userId;
            var permissions = await seed.Permissions.Where(value => value.MasterCode == PermissionCodes.EventCreate
                || value.MasterCode == PermissionCodes.EventManageFinance).ToListAsync();
            foreach (var permission in permissions)
            foreach (int roleId in new[] { (int)RoleEnum.OrgAdmin, (int)RoleEnum.GroupAdmin })
                if (!await seed.RolePermissions.AnyAsync(value => value.RoleId == roleId && value.PermissionId == permission.Id))
                    seed.RolePermissions.Add(new() { RoleId = roleId, Role = null!, PermissionId = permission.Id,
                        Permission = permission, GrantedAt = Now });
            await seed.SaveChangesAsync();
        }
        await using var context = database.CreateContext();
        var reader = Reader(context);
        var request = new EventResourceAuthorityRequest(scope.TenantAId, resource.Id, userId, false, "moderate");
        var facts = (await reader.ReadAsync(request, new(Now), default))!.ParentModeration!;
        var frozen = facts.Evaluate(new(""), new(Now));
        var organizations = new OrganizationMemberRepository(context);
        var groups = new GroupMemberRepository(context);
        await Assert.That(frozen.Principal.AdminOrganizationIds).IsEquivalentTo(new[] { organizationId });
        await Assert.That(frozen.Principal.AdminGroupIds).IsEquivalentTo(new[] { groupId });
        await Assert.That(frozen.Principal.EventCreateOrganizationIds).IsNotEmpty();
        await Assert.That(frozen.Principal.EventCreateGroupIds).IsNotEmpty();
        await Assert.That(frozen.Principal.EventCreateOrganizationIds).IsEquivalentTo(
            await organizations.GetOrganizationIdsWhereUserHasPermission(userId, PermissionCodes.EventCreate));
        await Assert.That(frozen.Principal.EventCreateGroupIds).IsEquivalentTo(
            await groups.GetGroupIdsWhereUserHasPermission(userId, PermissionCodes.EventCreate));
        await Assert.That(frozen.Principal.EventFinanceOrganizationIds).IsEquivalentTo(
            await organizations.GetOrganizationIdsWhereUserHasPermission(userId, PermissionCodes.EventManageFinance));
        await Assert.That(frozen.Principal.EventFinanceGroupIds).IsEquivalentTo(
            await groups.GetGroupIdsWhereUserHasPermission(userId, PermissionCodes.EventManageFinance));
        await Assert.That(frozen.Principal.CanModerate(scope.TenantAId)).IsFalse();
        await Assert.That(frozen.Resource.UserId).IsEqualTo(userId);
        await Assert.That(frozen.Resource.OrganizerActorId).IsEqualTo(organizerId);
        await Assert.That(frozen.Resource.OrganizerGroupId).IsEqualTo(groupId);
        await Assert.That(frozen.Resource.SubmittedByUserId).IsEqualTo(userId);
        await using (var writer = database.CreateContext())
        {
            await writer.Groups.Where(value => value.Id == groupId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.IsDeleted, true));
            await writer.Events.Where(value => value.Id == scope.EventAId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.SubmittedByUserId, (Guid?)null));
        }
        var changed = (await reader.ReadAsync(request, new(Now), default))!.ParentModeration!.Evaluate(new(""), new(Now));
        await Assert.That(changed.Principal.AdminGroupIds).IsEmpty();
        // Native permission helpers use participation membership, not the admin graph's required Group join.
        await Assert.That(changed.Principal.EventCreateGroupIds).IsEquivalentTo(
            await groups.GetGroupIdsWhereUserHasPermission(userId, PermissionCodes.EventCreate));
        await Assert.That(changed.Resource.SubmittedByUserId).IsNull();
        await Assert.That(changed == frozen).IsFalse();
    }

    private static EventResourceAuthoritySnapshotReader Reader(ExploreDbContext context)
    {
        var governance = Substitute.For<IEventResourceGovernancePolicyReader>();
        governance.ReadAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(EventResourceGovernancePolicy.Default(long.MaxValue));
        return new(new EventResourceRepository(context), new EventAuthoritySnapshotService(context), governance);
    }
}
