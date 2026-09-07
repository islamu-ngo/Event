using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class TenantPlanRepository(ExploreDbContext dbContext)
    : GenericRepository<TenantPlan, Guid>(dbContext), ITenantPlanRepository
{
    public async Task<IReadOnlyList<TenantPlan>> ListWithVersionsAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.TenantPlans
            .AsNoTracking()
            .Include(plan => plan.Versions)
                .ThenInclude(version => version.TenantPlanStatus)
            .OrderBy(plan => plan.DisplayName)
            .ToListAsync(cancellationToken);
    }

    public Task<TenantPlan?> GetByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        return dbContext.TenantPlans
            .AsNoTracking()
            .Include(plan => plan.Versions)
                .ThenInclude(version => version.TenantPlanStatus)
            .Include(plan => plan.Versions)
                .ThenInclude(version => version.Settings)
            .Include(plan => plan.Versions)
                .ThenInclude(version => version.Quotas)
            .FirstOrDefaultAsync(plan => plan.Key == key, cancellationToken);
    }

    public Task<TenantPlanVersion?> GetVersionAsync(Guid versionId, CancellationToken cancellationToken = default)
    {
        return dbContext.TenantPlanVersions
            .AsNoTracking()
            .Include(version => version.TenantPlan)
            .Include(version => version.Settings)
            .Include(version => version.Quotas)
            .FirstOrDefaultAsync(version => version.Id == versionId, cancellationToken);
    }

    public Task<TenantPlanVersion?> GetVersionForUpdateAsync(
        Guid versionId,
        CancellationToken cancellationToken = default)
    {
        return dbContext.TenantPlanVersions
            .Include(version => version.TenantPlan)
            .Include(version => version.Settings)
            .Include(version => version.Quotas)
            .FirstOrDefaultAsync(version => version.Id == versionId, cancellationToken);
    }

    public async Task CreateVersionAsync(TenantPlanVersion version, CancellationToken cancellationToken = default)
    {
        await dbContext.TenantPlanVersions.AddAsync(version, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ReplaceVersionContentAsync(TenantPlanVersion version, CancellationToken cancellationToken = default)
    {
        TenantPlanVersion? tracked = await dbContext.TenantPlanVersions
            .Include(existing => existing.Settings)
            .Include(existing => existing.Quotas)
            .FirstOrDefaultAsync(existing => existing.Id == version.Id, cancellationToken);

        if (tracked is null)
        {
            return;
        }

        tracked.PriceAmount = version.PriceAmount;
        tracked.CurrencyCode = version.CurrencyCode;
        tracked.BillingPeriod = version.BillingPeriod;
        tracked.IsActiveForProvisioning = version.IsActiveForProvisioning;

        dbContext.TenantPlanVersionSettings.RemoveRange(tracked.Settings);
        dbContext.TenantPlanVersionQuotas.RemoveRange(tracked.Quotas);
        tracked.Settings.Clear();
        tracked.Quotas.Clear();

        foreach (TenantPlanVersionSetting setting in version.Settings)
        {
            tracked.Settings.Add(new TenantPlanVersionSetting
            {
                Id = setting.Id,
                TenantPlanVersion = tracked,
                TenantPlanVersionId = tracked.Id,
                SettingKey = setting.SettingKey,
                JsonValue = setting.JsonValue,
                IsLocked = setting.IsLocked
            });
        }

        foreach (TenantPlanVersionQuota quota in version.Quotas)
        {
            tracked.Quotas.Add(new TenantPlanVersionQuota
            {
                Id = quota.Id,
                TenantPlanVersion = tracked,
                TenantPlanVersionId = tracked.Id,
                QuotaKey = quota.QuotaKey,
                Limit = quota.Limit
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateVersionAsync(TenantPlanVersion version, CancellationToken cancellationToken = default)
    {
        dbContext.TenantPlanVersions.Update(version);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TenantPlanAssignment>> ListActiveAssignmentsForPlanAsync(
        Guid tenantPlanId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.TenantPlanAssignments
            .AsNoTracking()
            .Include(assignment => assignment.TenantPlan)
            .Include(assignment => assignment.TenantPlanVersion)
            .Where(assignment => assignment.TenantPlanId == tenantPlanId
                && assignment.TenantPlanAssignmentStatusId == (int)TenantPlanAssignmentStatusEnum.Active)
            .ToListAsync(cancellationToken);
    }

    public Task<TenantPlanAssignment?> GetActiveAssignmentForTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        return dbContext.TenantPlanAssignments
            .AsNoTracking()
            .Include(assignment => assignment.TenantPlan)
            .Include(assignment => assignment.TenantPlanVersion)
            .FirstOrDefaultAsync(
                assignment => assignment.TenantId == tenantId
                    && assignment.TenantPlanAssignmentStatusId == (int)TenantPlanAssignmentStatusEnum.Active,
                cancellationToken);
    }

    public Task<TenantPlanAssignment?> GetPreviousEligibleAssignmentForTenantAsync(
        Guid tenantId,
        Guid currentAssignmentId,
        CancellationToken cancellationToken = default)
    {
        return dbContext.TenantPlanAssignments
            .AsNoTracking()
            .Include(assignment => assignment.TenantPlan)
            .Include(assignment => assignment.TenantPlanVersion)
            .Include(assignment => assignment.TenantPlanAssignmentStatus)
            .Where(assignment => assignment.TenantId == tenantId
                && assignment.Id != currentAssignmentId
                && assignment.TenantPlanAssignmentStatusId == (int)TenantPlanAssignmentStatusEnum.Superseded
                && assignment.TenantPlanVersion.TenantPlanStatusId == (int)TenantPlanStatusEnum.Published
                && assignment.EndedAt != null)
            .OrderByDescending(assignment => assignment.EndedAt)
            .ThenByDescending(assignment => assignment.AssignedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<TenantPlanAssignment?> GetAssignmentAsync(Guid assignmentId, CancellationToken cancellationToken = default)
    {
        return dbContext.TenantPlanAssignments
            .AsNoTracking()
            .Include(assignment => assignment.TenantPlan)
            .Include(assignment => assignment.TenantPlanVersion)
            .FirstOrDefaultAsync(assignment => assignment.Id == assignmentId, cancellationToken);
    }

    public async Task<TenantPlanAssignment> CreateAssignmentAsync(
        TenantPlanAssignment assignment,
        CancellationToken cancellationToken = default)
    {
        await dbContext.TenantPlanAssignments.AddAsync(assignment, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return assignment;
    }

    public async Task UpdateAssignmentAsync(TenantPlanAssignment assignment, CancellationToken cancellationToken = default)
    {
        dbContext.TenantPlanAssignments.Update(assignment);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
