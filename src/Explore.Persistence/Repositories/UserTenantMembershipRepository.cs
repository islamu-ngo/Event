using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class UserTenantMembershipRepository(
    IGlobalExploreDbContextFactory globalFactory,
    ITenantExploreDbContextFactory tenantFactory) : IUserTenantMembershipRepository
{
    public async Task<List<TenantUser>> GetActiveTenantsForUserAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return [];

        await using var context = await globalFactory.CreateDbContextAsync(cancellationToken);
        var nativeRead = UserTenantMembershipNativeQuery.ReadAsync(context, userId, cancellationToken);
        if (nativeRead is null)
        {
            return await context.TenantUsers
                .IgnoreTenantFilter(TenantFilterBypassReasons.UserTenantMembershipEnumeration)
                .AsNoTracking()
                .Include(membership => membership.Tenant).ThenInclude(tenant => tenant.TenantStatus)
                .Where(membership => membership.UserId == userId
                    && membership.StatusId == (int)TenantUserStatusEnum.Active
                    && !membership.IsDeleted
                    && membership.Tenant.TenantStatusId == (int)TenantStatusEnum.Active)
                .ToListAsync(cancellationToken);
        }

        var rows = await nativeRead;
        return rows.Select(row => new TenantUser
        {
            Id = row.Id,
            TenantId = row.TenantId,
            UserId = row.UserId,
            User = null!,
            ActorId = row.ActorId,
            StatusId = row.StatusId,
            JoinedAt = row.JoinedAt,
            SuspendedAt = row.SuspendedAt,
            SuspendedBy = row.SuspendedBy,
            BanExpiresAt = row.BanExpiresAt,
            RemovedAt = row.RemovedAt,
            RemovedBy = row.RemovedBy,
            ModerationNote = row.ModerationNote,
            CreatedAt = row.CreatedAt,
            CreatedBy = row.CreatedBy,
            UpdatedAt = row.UpdatedAt,
            UpdatedBy = row.UpdatedBy,
            IsDeleted = row.IsDeleted,
            DeletedAt = row.DeletedAt,
            DeletedBy = row.DeletedBy,
            Tenant = new Tenant
            {
                Id = row.TenantId,
                FullName = row.TenantFullName,
                Slug = row.TenantSlug,
                Description = row.TenantDescription,
                TenantStatusId = row.TenantStatusId,
                CreatedAt = row.TenantCreatedAt,
                CreatedBy = row.TenantCreatedBy,
                UpdatedAt = row.TenantUpdatedAt,
                UpdatedBy = row.TenantUpdatedBy,
                TenantStatus = new TenantStatus
                {
                    Id = row.TenantStatusId,
                    MasterCode = row.TenantStatusMasterCode,
                    FullName = row.TenantStatusFullName,
                    Description = row.TenantStatusDescription,
                    IsActiveState = row.TenantStatusIsActiveState
                }
            }
        }).ToList();
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
