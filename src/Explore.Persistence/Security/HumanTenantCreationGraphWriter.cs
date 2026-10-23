using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Management;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Identity;
using Explore.Persistence.Schema;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>
/// Writes one admitted human's fixed new-tenant graph. This owner never executes
/// caller-supplied entities, borrows Runtime credentials, or repairs existing tenants.
/// </summary>
internal sealed class HumanTenantCreationGraphWriter(
    ExploreDbContext context, ExploreDbContextExecutionLease selection,
    IHumanTenantCreationPort issuer, IdentityDatabaseTopology identityTopology) : IHumanTenantCreationGraphWriter
{
    private bool attempted;
    private Func<IHumanTenantCreationAuthority, HumanTenantCreationPlan,
        HumanTenantCreationAdministratorEffect?>? readCompletedAdministrator;

    public HumanTenantCreationAdministratorEffect? ReadCompletedAdministratorEffect(
        IHumanTenantCreationAuthority authority, HumanTenantCreationPlan plan) =>
        readCompletedAdministrator?.Invoke(authority, plan);

    public async Task<HumanTenantCreationResult> WriteAsync(IHumanTenantCreationAuthority authority,
        HumanTenantCreationPlan plan, CancellationToken cancellationToken = default)
    {
        if (attempted) throw new UnauthorizedAccessException("A human creation writer cannot replay.");
        attempted = true;
        await issuer.RequireCurrentAsync(authority, plan, cancellationToken);
        selection.RequireOriginalOwner(context);
        var human = selection.HumanScope
            ?? throw new UnauthorizedAccessException("The original creator selection is missing.");
        if (human.Purpose != HumanAdministratorCommitPurpose.TenantCreation
            || selection.PolicyPurpose != Explore.Application.Contracts.Operations.PolicyMutationPurpose.TenantCreation
            || context.TenantFilterTenantId is not null || context.IsTenantFilterBypassed
            || !context.HumanCommitAttemptOwnsTransaction)
            throw new UnauthorizedAccessException("The original human creator purpose is required.");
        human.RequireOwner(context);
        EventDiscoveryEnrollmentGate.RequireCurrent(context);
        var connection = RelationalConnectionIdentity.Capture(context);
        var contextId = context.ContextId;
        var transaction = context.Database.CurrentTransaction!;
        var physical = transaction.GetDbTransaction();
        if (physical.IsolationLevel != IsolationLevel.Serializable || !connection.Owns(physical))
            throw new UnauthorizedAccessException("Human creation requires its original Serializable transaction.");
        await PostgresTenantCreationPermissions.RequireCurrentAsync(context, human.Enrollment, identityTopology,
            cancellationToken);
        await PostgresDiscoveryMetadataPermissions.RequireCurrentAsync(context, human.Enrollment,
            globalOnly: true, cancellationToken);
        if (context.ChangeTracker.Entries().Any(entry =>
                entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Human creation cannot persist another operation's pending graph.");

        bool assignAdministrator = plan.AdministratorUserId.HasValue;
        if (assignAdministrator != plan.AdministratorMembershipId.HasValue
            || assignAdministrator != plan.AdministratorGrantId.HasValue
            || assignAdministrator && (plan.AdministratorUserId != human.Subject.PrincipalId
                || plan.Creation.ActorUserId != human.Subject.PrincipalId
                || plan.AdministratorMembershipId!.Value.Version != 7 || plan.AdministratorGrantId!.Value.Version != 7
                || new[] { plan.Creation.TenantId, plan.Creation.Branding.DocumentId,
                    plan.Creation.DirectoryOperatorIdentity.DocumentId, plan.AdministratorMembershipId.Value,
                    plan.AdministratorGrantId.Value }.Distinct().Count() != 5))
            throw new UnauthorizedAccessException("The complete original self-administrator graph is required.");

        var source = await ReadSourcesAsync(plan, cancellationToken);
        if (source.Tenants.Any(row => string.Equals(row.Slug, plan.Creation.Slug.Trim(), StringComparison.Ordinal)))
            return HumanTenantCreationResult.Refused("tenant_slug_conflict", "Slug must be unique across all tenants.");
        if (!source.Statuses.Any(row => row.Id == plan.Creation.TenantStatusId))
            return HumanTenantCreationResult.Refused("tenant_status_unavailable", "The selected tenant status is unavailable.");
        var administratorRole = source.Roles.Length == 1 ? source.Roles[0] : null;
        if (assignAdministrator && (administratorRole is null || administratorRole.MasterCode != "tenant.admin"
                || administratorRole.ScopeId != (int)RoleScopeEnum.Tenant))
            return HumanTenantCreationResult.Refused("tenant_administrator_role_unavailable",
                "The current tenant administrator role is unavailable.");
        if (plan.CapacityEnforced && source.Statuses.Single(row => row.Id == plan.Creation.TenantStatusId).Active)
        {
            DeploymentMode? mode = source.Bootstrap?.Status == InstanceBootstrapStatus.Completed
                ? source.Bootstrap.Mode : null;
            int active = source.Tenants.Count(row => source.Statuses.Any(status => status.Id == row.StatusId && status.Active));
            int reserved = source.Operations.Count(row => row.Status is ManagedTenantProvisioningStatus.Pending
                or ManagedTenantProvisioningStatus.Processing);
            var capacity = TenantActivationCapacityPolicy.Assess(plan.CapacityEnforced, requireMultiTenant: false,
                mode, plan.MaximumTenantCount, active, reserved);
            if (!capacity.Allowed)
                return HumanTenantCreationResult.Refused(capacity.FailureCode!, capacity.Error!);
        }

        var graph = TenantCreationService.Prepare(plan.Creation);
        graph.BrandingDocument.ConcurrencyStamp = Guid.CreateVersion7();
        graph.DirectoryOperatorIdentityDocument.ConcurrencyStamp = Guid.CreateVersion7();
        List<object> entities = [graph.Tenant, graph.BrandingDocument, graph.DirectoryOperatorIdentityDocument];
        HumanTenantCreationAdministratorEffect? administratorEffect = null;
        if (assignAdministrator)
        {
            DateTime occurredAt = plan.Creation.OccurredAt;
            Guid userId = human.Subject.PrincipalId;
            var member = new TenantUser
            {
                Id = plan.AdministratorMembershipId!.Value,
                TenantId = graph.Tenant.Id,
                Tenant = graph.Tenant,
                UserId = userId,
                User = null!,
                ActorId = null,
                Actor = null,
                StatusId = (int)TenantUserStatusEnum.Active,
                JoinedAt = occurredAt,
                SuspendedAt = null,
                SuspendedBy = null,
                BanExpiresAt = null,
                RemovedAt = null,
                RemovedBy = null,
                ModerationNote = null,
                CreatedAt = occurredAt,
                CreatedBy = userId,
                UpdatedAt = null,
                UpdatedBy = null,
                IsDeleted = false,
                DeletedAt = null,
                DeletedBy = null
            };
            var grant = new TenantUserRoleGrant
            {
                Id = plan.AdministratorGrantId!.Value,
                TenantId = graph.Tenant.Id,
                Tenant = graph.Tenant,
                TenantUserId = member.Id,
                TenantUser = member,
                RoleId = administratorRole!.Id,
                Role = null!,
                RoleScopeId = (int)RoleScopeEnum.Tenant,
                GrantedAt = occurredAt,
                GrantedBy = userId,
                RevokedAt = null,
                RevokedBy = null,
                RevocationReason = null,
                CreatedAt = occurredAt,
                CreatedBy = userId,
                UpdatedAt = null,
                UpdatedBy = null
            };
            entities.Add(member);
            entities.Add(grant);
            administratorEffect = new(graph.Tenant.Id, userId, member.Id, grant.Id,
                administratorRole.Id, administratorRole.ScopeId, administratorRole.MasterCode,
                graph.Tenant.TenantStatusId, source.Statuses.Single(row => row.Id == graph.Tenant.TenantStatusId).Active);
        }
        EntityEntry[] effects = entities.Select(entity => context.Entry(entity)).ToArray();
        PropertyValues[] expected = effects.Select(entry => entry.CurrentValues.Clone()).ToArray();
        bool completed = false;
        bool failed = false;
        void RequireOwner()
        {
            selection.RequireOriginalOwner(context);
            human.RequireOwner(context);
            EventDiscoveryEnrollmentGate.RequireCurrent(context);
            if (failed || context.ContextId != contextId || !connection.Matches(context)
                || !ReferenceEquals(context.Database.CurrentTransaction, transaction)
                || !ReferenceEquals(transaction.GetDbTransaction(), physical) || !connection.Owns(physical))
                throw new UnauthorizedAccessException("The original human creation attempt changed or failed.");
        }
        void RequireEffects()
        {
            RequireOwner();
            if (!completed || effects.Where((entry, index) => entry.State != EntityState.Unchanged
                    || entry.Metadata.GetProperties().Any(property =>
                        !Equals(entry.Property(property.Name).CurrentValue, expected[index][property.Name]))).Any()
                || !context.RequireDiscoveryEnrollmentCreations().SequenceEqual([plan.Creation.TenantId]))
                throw new InvalidOperationException("The complete original human creation graph is required.");
        }
        context.RegisterHumanCommitAdmission(async token =>
        {
            RequireEffects();
            await issuer.RequireCurrentAsync(authority, plan, token);
            await PostgresTenantCreationPermissions.RequireCurrentAsync(context, human.Enrollment, identityTopology,
                token);
            await PostgresDiscoveryMetadataPermissions.RequireCurrentAsync(context, human.Enrollment, globalOnly: true, token);
            var current = await ReadSourcesAsync(plan, token);
            if (!current.Tenants.Where(row => row.Id != plan.Creation.TenantId).SequenceEqual(source.Tenants)
                || current.Tenants.Count(row => row.Id == plan.Creation.TenantId
                    && row.Slug == graph.Tenant.Slug && row.StatusId == graph.Tenant.TenantStatusId) != 1
                || !current.Statuses.SequenceEqual(source.Statuses) || current.Bootstrap != source.Bootstrap
                || !current.Operations.SequenceEqual(source.Operations) || !current.Roles.SequenceEqual(source.Roles))
                throw new UnauthorizedAccessException("The original human creation catalogue changed.");
            RequireEffects();
        }, RequireEffects);
        try
        {
            RequireOwner();
            context.AddRange(entities);
            int written = await context.SaveChangesAsync(cancellationToken);
            if (written < effects.Length + 1)
                throw new InvalidOperationException("The original human creation save did not complete.");
            completed = true;
            RequireEffects();
            readCompletedAdministrator = (originalAuthority, originalPlan) =>
            {
                if (!ReferenceEquals(originalAuthority, authority) || !ReferenceEquals(originalPlan, plan))
                    throw new UnauthorizedAccessException("Administrator effects belong to their original creation authority.");
                RequireEffects();
                return administratorEffect;
            };
            await issuer.RequireCurrentAsync(authority, plan, cancellationToken);
            return HumanTenantCreationResult.Created(graph, administratorAssigned: assignAdministrator);
        }
        catch
        {
            failed = true;
            throw;
        }
    }

    private async Task<Sources> ReadSourcesAsync(HumanTenantCreationPlan plan, CancellationToken token)
    {
        // Tenant has no tenant/soft-delete filter. Its complete physical membership
        // remains the duplicate/capacity source; no descriptive/private column is read.
        var tenants = await context.Tenants.AsNoTracking().OrderBy(row => row.Id)
            .Select(row => new TenantImage(row.Id, row.Slug, row.TenantStatusId)).ToArrayAsync(token);
        var statuses = await context.Set<TenantStatus>().AsNoTracking().OrderBy(row => row.Id)
            .Select(row => new StatusImage(row.Id, row.IsActiveState)).ToArrayAsync(token);
        RoleImage[] roles = [];
        if (plan.AdministratorUserId.HasValue)
            roles = await context.Set<Role>().AsNoTracking()
                .Where(role => role.MasterCode == "tenant.admin")
                .OrderBy(role => role.Id)
                .Select(role => new RoleImage(role.Id, role.MasterCode, role.RoleScopeId)).ToArrayAsync(token);
        BootstrapImage? bootstrap = null;
        OperationImage[] operations = [];
        if (plan.CapacityEnforced && statuses.Any(row => row.Id == plan.Creation.TenantStatusId && row.Active))
        {
            bootstrap = await context.Set<InstanceBootstrapState>().AsNoTracking()
                .OrderByDescending(row => row.Generation).ThenByDescending(row => row.CreatedAt).ThenByDescending(row => row.Id)
                .Select(row => new BootstrapImage(row.Id, row.Status, row.DeploymentMode, row.Generation, row.CreatedAt))
                .FirstOrDefaultAsync(token);
            operations = await context.Set<ManagedTenantProvisioningOperation>().AsNoTracking().OrderBy(row => row.Id)
                .Select(row => new OperationImage(row.Id, row.Status)).ToArrayAsync(token);
        }
        return new(tenants, statuses, bootstrap, operations, roles);
    }

    private sealed record TenantImage(Guid Id, string Slug, int StatusId);
    private sealed record StatusImage(int Id, bool Active);
    private sealed record BootstrapImage(Guid Id, InstanceBootstrapStatus Status, DeploymentMode Mode,
        long Generation, DateTime CreatedAt);
    private sealed record OperationImage(Guid Id, ManagedTenantProvisioningStatus Status);
    private sealed record RoleImage(int Id, string MasterCode, int ScopeId);
    private sealed record Sources(TenantImage[] Tenants, StatusImage[] Statuses, BootstrapImage? Bootstrap,
        OperationImage[] Operations, RoleImage[] Roles);
}
