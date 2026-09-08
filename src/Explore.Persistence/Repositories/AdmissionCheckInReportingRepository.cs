using Explore.Application.Contracts.Admissions;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class AdmissionCheckInReportingRepository(ExploreDbContext dbContext)
    : IAdmissionCheckInSummaryQuery, IAdmissionCheckInReportingRepository
{
    private const int MaximumAuditRows = 101;
    private const int MaximumTargetBatchSize = 100;

    public Task<AdmissionCheckInEvent?> GetEventAsync(
        Guid tenantId,
        Guid eventId,
        Guid checkInId,
        CancellationToken cancellationToken) => (
            from fact in dbContext.AdmissionCheckInEvents.AsNoTracking()
            join target in dbContext.AdmissionTargets.AsNoTracking()
                on new { fact.TenantId, Id = fact.AdmissionTargetId }
                equals new { target.TenantId, target.Id }
            where fact.TenantId == tenantId &&
                  target.EventId == eventId &&
                  fact.Id == checkInId
            select fact)
        .SingleOrDefaultAsync(cancellationToken);

    public Task<AdmissionCheckInEvent?> GetActiveEventAsync(
        Guid tenantId,
        Guid ticketId,
        Guid targetId,
        CancellationToken cancellationToken) => (
            from state in dbContext.AdmissionCheckInStates.AsNoTracking()
            join fact in dbContext.AdmissionCheckInEvents.AsNoTracking()
                on new
                {
                    state.TenantId,
                    state.AdmissionTicketId,
                    state.AdmissionTargetId,
                    Id = state.ActiveCheckInEventId
                }
                equals new
                {
                    fact.TenantId,
                    fact.AdmissionTicketId,
                    fact.AdmissionTargetId,
                    Id = (Guid?)fact.Id
                }
            where state.TenantId == tenantId &&
                  state.AdmissionTicketId == ticketId &&
                  state.AdmissionTargetId == targetId
            select fact)
        .SingleOrDefaultAsync(cancellationToken);

    public Task<AdmissionCheckInSummaryProjection?> GetAsync(
        Guid tenantId,
        Guid eventId,
        Guid targetId,
        CancellationToken cancellationToken) => dbContext.AdmissionTargets
        .AsNoTracking()
        .Where(target =>
            target.TenantId == tenantId &&
            target.EventId == eventId &&
            target.Id == targetId)
        .Select(target => new AdmissionCheckInSummaryProjection(
            target.TenantId,
            target.EventId,
            target.Id,
            (AdmissionTargetTypeEnum)target.AdmissionTargetTypeId,
            dbContext.AdmissionCheckInEvents.LongCount(value =>
                value.TenantId == tenantId &&
                value.AdmissionTargetId == targetId &&
                value.AdmissionCheckInActionId == (int)AdmissionCheckInActionEnum.CheckIn),
            dbContext.AdmissionCheckInEvents.LongCount(value =>
                value.TenantId == tenantId &&
                value.AdmissionTargetId == targetId &&
                value.AdmissionCheckInActionId == (int)AdmissionCheckInActionEnum.Undo),
            dbContext.AdmissionCheckInStates.LongCount(value =>
                value.TenantId == tenantId &&
                value.AdmissionTargetId == targetId &&
                value.ActiveCheckInEventId != null),
            dbContext.AdmissionCheckInStates.LongCount(value =>
                value.TenantId == tenantId &&
                value.AdmissionTargetId == targetId &&
                value.ActiveCheckInEventId == null),
            dbContext.AdmissionCheckInEvents
                .Where(value =>
                    value.TenantId == tenantId &&
                    value.AdmissionTargetId == targetId)
                .Max(value => (DateTime?)value.OccurredAtUtc)))
        .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<AdmissionCheckInEvent>> ListEventAuditPageAsync(
        Guid tenantId,
        Guid eventId,
        AdmissionCheckInEvent? cursor,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (pageSize is < 1 or > MaximumAuditRows)
        {
            return [];
        }

        IQueryable<AdmissionCheckInEvent> query =
            from fact in dbContext.AdmissionCheckInEvents.AsNoTracking()
            join target in dbContext.AdmissionTargets.AsNoTracking()
                on new { fact.TenantId, Id = fact.AdmissionTargetId }
                equals new { target.TenantId, target.Id }
            where fact.TenantId == tenantId &&
                target.TenantId == tenantId &&
                target.EventId == eventId
            select fact;
        if (cursor is not null)
        {
            query = query.Where(fact =>
                fact.OccurredAtUtc < cursor.OccurredAtUtc ||
                fact.OccurredAtUtc == cursor.OccurredAtUtc &&
                fact.Id.CompareTo(cursor.Id) < 0);
        }

        return await query
            .OrderByDescending(fact => fact.OccurredAtUtc)
            .ThenByDescending(fact => fact.Id)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AdmissionTarget>> ListTargetsAsync(
        Guid tenantId,
        Guid eventId,
        IReadOnlyList<Guid> targetIds,
        CancellationToken cancellationToken)
    {
        if (targetIds is not { Count: > 0 and <= MaximumTargetBatchSize } ||
            targetIds.Any(targetId => targetId == Guid.Empty) ||
            targetIds.Distinct().Count() != targetIds.Count)
        {
            return [];
        }

        Guid[] ids = targetIds.ToArray();
        return await dbContext.AdmissionTargets
            .AsNoTracking()
            .Where(target =>
                target.TenantId == tenantId &&
                target.EventId == eventId &&
                ids.Contains(target.Id))
            .OrderBy(target => target.Id)
            .ToArrayAsync(cancellationToken);
    }
}
