using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Federation;
using Explore.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class AtprotoEventProjectionRepository(ExploreDbContext dbContext)
    : IAtprotoEventProjectionRepository
{
    public async Task<(IReadOnlyList<AtprotoEventProjection> Items, int TotalCount)> GetPublicWindowAsync(
        AtprotoEventProjectionQuery query,
        CancellationToken cancellationToken)
    {
        IQueryable<AtprotoEventProjection> filtered = VisibleQuery();

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            string pattern = $"%{query.SearchTerm.Trim().ToLowerInvariant()}%";
            filtered = filtered.Where(value =>
                EF.Functions.Like(value.Name.ToLower(), pattern)
                || (value.Description != null && EF.Functions.Like(value.Description.ToLower(), pattern)));
        }

        if (query.DateFrom.HasValue)
        {
            DateTimeOffset from = new(query.DateFrom.Value, TimeOnly.MinValue, TimeSpan.Zero);
            filtered = filtered.Where(value => value.StartsAt != null && value.StartsAt >= from);
        }

        if (query.DateTo.HasValue)
        {
            DateTimeOffset toExclusive = new(query.DateTo.Value.AddDays(1), TimeOnly.MinValue, TimeSpan.Zero);
            filtered = filtered.Where(value => value.StartsAt != null && value.StartsAt < toExclusive);
        }

        if (query.Modes is { Count: > 0 })
        {
            string[] modes = query.Modes.ToArray();
            filtered = filtered.Where(value => value.Mode != null && modes.Contains(value.Mode));
        }

        filtered = query.TemporalFilter switch
        {
            AtprotoEventTemporalFilter.CurrentOrUpcoming => filtered.Where(value =>
                value.StartsAt != null && (value.EndsAt ?? value.StartsAt) > query.Now),
            AtprotoEventTemporalFilter.Upcoming => filtered.Where(value => value.StartsAt > query.Now),
            AtprotoEventTemporalFilter.Ongoing => filtered.Where(value =>
                value.StartsAt != null && value.StartsAt <= query.Now && value.EndsAt > query.Now),
            AtprotoEventTemporalFilter.Past => filtered.Where(value =>
                value.StartsAt != null && (value.EndsAt ?? value.StartsAt) <= query.Now),
            _ => filtered
        };

        int totalCount = await filtered.CountAsync(cancellationToken);
        IReadOnlyList<AtprotoEventProjection> items = await Order(filtered, query.Sort, query.SortDescending)
            .Take(query.Take)
            .ToListAsync(cancellationToken);
        return (items, totalCount);
    }

    public async Task<IReadOnlyList<AtprotoEventProjection>> GetVisibleByRecordIdsAsync(
        IReadOnlyCollection<Guid> atprotoRecordIds,
        CancellationToken cancellationToken)
    {
        if (atprotoRecordIds.Count == 0)
        {
            return [];
        }

        return await VisibleQuery(includeLocalEchoes: true)
            .Where(value => atprotoRecordIds.Contains(value.AtprotoRecordId))
            .OrderBy(value => value.AtprotoRecordId)
            .ToListAsync(cancellationToken);
    }

    public Task<AtprotoEventProjection?> GetVisibleByRecordIdAsync(
        Guid atprotoRecordId,
        CancellationToken cancellationToken) =>
        VisibleQuery(includeLocalEchoes: true)
            .SingleOrDefaultAsync(value => value.AtprotoRecordId == atprotoRecordId, cancellationToken);

    private IQueryable<AtprotoEventProjection> VisibleQuery(bool includeLocalEchoes = false)
    {
        IQueryable<Event> locallyEligibleEvents = dbContext.Events.WherePubliclyEligible(dbContext);
        IQueryable<AtprotoEventProjection> query = dbContext.AtprotoEventProjections
            .AsNoTracking()
            .Where(projection => dbContext.AtprotoRecords.Any(record =>
                record.Id == projection.AtprotoRecordId
                && record.TombstonedAt == null
                && (dbContext.AtprotoOutboundRecordOwnerships.Any(ownership =>
                    ownership.AtprotoRecordId == record.Id
                    && ownership.SourceEntityType == nameof(Event))
                    ? locallyEligibleEvents.Any(@event =>
                        @event.AtprotoRecordId == record.Id
                        && dbContext.AtprotoOutboundRecordOwnerships.Any(ownership =>
                            ownership.AtprotoRecordId == record.Id
                            && ownership.SourceEntityType == nameof(Event)
                            && ownership.SourceEntityId == @event.Id
                            && ownership.TenantId == @event.TenantId))
                    : dbContext.Events.Any(@event =>
                            @event.AtprotoRecordId == record.Id
                            && !@event.IsDeleted
                            && @event.VisibilityTypeId == (int)VisibilityTypeEnum.Public
                            && @event.EventStatusId != (int)EventStatusEnum.Moderated
                            && @event.EventStatusId != (int)EventStatusEnum.Archived
                            && @event.Actor != null
                            && !@event.Actor.IsDeleted
                            && !@event.Actor.IsSuspended
                            && dbContext.AtprotoRecordTenantPresentations.Any(presentation =>
                                presentation.TenantId == @event.TenantId
                                && presentation.AtprotoRecordId == record.Id
                                && presentation.IsVisible
                                && presentation.SourceVersion == record.SourceVersion)
                            && dbContext.AtprotoIdentities.Any(identity =>
                                identity.ActorId == @event.ActorId
                                && identity.Did == record.Did
                                && identity.IsActive
                                && !identity.IsSuspended
                                && !identity.IsDeleted)))));

        return includeLocalEchoes
            ? query
            : query.Where(projection => !dbContext.Events.Any(@event =>
                @event.AtprotoRecordId == projection.AtprotoRecordId
                && !@event.IsDeleted
                && @event.EventStatusId == (int)EventStatusEnum.Published
                && @event.VisibilityTypeId == (int)VisibilityTypeEnum.Public));
    }

    private static IOrderedQueryable<AtprotoEventProjection> Order(
        IQueryable<AtprotoEventProjection> query,
        AtprotoEventDiscoverySort sort,
        bool descending) => (sort, descending) switch
        {
            (AtprotoEventDiscoverySort.Title, false) => query
                .OrderBy(value => value.Name)
                .ThenBy(value => value.AtprotoRecordId),
            (AtprotoEventDiscoverySort.Title, true) => query
                .OrderByDescending(value => value.Name)
                .ThenBy(value => value.AtprotoRecordId),
            (AtprotoEventDiscoverySort.CreatedAt, false) => query
                .OrderBy(value => value.CreatedAt)
                .ThenBy(value => value.AtprotoRecordId),
            (AtprotoEventDiscoverySort.CreatedAt, true) => query
                .OrderByDescending(value => value.CreatedAt)
                .ThenBy(value => value.AtprotoRecordId),
            (AtprotoEventDiscoverySort.Views, _) => query.OrderBy(value => value.AtprotoRecordId),
            (_, false) => query
                .OrderBy(value => value.StartsAt)
                .ThenBy(value => value.AtprotoRecordId),
            _ => query
                .OrderByDescending(value => value.StartsAt)
                .ThenBy(value => value.AtprotoRecordId)
        };
}
