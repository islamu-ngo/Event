using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class UserTenantMembershipRepository(
    IGlobalExploreDbContextFactory globalFactory,
    ITenantExploreDbContextFactory tenantFactory,
    PostgresNativeQueueEnrollment? enrollment) : IUserTenantMembershipRepository
{
    public async Task<List<TenantUser>> GetActiveTenantsForUserAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return [];

        await using var context = await globalFactory.CreateDbContextAsync(cancellationToken);
        return await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            context.RequireBoundTenantLease();
            if (context.TenantFilterTenantId is not null || context.IsTenantFilterBypassed
                || context.IsConfigurationInstanceImportOwner || context.Database.CurrentTransaction is not null)
                throw new UnauthorizedAccessException("membership_requires_selected_global_owner");
            bool relational = context.Database.IsRelational();
            if (relational) await context.Database.OpenConnectionAsync(cancellationToken);
            var connection = relational ? RelationalConnectionIdentity.Capture(context) : null;
            var contextId = context.ContextId;
            int? backend = connection?.BackendProcessId;
            bool interrupted = false;
            void Changed(object? sender, System.Data.StateChangeEventArgs change)
            {
                if (change.CurrentState != System.Data.ConnectionState.Open) interrupted = true;
            }
            void RequireOriginal()
            {
                cancellationToken.ThrowIfCancellationRequested();
                context.RequireBoundTenantLease();
                if (context.ContextId != contextId || context.TenantFilterTenantId is not null
                    || context.IsTenantFilterBypassed || context.IsConfigurationInstanceImportOwner
                    || context.Database.CurrentTransaction is not null
                    || connection is not null && (interrupted || !connection.Matches(context)
                        || connection.State != System.Data.ConnectionState.Open || connection.BackendProcessId != backend))
                    throw new UnauthorizedAccessException("membership_original_owner_changed");
            }
            if (connection is not null) connection.StateChange += Changed;
            try
            {
                RequireOriginal();
                await PostgresUserTenantMembershipReadPermissions.RequireReadyAsync(context, enrollment, cancellationToken);
                RequireOriginal();
                var memberships = await context.TenantUsers
                    .IgnoreTenantFilter(TenantFilterBypassReasons.UserTenantMembershipEnumeration)
                    .IgnoreAutoIncludes().AsNoTracking()
                    .Where(member => member.UserId == userId && !member.IsDeleted
                        && member.StatusId == (int)TenantUserStatusEnum.Active
                        && member.Tenant.TenantStatusId == (int)TenantStatusEnum.Active)
                    .Select(member => new TenantUser
                    {
                        Id = member.Id,
                        TenantId = member.TenantId,
                        UserId = member.UserId,
                        User = null!,
                        ActorId = member.ActorId,
                        StatusId = member.StatusId,
                        JoinedAt = member.JoinedAt,
                        SuspendedAt = member.SuspendedAt,
                        SuspendedBy = member.SuspendedBy,
                        BanExpiresAt = member.BanExpiresAt,
                        RemovedAt = member.RemovedAt,
                        RemovedBy = member.RemovedBy,
                        ModerationNote = member.ModerationNote,
                        CreatedAt = member.CreatedAt,
                        CreatedBy = member.CreatedBy,
                        UpdatedAt = member.UpdatedAt,
                        UpdatedBy = member.UpdatedBy,
                        IsDeleted = member.IsDeleted,
                        DeletedAt = member.DeletedAt,
                        DeletedBy = member.DeletedBy,
                        Tenant = new Tenant
                        {
                            Id = member.Tenant.Id,
                            FullName = member.Tenant.FullName,
                            Slug = member.Tenant.Slug,
                            Description = member.Tenant.Description,
                            TenantStatusId = member.Tenant.TenantStatusId,
                            CreatedAt = member.Tenant.CreatedAt,
                            CreatedBy = member.Tenant.CreatedBy,
                            UpdatedAt = member.Tenant.UpdatedAt,
                            UpdatedBy = member.Tenant.UpdatedBy,
                            TenantStatus = new TenantStatus
                            {
                                Id = member.Tenant.TenantStatus.Id,
                                MasterCode = member.Tenant.TenantStatus.MasterCode,
                                FullName = member.Tenant.TenantStatus.FullName,
                                Description = member.Tenant.TenantStatus.Description,
                                IsActiveState = member.Tenant.TenantStatus.IsActiveState
                            }
                        }
                    }).ToListAsync(cancellationToken);
                RequireOriginal();
                return memberships;
            }
            finally
            {
                if (connection is not null) connection.StateChange -= Changed;
                if (relational) await context.Database.CloseConnectionAsync();
            }
        });
    }

    public async Task<bool> IsActiveTenantUserAsync(
        Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty || userId == Guid.Empty)
            return false;

        await using var context = await tenantFactory.CreateDbContextAsync(tenantId, cancellationToken);
        return await context.TenantUsers.AsNoTracking().AnyAsync(membership =>
            membership.TenantId == tenantId && membership.UserId == userId
            && membership.StatusId == (int)TenantUserStatusEnum.Active && !membership.IsDeleted,
            cancellationToken);
    }

}
