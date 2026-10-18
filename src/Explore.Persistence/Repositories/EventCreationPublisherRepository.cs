using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

/// <summary>Own publisher membership graphs on the original prepared human's Serializable owner.</summary>
internal sealed class EventCreationPublisherRepository(ExploreDbContext context, ExploreDbContextExecutionLease lease)
    : IEventCreationPublisherRepository
{
    public async Task<(IReadOnlyList<OrganizationMember> Organizations, IReadOnlyList<GroupMember> Groups,
        IReadOnlyList<RolePermission> Permissions)> ReadAsync(CancellationToken cancellationToken = default)
    {
        lease.RequireOriginalOwner(context);
        var human = lease.HumanScope
            ?? throw new UnauthorizedAccessException("Publisher reads require a prepared original human.");
        human.RequireOwner(context);
        var lifetime = lease.HumanLifetime
            ?? throw new UnauthorizedAccessException("Publisher reads require an authentic credential lifetime.");
        if (human.Purpose != HumanAdministratorCommitPurpose.RuntimeTenant
            || context.IsConfigurationInstanceImportOwner
            || !context.HasHumanCommitContextLease || !context.HumanCommitAttemptOwnsTransaction
            || !ReferenceEquals(context.HumanCommitAuditSubject, human.Subject)
            || human.Subject.AccountKey != lifetime.AccountKey
            || lease.SelectedTenantId is not { } tenant || tenant == Guid.Empty
            || human.TenantId != tenant || human.Subject.PrincipalId == Guid.Empty
            || RelationalConnectionIdentity.Capture(context).State != ConnectionState.Open)
            throw new UnauthorizedAccessException("Publisher reads require the original Runtime tenant human owner.");
        context.RequireHumanCommitAdmission();
        var transaction = context.Database.CurrentTransaction!;
        var result = EventCreationPublisherNativeOperations.For(context) is { } native
            ? await native.ReadAsync(
                human.Enrollment ?? throw new UnauthorizedAccessException("Publisher reads cannot enroll a PostgreSQL reader."),
                tenant, human.Subject.PrincipalId, cancellationToken)
            : await ReadPortableAsync(tenant, human.Subject.PrincipalId, cancellationToken);
        lease.RequireOriginalOwner(context);
        human.RequireOwner(context);
        context.RequireHumanCommitAdmission();
        if (!ReferenceEquals(lease.HumanScope, human) || !ReferenceEquals(lease.HumanLifetime, lifetime)
            || !context.HasHumanCommitContextLease || !context.HumanCommitAttemptOwnsTransaction
            || !ReferenceEquals(context.HumanCommitAuditSubject, human.Subject)
            || !ReferenceEquals(context.Database.CurrentTransaction, transaction))
            throw new UnauthorizedAccessException("Publisher reads cannot replace the original human or transaction.");
        return result;
    }

    private async Task<(IReadOnlyList<OrganizationMember> Organizations, IReadOnlyList<GroupMember> Groups,
        IReadOnlyList<RolePermission> Permissions)> ReadPortableAsync(Guid tenant, Guid principal, CancellationToken token)
    {
        if (context.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new UnauthorizedAccessException("Publisher reads require a Serializable original owner.");
        if (!await context.Users.AnyAsync(user => user.Id == principal && !user.IsDeleted, token)
            || !await context.TenantUsers.AnyAsync(member => member.TenantId == tenant && member.UserId == principal
                && !member.IsDeleted && member.StatusId == (int)TenantUserStatusEnum.Active
                && member.Tenant.TenantStatusId == (int)TenantStatusEnum.Active && member.Tenant.TenantStatus.IsActiveState, token))
            return (Array.Empty<OrganizationMember>(), Array.Empty<GroupMember>(), Array.Empty<RolePermission>());

        var organizations = await context.OrganizationMembers.IgnoreAutoIncludes().AsNoTracking()
            .Where(member => member.TenantId == tenant && member.UserId == principal && !member.IsDeleted
                && member.OrganizationTenant.TenantId == tenant && !member.OrganizationTenant.IsDeleted
                && !member.OrganizationTenant.Organization.IsDeleted)
            .OrderBy(member => member.Id)
            .Select(member => new OrganizationMember
            {
                Id = member.Id,
                TenantId = tenant,
                Tenant = null!,
                UserId = principal,
                User = null!,
                RoleId = member.RoleId,
                Role = null!,
                OrganizationTenantId = member.OrganizationTenantId,
                OrganizationTenant = new OrganizationTenant
                {
                    Id = member.OrganizationTenantId,
                    TenantId = tenant,
                    Tenant = null!,
                    ApprovalStatus = null!,
                    OrganizationId = member.OrganizationTenant.OrganizationId,
                    Organization = new Organization
                    {
                        Id = member.OrganizationTenant.OrganizationId,
                        Pii = new OrganizationPii
                        {
                            OrganizationId = member.OrganizationTenant.OrganizationId,
                            FullName = member.OrganizationTenant.Organization.Pii.FullName
                        }
                    }
                }
            }).ToListAsync(token);
        var groups = await context.GroupMembers.IgnoreAutoIncludes().AsNoTracking()
            .Where(member => member.TenantId == tenant && member.UserId == principal && !member.IsDeleted
                && member.GroupTenant.TenantId == tenant && !member.GroupTenant.IsDeleted && !member.GroupTenant.Group.IsDeleted)
            .OrderBy(member => member.Id)
            .Select(member => new GroupMember
            {
                Id = member.Id,
                TenantId = tenant,
                Tenant = null!,
                UserId = principal,
                User = null!,
                RoleId = member.RoleId,
                Role = null!,
                GroupTenantId = member.GroupTenantId,
                GroupTenant = new GroupTenant
                {
                    Id = member.GroupTenantId,
                    TenantId = tenant,
                    Tenant = null!,
                    ApprovalStatus = null!,
                    GroupId = member.GroupTenant.GroupId,
                    Group = new Group { Id = member.GroupTenant.GroupId, FullName = member.GroupTenant.Group.FullName }
                }
            }).ToListAsync(token);
        var roles = organizations.Select(member => member.RoleId).Concat(groups.Select(member => member.RoleId)).Distinct().ToArray();
        var permissions = await context.Set<RolePermission>().IgnoreAutoIncludes().AsNoTracking()
            .Where(mapping => roles.Contains(mapping.RoleId) && mapping.Permission.IsActive
                && mapping.Permission.MasterCode == PermissionCodes.EventCreate)
            .Select(mapping => new RolePermission
            {
                RoleId = mapping.RoleId,
                Role = null!,
                PermissionId = mapping.PermissionId,
                Permission = new Permission
                {
                    Id = mapping.PermissionId,
                    MasterCode = mapping.Permission.MasterCode,
                    IsActive = mapping.Permission.IsActive,
                    ResourceKind = null!,
                    Action = null!,
                    FullName = null!,
                    GroupName = null!
                }
            }).ToListAsync(token);
        return (organizations.AsReadOnly(), groups.AsReadOnly(), permissions.AsReadOnly());
    }
}
