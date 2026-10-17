using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Application.Features.GroupMembers.Handlers.Commands;
using Explore.Application.Features.GroupMembers.Handlers.Queries;
using Explore.Application.Features.OrganizationMembers.Handlers.Commands;
using Explore.Application.Features.OrganizationMembers.Handlers.Queries;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class HumanAccountMembershipCustodyTests
{
    [Test]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(false, false)]
    public async Task UnannotatedGroupOwnerKeepsPermissionOrAdminWithoutACerbosDecision(bool administrator, bool permission)
    {
        await using var fixture = await CreateFixture();
        var target = await SeedMembers(fixture, group: true, secondAdmin: false);
        int roleId = administrator ? (int)RoleEnum.GroupAdmin : (int)RoleEnum.GroupModerator;
        await using (var seed = fixture.Native.CreateDatabase())
        {
            await seed.GroupMembers.Where(row => row.Id == target.Self)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.RoleId, roleId));
            var grant = await seed.Set<Permission>().SingleOrDefaultAsync(row => row.MasterCode == PermissionCodes.GroupMemberCreate);
            if (grant is null)
            {
                grant = new Permission
                {
                    Id = 99001,
                    ResourceKind = "islamuevent_group_member",
                    Action = "create",
                    MasterCode = PermissionCodes.GroupMemberCreate,
                    FullName = "Create group membership",
                    GroupName = "Groups",
                    RoleScopeId = (int)RoleScopeEnum.Group,
                    IsActive = true
                };
                seed.Add(grant);
            }
            grant.IsActive = true;
            var mapping = await seed.Set<RolePermission>()
                .SingleOrDefaultAsync(row => row.RoleId == roleId && row.PermissionId == grant.Id);
            if (permission && mapping is null)
                seed.Add(new RolePermission
                {
                    RoleId = roleId,
                    Role = null!,
                    PermissionId = grant.Id,
                    Permission = grant,
                    GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
                });
            if (!permission && mapping is not null) seed.Remove(mapping);
            var provider = await seed.SystemSettings.SingleAsync(row =>
                row.SettingKey == GovernanceSettingKeys.Security.AuthorizationProvider);
            provider.Value = "\"cerbos\"";
            await seed.SaveChangesAsync();
        }
        await using var request = new Request(fixture.Host.Services);
        await request.Authenticate(await fixture.SignInAsync());
        var result = await ActivatorUtilities.CreateInstance<AddGroupMemberCommandHandler>(request.Services)
            .ExecuteAsync(new()
            {
                RequesterUserId = fixture.Binding.LocalSubjectId.ToString("D"),
                AddGroupMemberDto = new() { GroupId = target.Owner, Email = target.OtherEmail, Role = RoleEnum.GroupMember }
            }, default);
        await Assert.That(result.IsSuccess).IsEqualTo(administrator || permission);
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.GroupMembers.AsNoTracking().AnyAsync(row =>
            row.GroupTenant.GroupId == target.Owner && row.UserId == target.OtherUser)).IsEqualTo(administrator || permission);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AnotherMembersRoleMustMatchOnlyTheOriginalRequestedTransition(bool group)
    {
        await using var fixture = await CreateFixture();
        var seed = await SeedMembers(fixture, group, secondAdmin: true);
        Guid target;
        await using (var observer = fixture.Native.CreateDatabase())
            target = group
                ? await observer.GroupMembers.Where(row => row.UserId == seed.OtherUser && row.GroupTenant.GroupId == seed.Owner)
                    .Select(row => row.Id).SingleAsync()
                : await observer.OrganizationMembers.Where(row => row.UserId == seed.OtherUser && row.OrganizationTenant.OrganizationId == seed.Owner)
                    .Select(row => row.Id).SingleAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.Authenticate(await fixture.SignInAsync());
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var owner = new AfterBody((ISerializableAttemptOwner)new EfCoreUnitOfWork(context), async token =>
        {
            if (group)
                await context.GroupMembers.Where(row => row.Id == target)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.RoleId, (int)RoleEnum.GroupModerator), token);
            else
                await context.OrganizationMembers.Where(row => row.Id == target)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.RoleId, (int)RoleEnum.OrgModerator), token);
        });
        async Task Invoke()
        {
            if (group)
                await ActivatorUtilities.CreateInstance<UpdateGroupMemberRoleCommandHandler>(request.Services, owner)
                    .ExecuteAsync(new()
                    {
                        RequesterUserId = fixture.Binding.LocalSubjectId.ToString("D"),
                        UpdateGroupMemberRoleDto = new() { Id = target, Role = RoleEnum.GroupMember }
                    }, default);
            else
                await ActivatorUtilities.CreateInstance<UpdateOrganizationMemberRoleCommandHandler>(request.Services, owner)
                    .ExecuteAsync(new()
                    {
                        RequesterUserId = fixture.Binding.LocalSubjectId.ToString("D"),
                        UpdateOrganizationMemberRoleDto = new() { Id = target, Role = RoleEnum.OrgMember }
                    }, default);
        }
        await Assert.ThrowsAsync<UnauthorizedAccessException>(Invoke);
        await using var persisted = fixture.Native.CreateDatabase();
        int role = group
            ? await persisted.GroupMembers.AsNoTracking().Where(row => row.Id == target).Select(row => row.RoleId).SingleAsync()
            : await persisted.OrganizationMembers.AsNoTracking().Where(row => row.Id == target).Select(row => row.RoleId).SingleAsync();
        await Assert.That(role).IsEqualTo(group ? (int)RoleEnum.GroupAdmin : (int)RoleEnum.OrgAdmin);
    }

    [Test]
    [Arguments(false, false, false)]
    [Arguments(false, true, false)]
    [Arguments(true, false, false)]
    [Arguments(true, true, false)]
    [Arguments(false, false, true)]
    [Arguments(false, true, true)]
    [Arguments(true, false, true)]
    [Arguments(true, true, true)]
    public async Task IntentionalSelfTransitionPreservesTheLastAdministrator(bool group, bool delete, bool last)
    {
        await using var fixture = await CreateFixture();
        var seed = await SeedMembers(fixture, group, secondAdmin: !last);
        await using var request = new Request(fixture.Host.Services);
        await request.Authenticate(await fixture.SignInAsync());
        string actor = fixture.Binding.LocalSubjectId.ToString("D");
        BaseCommandResponse<Guid> response;
        if (group)
        {
            if (delete)
                response = await ActivatorUtilities.CreateInstance<DeleteGroupMemberCommandHandler>(request.Services)
                    .ExecuteAsync(new() { MemberId = seed.Self, RequesterUserId = actor }, default);
            else
                response = await ActivatorUtilities.CreateInstance<UpdateGroupMemberRoleCommandHandler>(request.Services)
                    .ExecuteAsync(new()
                    {
                        RequesterUserId = actor,
                        UpdateGroupMemberRoleDto = new() { Id = seed.Self, Role = RoleEnum.GroupMember }
                    }, default);
        }
        else
        {
            if (delete)
                response = await ActivatorUtilities.CreateInstance<DeleteOrganizationMemberCommandHandler>(request.Services)
                    .ExecuteAsync(new() { MemberId = seed.Self, RequesterUserId = actor }, default);
            else
                response = await ActivatorUtilities.CreateInstance<UpdateOrganizationMemberRoleCommandHandler>(request.Services)
                    .ExecuteAsync(new()
                    {
                        RequesterUserId = actor,
                        UpdateOrganizationMemberRoleDto = new() { Id = seed.Self, Role = RoleEnum.OrgMember }
                    }, default);
        }
        await Assert.That(response.IsSuccess).IsEqualTo(!last);
        await using var observer = fixture.Native.CreateDatabase();
        var stored = group
            ? await observer.GroupMembers.IgnoreQueryFilters([QueryFilterNames.SoftDelete]).AsNoTracking()
                .Where(row => row.Id == seed.Self).Select(row => new { row.RoleId, row.IsDeleted, row.DeletedBy }).SingleAsync()
            : await observer.OrganizationMembers.IgnoreQueryFilters([QueryFilterNames.SoftDelete]).AsNoTracking()
                .Where(row => row.Id == seed.Self).Select(row => new { row.RoleId, row.IsDeleted, row.DeletedBy }).SingleAsync();
        await Assert.That(stored.IsDeleted).IsEqualTo(delete && !last);
        await Assert.That(stored.RoleId).IsEqualTo(!delete && !last
            ? group ? (int)RoleEnum.GroupMember : (int)RoleEnum.OrgMember
            : group ? (int)RoleEnum.GroupAdmin : (int)RoleEnum.OrgAdmin);
        if (delete && !last) await Assert.That(stored.DeletedBy).IsEqualTo(fixture.Binding.LocalSubjectId);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SelfDemotionCannotHideDriftInItsOriginalRoleDefinition(bool group)
    {
        await using var fixture = await CreateFixture();
        var seed = await SeedMembers(fixture, group, secondAdmin: true);
        await using var request = new Request(fixture.Host.Services);
        await request.Authenticate(await fixture.SignInAsync());
        await request.Services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(default);
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        int oldRole = group ? (int)RoleEnum.GroupAdmin : (int)RoleEnum.OrgAdmin;
        string originalCode = await context.Roles.Where(row => row.Id == oldRole).Select(row => row.MasterCode).SingleAsync();
        var owner = new AfterBody((ISerializableAttemptOwner)new EfCoreUnitOfWork(context), token =>
            context.Roles.Where(row => row.Id == oldRole)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.MasterCode, "drift.original.definition"), token));
        async Task Invoke()
        {
            BaseCommandResponse<Guid> result;
            if (group)
                result = await ActivatorUtilities.CreateInstance<UpdateGroupMemberRoleCommandHandler>(request.Services, owner)
                    .ExecuteAsync(new()
                    {
                        RequesterUserId = fixture.Binding.LocalSubjectId.ToString("D"),
                        UpdateGroupMemberRoleDto = new() { Id = seed.Self, Role = RoleEnum.GroupMember }
                    }, default);
            else
                result = await ActivatorUtilities.CreateInstance<UpdateOrganizationMemberRoleCommandHandler>(request.Services, owner)
                    .ExecuteAsync(new()
                    {
                        RequesterUserId = fixture.Binding.LocalSubjectId.ToString("D"),
                        UpdateOrganizationMemberRoleDto = new() { Id = seed.Self, Role = RoleEnum.OrgMember }
                    }, default);
            await Assert.That(result.IsSuccess).IsTrue();
        }
        await Assert.ThrowsAsync<UnauthorizedAccessException>(Invoke);
        await using var observer = fixture.Native.CreateDatabase();
        int role = group
            ? await observer.GroupMembers.AsNoTracking().Where(row => row.Id == seed.Self).Select(row => row.RoleId).SingleAsync()
            : await observer.OrganizationMembers.AsNoTracking().Where(row => row.Id == seed.Self).Select(row => row.RoleId).SingleAsync();
        await Assert.That(role).IsEqualTo(oldRole);
        await Assert.That(await observer.Roles.AsNoTracking().Where(row => row.Id == oldRole)
            .Select(row => row.MasterCode).SingleAsync()).IsEqualTo(originalCode);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AddAndDuplicateTargetUseTheSameSelectedOwner(bool group)
    {
        await using var fixture = await CreateFixture();
        var seed = await SeedMembers(fixture, group, secondAdmin: false);
        await using var request = new Request(fixture.Host.Services);
        await request.Authenticate(await fixture.SignInAsync());
        async Task<BaseCommandResponse<Guid>> Add()
        {
            if (group)
                return await ActivatorUtilities.CreateInstance<AddGroupMemberCommandHandler>(request.Services)
                    .ExecuteAsync(new()
                    {
                        RequesterUserId = fixture.Binding.LocalSubjectId.ToString("D"),
                        AddGroupMemberDto = new() { GroupId = seed.Owner, Email = seed.OtherEmail, Role = RoleEnum.GroupMember }
                    }, default);
            return await ActivatorUtilities.CreateInstance<AddOrganizationMemberCommandHandler>(request.Services)
                .ExecuteAsync(new()
                {
                    RequesterUserId = fixture.Binding.LocalSubjectId.ToString("D"),
                    TenantId = PlatformDefaults.DefaultTenantId,
                    AddOrganizationMemberDto = new() { OrganizationId = seed.Owner, Email = seed.OtherEmail, Role = RoleEnum.OrgMember }
                }, default);
        }
        var first = await Add();
        await Assert.That(first.IsSuccess).IsTrue();
        var duplicate = await Add();
        await Assert.That(duplicate.IsSuccess).IsFalse();
        await using var observer = fixture.Native.CreateDatabase();
        int count = group
            ? await observer.GroupMembers.CountAsync(row => row.GroupTenant.GroupId == seed.Owner && row.UserId == seed.OtherUser)
            : await observer.OrganizationMembers.CountAsync(row => row.OrganizationTenant.OrganizationId == seed.Owner && row.UserId == seed.OtherUser);
        await Assert.That(count).IsEqualTo(1);
    }

    [Test]
    public async Task PublicGroupQueriesDoNotRequireAHumanCredential()
    {
        await using var fixture = await CreateFixture();
        var seed = await SeedMembers(fixture, group: true, secondAdmin: false);
        await using var request = new Request(fixture.Host.Services);
        var detail = await ActivatorUtilities.CreateInstance<GetGroupMemberDetailsRequestHandler>(request.Services)
            .QueryAsync(new(seed.Self), default);
        var members = await ActivatorUtilities.CreateInstance<GetGroupMembersRequestHandler>(request.Services)
            .QueryAsync(new(seed.Owner), default);
        await Assert.That(detail).IsNotNull();
        await Assert.That(members.Count).IsEqualTo(1);
    }

    [Test]
    public async Task OrganizationCollectionAndDetailRetainOriginalSelectedPolicy()
    {
        await using var fixture = await CreateFixture();
        var seed = await SeedMembers(fixture, group: false, secondAdmin: false);
        await using var request = new Request(fixture.Host.Services);
        await request.Authenticate(await fixture.SignInAsync());
        var detail = await ActivatorUtilities.CreateInstance<GetOrganizationMemberDetailsRequestHandler>(request.Services)
            .QueryAsync(new() { Id = seed.Self, TenantId = PlatformDefaults.DefaultTenantId }, default);
        var members = await ActivatorUtilities.CreateInstance<GetOrganizationMembersRequestHandler>(request.Services)
            .QueryAsync(new() { OrganizationId = seed.Owner, TenantId = PlatformDefaults.DefaultTenantId }, default);
        await Assert.That(detail).IsNotNull();
        await Assert.That(members.Count).IsEqualTo(1);
        await Assert.That(detail!.Id).IsEqualTo(seed.Self);
        await Assert.That(detail.TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
        await Assert.That(detail.UserId).IsEqualTo(fixture.Binding.LocalSubjectId);
        await Assert.That(detail.OrganizationFullName).IsEqualTo("Account custody organization");
        await Assert.That(detail.RoleId).IsEqualTo((int)RoleEnum.OrgAdmin);
        await Assert.That(members.Single()).IsEqualTo(detail);
        await Assert.ThrowsAsync<Explore.Application.Exceptions.AuthorizationException>(() =>
            ActivatorUtilities.CreateInstance<GetOrganizationMemberDetailsRequestHandler>(request.Services)
                .QueryAsync(new() { Id = Guid.CreateVersion7(), TenantId = PlatformDefaults.DefaultTenantId }, default));
    }

    private sealed record MemberSeed(Guid Owner, Guid Self, Guid OtherUser, string OtherEmail,
        LocalAuthRequestDto OtherLogin);

    private static async Task<MemberSeed> SeedMembers(LocalIdentityLifecycleHttpFixture fixture, bool group, bool secondAdmin)
    {
        var login = await fixture.Native.SeedLocalUserAsync(emailConfirmed: true);
        await using var seed = fixture.Native.CreateDatabase();
        var other = await seed.Users.Include(row => row.Pii).SingleAsync(row => row.Pii.Email == login.Identifier);
        Guid ownerId = Guid.CreateVersion7(), placementId = Guid.CreateVersion7(), self = Guid.CreateVersion7();
        if (group)
        {
            var participation = new GroupTenant
            {
                Id = placementId,
                TenantId = PlatformDefaults.DefaultTenantId,
                Tenant = null!,
                GroupId = ownerId,
                Group = new Group { Id = ownerId, FullName = "Account custody group", ConcurrencyStamp = Guid.CreateVersion7() },
                ApprovalStatusId = (int)ApprovalStatusEnum.Pending,
                ApprovalStatus = null!,
                ConcurrencyStamp = Guid.CreateVersion7()
            };
            seed.GroupMembers.Add(new GroupMember
            {
                Id = self,
                TenantId = participation.TenantId,
                Tenant = null!,
                GroupTenantId = placementId,
                GroupTenant = participation,
                UserId = fixture.Binding.LocalSubjectId,
                User = null!,
                RoleId = (int)RoleEnum.GroupAdmin,
                Role = null!
            });
            if (secondAdmin) seed.GroupMembers.Add(new GroupMember
            {
                Id = Guid.CreateVersion7(),
                TenantId = participation.TenantId,
                Tenant = null!,
                GroupTenantId = placementId,
                GroupTenant = participation,
                UserId = other.Id,
                User = null!,
                RoleId = (int)RoleEnum.GroupAdmin,
                Role = null!
            });
        }
        else
        {
            var participation = new OrganizationTenant
            {
                Id = placementId,
                TenantId = PlatformDefaults.DefaultTenantId,
                Tenant = null!,
                OrganizationId = ownerId,
                Organization = new Organization
                {
                    Id = ownerId,
                    Pii = new OrganizationPii { FullName = "Account custody organization" },
                    ConcurrencyStamp = Guid.CreateVersion7()
                },
                ApprovalStatusId = (int)ApprovalStatusEnum.Pending,
                ApprovalStatus = null!,
                ConcurrencyStamp = Guid.CreateVersion7()
            };
            seed.OrganizationMembers.Add(new OrganizationMember
            {
                Id = self,
                TenantId = participation.TenantId,
                Tenant = null!,
                OrganizationTenantId = placementId,
                OrganizationTenant = participation,
                UserId = fixture.Binding.LocalSubjectId,
                User = null!,
                RoleId = (int)RoleEnum.OrgAdmin,
                Role = null!
            });
            if (secondAdmin) seed.OrganizationMembers.Add(new OrganizationMember
            {
                Id = Guid.CreateVersion7(),
                TenantId = participation.TenantId,
                Tenant = null!,
                OrganizationTenantId = placementId,
                OrganizationTenant = participation,
                UserId = other.Id,
                User = null!,
                RoleId = (int)RoleEnum.OrgAdmin,
                Role = null!
            });
        }
        var provider = await seed.SystemSettings.SingleOrDefaultAsync(row =>
            row.SettingKey == GovernanceSettingKeys.Security.AuthorizationProvider);
        if (provider is null)
            seed.SystemSettings.Add(new SystemSetting { SettingKey = GovernanceSettingKeys.Security.AuthorizationProvider, Value = "\"local\"" });
        else provider.Value = "\"local\"";
        await seed.SaveChangesAsync();
        return new(ownerId, self, other.Id, login.Identifier, login);
    }
}
