using Explore.Application.Contracts.Admissions;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class AdmissionTargetOperationsRepository(ExploreDbContext dbContext)
    : IAdmissionTargetOperationsRepository
{
    public async Task<AdmissionTarget?> GetAsync(
        Guid tenantId,
        Guid eventId,
        Guid targetId,
        CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction is not null)
        {
            await AdmissionTargetTransaction.RequireCurrentAsync(
                dbContext,
                tenantId,
                targetId,
                cancellationToken);
            return await dbContext.AdmissionTargets.SingleOrDefaultAsync(target =>
                target.TenantId == tenantId &&
                target.EventId == eventId &&
                target.Id == targetId,
                cancellationToken);
        }

        return await dbContext.AdmissionTargets.AsNoTracking().SingleOrDefaultAsync(target =>
            target.TenantId == tenantId &&
            target.EventId == eventId &&
            target.Id == targetId,
            cancellationToken);
    }

    public Task<AdmissionTarget> UpdateAsync(
        AdmissionTarget target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        AdmissionTargetTransaction.RequireCurrent(dbContext, target.TenantId, target.Id);
        var entry = dbContext.Entry(target);
        if (entry.State == EntityState.Detached) dbContext.AdmissionTargets.Attach(target);
        entry.Property(value => value.AdmissionOperationalStatusId).IsModified = true;
        entry.Property(value => value.ConcurrencyStamp).IsModified = true;
        return Task.FromResult(target);
    }
}
