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

/// <summary>Actor display graphs for the original prepared human, never a caller-selected subject.</summary>
internal sealed class AiActorContextRepository(ExploreDbContext context, ExploreDbContextExecutionLease lease)
    : IAiActorContextRepository
{
    public async Task<IReadOnlyList<Actor>> GetAvailableActorsAsync(CancellationToken cancellationToken = default)
    {
        lease.RequireOriginalOwner(context);
        var human = lease.HumanScope
            ?? throw new UnauthorizedAccessException("Actor selection requires a prepared original human.");
        human.RequireOwner(context);
        var lifetime = lease.HumanLifetime
            ?? throw new UnauthorizedAccessException("Actor selection requires the original authentic credential lifetime.");
        if (human.Purpose != HumanAdministratorCommitPurpose.RuntimeTenant
            || context.IsConfigurationInstanceImportOwner
            || !context.HasHumanCommitContextLease || !context.HumanCommitAttemptOwnsTransaction
            || !ReferenceEquals(context.HumanCommitAuditSubject, human.Subject)
            || human.Subject.AccountKey != lifetime.AccountKey
            || lease.SelectedTenantId is not { } tenant || tenant == Guid.Empty
            || human.TenantId != tenant || human.Subject.PrincipalId == Guid.Empty
            || RelationalConnectionIdentity.Capture(context).State != ConnectionState.Open)
            throw new UnauthorizedAccessException("Actor selection requires the original Runtime tenant human owner.");
        context.RequireHumanCommitAdmission();
        var transaction = context.Database.CurrentTransaction!;
        IReadOnlyList<Actor> result;
        if (AiRepositoryNativeOperations.For(context) is { } native)
        {
            var enrollment = human.Enrollment
                ?? throw new UnauthorizedAccessException("Actor selection cannot enroll a PostgreSQL reader.");
            result = await native.ReadActorsAsync(enrollment, tenant, human.Subject.PrincipalId, cancellationToken);
        }
        else
        {
            result = await ReadPortableAsync(tenant, human.Subject.PrincipalId, cancellationToken);
        }
        lease.RequireOriginalOwner(context);
        human.RequireOwner(context);
        context.RequireHumanCommitAdmission();
        if (!ReferenceEquals(lease.HumanScope, human) || !ReferenceEquals(lease.HumanLifetime, lifetime)
            || !context.HasHumanCommitContextLease || !context.HumanCommitAttemptOwnsTransaction
            || !ReferenceEquals(context.HumanCommitAuditSubject, human.Subject)
            || !ReferenceEquals(context.Database.CurrentTransaction, transaction))
            throw new UnauthorizedAccessException("Actor selection cannot replace its original human or transaction.");
        return result;
    }

    private async Task<IReadOnlyList<Actor>> ReadPortableAsync(Guid tenant, Guid principal, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new UnauthorizedAccessException("Actor selection requires a Serializable original owner.");
        var actors = await context.Actors.IgnoreAutoIncludes().AsNoTracking()
            .Where(actor => !actor.IsDeleted && !actor.IsSuspended && actor.Pii != null
                && actor.ExternalActorSubjectId == null && actor.ServicePrincipalId == null
                && context.Users.Any(user => user.Id == principal && !user.IsDeleted)
                && context.TenantUsers.Any(member => member.TenantId == tenant && member.UserId == principal
                    && !member.IsDeleted && member.StatusId == (int)TenantUserStatusEnum.Active
                    && member.Tenant.TenantStatusId == (int)TenantStatusEnum.Active && member.Tenant.TenantStatus.IsActiveState)
                && (actor.ActorTypeId == (int)ActorTypeEnum.User && actor.UserId == principal
                        && actor.OrganizationId == null && actor.GroupId == null
                    || actor.ActorTypeId == (int)ActorTypeEnum.Organization && actor.Organization != null && actor.Organization.Pii != null
                        && actor.UserId == null && actor.GroupId == null
                        && !actor.Organization.IsDeleted
                        && context.OrganizationMembers.Any(member => member.TenantId == tenant && member.UserId == principal
                            && !member.IsDeleted && !member.OrganizationTenant.IsDeleted
                            && !member.OrganizationTenant.IsSuspended
                            && member.OrganizationTenant.TenantId == tenant
                            && member.OrganizationTenant.OrganizationId == actor.OrganizationId
                            && member.OrganizationTenant.ApprovalStatusId == (int)ApprovalStatusEnum.Approved
                            && member.Role.RoleScopeId == (int)RoleScopeEnum.Organization
                            && context.Set<RolePermission>().Any(grant => grant.RoleId == member.RoleId
                                && grant.Permission.IsActive && grant.Permission.MasterCode == PermissionCodes.EventCreate))
                    || actor.ActorTypeId == (int)ActorTypeEnum.Group && actor.Group != null && !actor.Group.IsDeleted
                        && actor.UserId == null && actor.OrganizationId == null
                        && context.GroupMembers.Any(member => member.TenantId == tenant && member.UserId == principal
                            && !member.IsDeleted && !member.GroupTenant.IsDeleted && !member.GroupTenant.IsSuspended
                            && member.GroupTenant.TenantId == tenant && member.GroupTenant.GroupId == actor.GroupId
                            && member.GroupTenant.ApprovalStatusId == (int)ApprovalStatusEnum.Approved
                            && member.Role.RoleScopeId == (int)RoleScopeEnum.Group
                            && context.Set<RolePermission>().Any(grant => grant.RoleId == member.RoleId
                                && grant.Permission.IsActive && grant.Permission.MasterCode == PermissionCodes.EventCreate))))
            .OrderBy(actor => actor.Id)
            .Select(actor => new Actor
            {
                Id = actor.Id,
                ActorTypeId = actor.ActorTypeId,
                ActorType = null!,
                UserId = actor.UserId,
                OrganizationId = actor.OrganizationId,
                GroupId = actor.GroupId,
                Pii = new ActorPii { ActorId = actor.Id, DisplayName = actor.Pii.DisplayName },
                Organization = actor.Organization == null ? null : new Organization
                {
                    Id = actor.Organization.Id,
                    Pii = new OrganizationPii { OrganizationId = actor.Organization.Id, FullName = actor.Organization.Pii.FullName }
                },
                Group = actor.Group == null ? null : new Group { Id = actor.Group.Id, FullName = actor.Group.FullName }
            })
            .Take(PostgresAiActorContextReadContract.MaximumActors + 1)
            .ToListAsync(cancellationToken);
        if (actors.Count > PostgresAiActorContextReadContract.MaximumActors)
            throw new InvalidOperationException("ai_actor_context_overflow");
        return actors.AsReadOnly();
    }
}
