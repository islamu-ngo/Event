using Explore.Application.Contracts.Admissions;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class AdmissionTargetMaterializationRepository(ExploreDbContext dbContext)
    : IAdmissionTargetMaterializationRepository
{
    private bool materializationFenceAcquired;

    public async Task<IReadOnlyList<EventSession>> ListScheduleSessionsForUpdateAsync(
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken)
    {
        await AcquireMaterializationFenceAsync(tenantId, eventId, cancellationToken);
        return await dbContext.EventSessions
            .Where(session =>
                session.TenantId == tenantId &&
                session.EventId == eventId &&
                !session.IsDeleted)
            .OrderBy(session => session.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AdmissionTarget>> ListTargetsForUpdateAsync(
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken)
    {
        await AcquireMaterializationFenceAsync(tenantId, eventId, cancellationToken);
        return await dbContext.AdmissionTargets
            .Where(target => target.TenantId == tenantId && target.EventId == eventId)
            .OrderBy(target => target.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AdmissionCheckInPolicy>> ListPoliciesAsync(
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken) => await dbContext.AdmissionCheckInPolicies
            .Where(policy =>
                policy.TenantId == tenantId &&
                dbContext.AdmissionTargets.Any(target =>
                    target.TenantId == tenantId &&
                    target.EventId == eventId &&
                    target.Id == policy.AdmissionTargetId))
            .OrderBy(policy => policy.Id)
            .ToListAsync(cancellationToken);

    public async Task AddTargetsAsync(
        IReadOnlyCollection<AdmissionTarget> targets,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(targets);
        await dbContext.AdmissionTargets.AddRangeAsync(targets, cancellationToken);
    }

    public async Task AddPoliciesAsync(
        IReadOnlyCollection<AdmissionCheckInPolicy> policies,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policies);
        await dbContext.AdmissionCheckInPolicies.AddRangeAsync(policies, cancellationToken);
    }

    private async Task AcquireMaterializationFenceAsync(
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational() || materializationFenceAcquired)
        {
            return;
        }

        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Admission target materialization fences require an active transaction.");
        }

        await using IAsyncDisposable materializationLock =
            await RelationalNamedLock.AcquireTransactionAsync(
                dbContext,
                $"admission-target-materialization:{tenantId:N}:{eventId:N}",
                cancellationToken);
        materializationFenceAcquired = true;
    }
}
