using System.Data;
using System.Data.Common;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using SourceDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationQualificationWriterTests.SourceDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class RolePermissionMutationTests
{
    [Test]
    public async Task Deleting_an_unused_custom_role_removes_only_its_own_permission_graph()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var (role, otherRole, first, second, _) = await SeedAsync(database);
        await using var context = database.Context();
        var repository = new RoleRepository(context);
        await repository.AssignPermissionsAsync(role, [first]);
        await repository.AssignPermissionsAsync(otherRole, [second]);
        var deleted = await repository.DeleteUnusedCustomRoleAsync(role, CancellationToken.None);
        await Assert.That(deleted!.Id).IsEqualTo(role);
        await Assert.That(await context.Roles.AnyAsync(row => row.Id == role)).IsFalse();
        await Assert.That(await context.RolePermissions.AnyAsync(row => row.RoleId == role)).IsFalse();
        await Assert.That(await context.RolePermissions.AnyAsync(row => row.RoleId == otherRole && row.PermissionId == second)).IsTrue();
        await Assert.That(await context.Permissions.AnyAsync(row => row.Id == first)).IsTrue();
        await Assert.That(await repository.DeleteUnusedCustomRoleAsync(role, CancellationToken.None)).IsNull();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments("group")]
    [Arguments("deleted-group")]
    [Arguments("organization")]
    [Arguments("tenant")]
    [Arguments("revoked-tenant")]
    [Arguments("platform")]
    [Arguments("past-event")]
    [Arguments("invitation")]
    [Arguments("expired-invitation")]
    public async Task A_reference_outside_the_request_tenant_cannot_lose_its_role_or_permissions(string reference)
    {
        await using var database = await SourceDatabase.CreateAsync();
        var scope = reference switch
        {
            "platform" => RoleScopeEnum.Platform,
            "organization" => RoleScopeEnum.Organization,
            "past-event" => RoleScopeEnum.Event,
            "group" or "deleted-group" => RoleScopeEnum.Group,
            _ => RoleScopeEnum.Tenant
        };
        var (role, _, first, _, _) = await SeedAsync(database, scope);
        await using (var seed = database.Context())
        {
            await new RoleRepository(seed).AssignPermissionsAsync(role, [first]);
            await SeedRoleReferenceAsync(seed, database, role, reference);
        }
        await using var context = database.Context();
        context.TenantContext = new OtherRoleTenant(Guid.CreateVersion7());
        await Assert.That(async () => await new RoleRepository(context).DeleteUnusedCustomRoleAsync(role, CancellationToken.None))
            .Throws<BadRequestException>();
        await Assert.That(context.IsTenantFilterBypassed).IsFalse();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await using var check = database.Context();
        await Assert.That(await check.Roles.AnyAsync(row => row.Id == role)).IsTrue();
        await Assert.That(await check.RolePermissions.AnyAsync(row => row.RoleId == role && row.PermissionId == first)).IsTrue();
    }

    [Test]
    public async Task A_failure_after_the_role_delete_rolls_back_its_cascaded_permission_removal()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var (role, _, first, _, _) = await SeedAsync(database);
        await using var baseline = database.Context();
        await new RoleRepository(baseline).AssignPermissionsAsync(role, [first]);
        var entity = baseline.Model.FindEntityType(typeof(Role))!;
        var table = baseline.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
        var fault = new FailAfterRoleDelete(table);
        var options = TestDbContextOptions.Create<Explore.Persistence.ExploreDbContext>()
            .UseSqlite(baseline.Database.GetDbConnection()).UseSnakeCaseNamingConvention().AddInterceptors(fault).Options;
        await using (var context = new Explore.Persistence.ExploreDbContext(options) { TenantContext = database })
            await Assert.That(async () => await new RoleRepository(context).DeleteUnusedCustomRoleAsync(role, CancellationToken.None))
                .Throws<IOException>();
        await Assert.That(fault.Observed).IsTrue();
        await using var check = database.Context();
        await Assert.That(await check.Roles.AnyAsync(row => row.Id == role)).IsTrue();
        await Assert.That(await check.RolePermissions.AnyAsync(row => row.RoleId == role && row.PermissionId == first)).IsTrue();
    }

    [Test]
    public async Task Deletion_does_not_commit_an_existing_serializable_transaction()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var (role, _, first, _, _) = await SeedAsync(database);
        await using (var context = database.Context())
        {
            var repository = new RoleRepository(context);
            await repository.AssignPermissionsAsync(role, [first]);
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await repository.DeleteUnusedCustomRoleAsync(role, CancellationToken.None);
            await Assert.That(context.Database.CurrentTransaction).IsNotNull();
            await transaction.RollbackAsync();
        }
        await using var check = database.Context();
        await Assert.That(await check.Roles.AnyAsync(row => row.Id == role)).IsTrue();
        await Assert.That(await check.RolePermissions.AnyAsync(row => row.RoleId == role && row.PermissionId == first)).IsTrue();
    }

    [Test]
    public async Task A_system_flag_changed_after_discovery_is_rechecked_before_deletion()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var (role, _, first, _, _) = await SeedAsync(database);
        await using var context = database.Context();
        var repository = new RoleRepository(context);
        await repository.AssignPermissionsAsync(role, [first]);
        await repository.GetByIdAsync(role);
        await context.Roles.Where(row => row.Id == role).ExecuteUpdateAsync(set => set.SetProperty(row => row.IsSystem, true));
        await Assert.That(async () => await repository.DeleteUnusedCustomRoleAsync(role, CancellationToken.None))
            .Throws<BadRequestException>();
        await Assert.That(await context.RolePermissions.AnyAsync(row => row.RoleId == role && row.PermissionId == first)).IsTrue();
    }

    [Test]
    public async Task Deletion_cannot_absorb_or_discard_pending_caller_changes()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var (role, _, _, _, _) = await SeedAsync(database);
        await using var context = database.Context();
        var pending = new Group { Id = Guid.CreateVersion7(), FullName = "Pending unrelated source" };
        context.Add(pending);
        await Assert.That(async () => await new RoleRepository(context).DeleteUnusedCustomRoleAsync(role, CancellationToken.None))
            .Throws<InvalidOperationException>();
        await Assert.That(context.Entry(pending).State).IsEqualTo(EntityState.Added);
        await Assert.That(await context.Roles.AnyAsync(row => row.Id == role)).IsTrue();
    }

    [Test]
    public async Task Deletion_cannot_borrow_a_weaker_transaction()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var (role, _, _, _, _) = await SeedAsync(database);
        await using var context = database.Context();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadUncommitted);
        await Assert.That(async () => await new RoleRepository(context).DeleteUnusedCustomRoleAsync(role, CancellationToken.None))
            .Throws<InvalidOperationException>();
        await Assert.That(context.Database.CurrentTransaction).IsNotNull();
        await transaction.RollbackAsync();
    }

    private static async Task SeedRoleReferenceAsync(Explore.Persistence.ExploreDbContext context, SourceDatabase database, int roleId, string kind)
    {
        if (kind == "platform")
            context.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = Guid.CreateVersion7(),
                UserId = database.SubjectId,
                User = null!,
                RoleId = roleId,
                Role = null!,
                GrantedAt = DateTime.UtcNow
            });
        else if (kind is "tenant" or "revoked-tenant")
        {
            var membership = await context.TenantUsers.SingleAsync(row => row.UserId == database.SubjectId);
            context.TenantUserRoleGrants.Add(new TenantUserRoleGrant
            {
                Id = Guid.CreateVersion7(),
                TenantId = database.TenantId,
                Tenant = null!,
                TenantUserId = membership.Id,
                TenantUser = null!,
                RoleId = roleId,
                Role = null!,
                RoleScopeId = (int)RoleScopeEnum.Tenant,
                GrantedAt = DateTime.UtcNow.AddDays(-2),
                RevokedAt = kind == "revoked-tenant" ? DateTime.UtcNow.AddDays(-1) : null
            });
        }
        else if (kind is "invitation" or "expired-invitation")
        {
            context.TenantInvitations.Add(new TenantInvitation
            {
                Id = Guid.CreateVersion7(),
                TenantId = database.TenantId,
                Tenant = null!,
                RoleId = roleId,
                Role = null!,
                Email = "role-reference@example.invalid",
                Token = Guid.CreateVersion7().ToString("N"),
                ExpiresAt = DateTime.UtcNow.AddDays(kind == "invitation" ? 1 : -1)
            });
        }
        else if (kind == "organization")
        {
            var organization = new Organization { Id = Guid.CreateVersion7(), Pii = new OrganizationPii { FullName = "Retained role reference" } };
            var participation = new OrganizationTenant
            {
                Id = Guid.CreateVersion7(),
                TenantId = database.TenantId,
                Tenant = null!,
                OrganizationId = organization.Id,
                Organization = organization,
                ApprovalStatusId = (int)ApprovalStatusEnum.Approved,
                ApprovalStatus = null!
            };
            context.OrganizationMembers.Add(new OrganizationMember
            {
                Id = Guid.CreateVersion7(),
                TenantId = database.TenantId,
                Tenant = null!,
                OrganizationTenantId = participation.Id,
                OrganizationTenant = participation,
                UserId = database.SubjectId,
                User = null!,
                RoleId = roleId,
                Role = null!
            });
        }
        else if (kind == "past-event")
        {
            var actor = new Actor
            {
                Id = Guid.CreateVersion7(),
                ActorTypeId = (int)ActorTypeEnum.User,
                ActorType = null!,
                UserId = database.SubjectId,
                Pii = new ActorPii { DisplayName = "Role reference" }
            };
            var source = new Explore.Domain.Event(EventStatusEnum.Published)
            {
                Id = Guid.CreateVersion7(),
                TenantId = database.TenantId,
                Tenant = null!,
                Actor = actor,
                ActorId = actor.Id,
                OrganizerActorId = actor.Id,
                Title = "Retained role reference",
                PublicCode = Guid.CreateVersion7().ToString("N")[..12],
                EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
                VisibilityTypeId = (int)VisibilityTypeEnum.Public,
                VisibilityType = null!,
                EventFormatId = (int)EventFormatEnum.Local,
                EventFormat = null!,
                EventStatus = null!,
                Timezone = "UTC"
            };
            context.Events.Add(source);
            context.EventRoleAssignments.Add(EventRoleAssignment.Create(database.TenantId, source.Id, database.SubjectId, roleId,
                EventRoleAssignmentStatus.Active, DateTime.UtcNow.AddDays(-2), DateTime.UtcNow.AddDays(-1), database.AdministratorId));
        }
        else
        {
            var group = new Group { Id = Guid.CreateVersion7(), FullName = "Retained role reference" };
            var participation = new GroupTenant
            {
                Id = Guid.CreateVersion7(),
                TenantId = database.TenantId,
                Tenant = null!,
                GroupId = group.Id,
                Group = group,
                ApprovalStatusId = (int)ApprovalStatusEnum.Approved,
                ApprovalStatus = null!
            };
            context.GroupMembers.Add(new GroupMember
            {
                Id = Guid.CreateVersion7(),
                TenantId = database.TenantId,
                Tenant = null!,
                GroupTenantId = participation.Id,
                GroupTenant = participation,
                UserId = database.SubjectId,
                User = null!,
                RoleId = roleId,
                Role = null!,
                IsDeleted = kind == "deleted-group"
            });
        }
        await context.SaveChangesAsync();
    }

    private sealed record OtherRoleTenant(Guid TenantId) : ITenantContext;

    private sealed class FailAfterRoleDelete(string table) : DbCommandInterceptor
    {
        public bool Observed { get; private set; }
        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (result > 0 && command.CommandText.TrimStart().StartsWith("DELETE", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains(table, StringComparison.Ordinal))
            {
                Observed = true;
                throw new IOException("Role deletion transport failure after the write.");
            }
            return ValueTask.FromResult(result);
        }
    }
}
