using System.Data;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using SourceDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationQualificationWriterTests.SourceDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class RolePermissionMutationTests
{
    [Test]
    public async Task Replacement_changes_only_the_requested_set_and_preserves_retained_grant_provenance()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var (role, otherRole, first, second, third) = await SeedAsync(database);
        await using var context = database.Context();
        var repository = new RoleRepository(context);
        await repository.AssignPermissionsAsync(role, [first, second, first], database.AdministratorId);
        await repository.AssignPermissionsAsync(otherRole, [third], database.SubjectId);
        var retained = await context.RolePermissions.AsNoTracking()
            .SingleAsync(row => row.RoleId == role && row.PermissionId == second);
        await repository.ReplacePermissionsAsync(role, [second, third, third], database.ReviewerId);
        var actual = await context.RolePermissions.AsNoTracking().Where(row => row.RoleId == role).ToArrayAsync();
        await Assert.That(actual.Select(row => row.PermissionId)).IsEquivalentTo([second, third]);
        await Assert.That(actual.Single(row => row.PermissionId == second).GrantedBy).IsEqualTo(retained.GrantedBy);
        await Assert.That(actual.Single(row => row.PermissionId == second).GrantedAt).IsEqualTo(retained.GrantedAt);
        await Assert.That(actual.Single(row => row.PermissionId == third).GrantedBy).IsEqualTo((Guid?)database.ReviewerId);
        await repository.ReplacePermissionsAsync(role, [third, second], database.SubjectId);
        await repository.AssignPermissionsAsync(role, [first, second], database.AdministratorId);
        await Assert.That(await context.RolePermissions.Where(row => row.RoleId == role).Select(row => row.PermissionId).ToArrayAsync())
            .IsEquivalentTo([first, second, third]);
        await repository.RemoveAllPermissionsAsync(role);
        await Assert.That(await context.RolePermissions.AnyAsync(row => row.RoleId == role)).IsFalse();
        await Assert.That(await context.RolePermissions.Where(row => row.RoleId == otherRole).Select(row => row.PermissionId).ToArrayAsync())
            .IsEquivalentTo([third]);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments("assign")]
    [Arguments("replace")]
    [Arguments("remove")]
    public async Task Permission_mutation_remains_owned_by_an_existing_transaction(string mutation)
    {
        await using var database = await SourceDatabase.CreateAsync();
        var (role, _, first, second, third) = await SeedAsync(database);
        await using (var context = database.Context())
        {
            var repository = new RoleRepository(context);
            await repository.AssignPermissionsAsync(role, [first, second]);
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            if (mutation == "assign") await repository.AssignPermissionsAsync(role, [third]);
            else if (mutation == "replace") await repository.ReplacePermissionsAsync(role, [third]);
            else await repository.RemoveAllPermissionsAsync(role);
            await transaction.RollbackAsync();
        }
        await using var check = database.Context();
        await Assert.That(await check.RolePermissions.Where(row => row.RoleId == role).Select(row => row.PermissionId).ToArrayAsync())
            .IsEquivalentTo([first, second]);
    }

    [Test]
    public async Task An_unknown_permission_cannot_partially_replace_the_existing_grants()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var (role, _, first, second, _) = await SeedAsync(database);
        await using (var context = database.Context())
        {
            var repository = new RoleRepository(context);
            await repository.AssignPermissionsAsync(role, [first, second]);
            await Assert.That(async () => await repository.ReplacePermissionsAsync(role, [first, int.MaxValue]))
                .Throws<ArgumentException>();
            await Assert.That(context.Database.CurrentTransaction).IsNull();
        }
        await using var check = database.Context();
        await Assert.That(await check.RolePermissions.Where(row => row.RoleId == role).Select(row => row.PermissionId).ToArrayAsync())
            .IsEquivalentTo([first, second]);
    }

    [Test]
    public async Task Repository_owned_replay_cannot_absorb_or_discard_pending_caller_changes()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var (role, _, first, _, _) = await SeedAsync(database);
        await using var context = database.Context();
        var pending = new Group { Id = Guid.CreateVersion7(), FullName = "Uncommitted caller work" };
        context.Groups.Add(pending);
        await Assert.That(async () => await new RoleRepository(context).AssignPermissionsAsync(role, [first]))
            .Throws<InvalidOperationException>();
        await Assert.That(context.Entry(pending).State).IsEqualTo(EntityState.Added);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await using var check = database.Context();
        await Assert.That(await check.RolePermissions.AnyAsync(row => row.RoleId == role)).IsFalse();
        await Assert.That(await check.Groups.AnyAsync(row => row.Id == pending.Id)).IsFalse();
    }

    [Test]
    public async Task A_weaker_caller_transaction_cannot_be_used_for_permission_replacement()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var (role, _, first, _, _) = await SeedAsync(database);
        await using var context = database.Context();
        var repository = new RoleRepository(context);
        await repository.AssignPermissionsAsync(role, [first]);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadUncommitted);
        await Assert.That(async () => await repository.ReplacePermissionsAsync(role, []))
            .Throws<InvalidOperationException>();
        await Assert.That(context.Database.CurrentTransaction).IsNotNull();
        await Assert.That(await context.RolePermissions.Where(row => row.RoleId == role).Select(row => row.PermissionId).ToArrayAsync())
            .IsEquivalentTo([first]);
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task A_live_group_membership_prevents_the_role_being_treated_as_unused()
    {
        await using var database = await SourceDatabase.CreateAsync();
        await using var context = database.Context();
        var group = new Group { Id = Guid.CreateVersion7(), FullName = "Role usage witness" };
        var participation = new GroupTenant
        {
            Id = Guid.CreateVersion7(),
            TenantId = database.TenantId,
            Tenant = null!,
            Group = group,
            GroupId = group.Id,
            ApprovalStatus = null!,
            ApprovalStatusId = (int)ApprovalStatusEnum.Approved
        };
        var member = new GroupMember
        {
            Id = Guid.CreateVersion7(),
            TenantId = database.TenantId,
            Tenant = null!,
            GroupTenant = participation,
            GroupTenantId = participation.Id,
            User = null!,
            UserId = database.SubjectId,
            Role = null!,
            RoleId = (int)RoleEnum.GroupMember
        };
        context.Add(member);
        await context.SaveChangesAsync();
        await Assert.That(await new RoleRepository(context).HasActiveMembersAsync((int)RoleEnum.GroupMember)).IsTrue();
    }

    private static async Task<(int Role, int OtherRole, int First, int Second, int Third)> SeedAsync(
        SourceDatabase database, RoleScopeEnum scope = RoleScopeEnum.Group)
    {
        await using var context = database.Context();
        var role = await context.Roles.MaxAsync(row => row.Id) + 1;
        var permission = (await context.Permissions.Select(row => (int?)row.Id).MaxAsync() ?? 0) + 1;
        var prefix = Guid.CreateVersion7().ToString("N");
        context.Roles.AddRange(Enumerable.Range(role, 2).Select(id => new Role
        {
            Id = id,
            MasterCode = $"role-{prefix}-{id}",
            FullName = "Permission mutation witness",
            Scope = scope
        }));
        context.Permissions.AddRange(Enumerable.Range(permission, 3).Select(id => new Permission
        {
            Id = id,
            MasterCode = $"permission-{prefix}-{id}",
            ResourceKind = "group_member",
            Action = $"witness-{id}",
            FullName = "Permission mutation witness",
            GroupName = "Group",
            Scope = RoleScopeEnum.Group,
            IsActive = true
        }));
        await context.SaveChangesAsync();
        return (role, role + 1, permission, permission + 1, permission + 2);
    }
}
