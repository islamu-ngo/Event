using Explore.Application.Contracts.Persistence;
using Explore.Application.Specifications.Events;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Extensions;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class EventRepository : GenericRepository<Event, Guid>, IEventRepository
{
    private readonly ExploreDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public EventRepository(ExploreDbContext dbContext, TimeProvider? timeProvider = null) : base(dbContext)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public new async Task<Event?> GetById(Guid id)
    {
        return await _dbContext.Events
            .AsNoTracking()
            .Include(e => e.ParticipationConfiguration)
            .FirstOrDefaultAsync(e => e.Id == id);
    }

    public async Task<List<Event>> GetEventsWithDetails()
    {
        return await _dbContext.Events
            .AsNoTrackingWithIdentityResolution()
            .AsSplitQuery()
            .IncludeStandardDetails()
            .ToListAsync();
    }

    public async Task<Event?> GetEventWithDetails(Guid id)
    {
        return await _dbContext.Events
            .AsNoTrackingWithIdentityResolution()
            .AsSplitQuery()
            .IncludeStandardDetails()
            .Include(e => e.AtprotoRecord)
            .FirstOrDefaultAsync(e => e.Id == id);
    }

    public Task<Event?> GetEventWithDetailsAsync(
        Guid id,
        Guid tenantId,
        CancellationToken cancellationToken) =>
        _dbContext.Events
            .AsNoTrackingWithIdentityResolution()
            .AsSplitQuery()
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .IncludeStandardDetails()
            .Include(e => e.AtprotoRecord)
            .FirstOrDefaultAsync(e => e.Id == id && e.TenantId == tenantId, cancellationToken);

    public async Task<Event?> GetPublicEventWithDetailsByCodeAsync(string publicCode, CancellationToken cancellationToken)
    {
        return await _dbContext.Events
            .AsNoTrackingWithIdentityResolution()
            .AsSplitQuery()
            .IncludeStandardDetails()
            .Include(e => e.AtprotoRecord)
            .WherePubliclyEligible(_dbContext)
            .FirstOrDefaultAsync(e => e.PublicCode == publicCode, cancellationToken);
    }

    public async Task<Event?> GetPublicEventForOpenGraphAsync(string publicCode, CancellationToken cancellationToken)
    {
        return await _dbContext.Events
            .AsNoTracking()
            .WherePubliclyEligible(_dbContext)
            .Where(e => e.PublicCode == publicCode)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<bool> IsPubliclyEligibleAsync(Guid tenantId, Guid eventId, CancellationToken cancellationToken) =>
        _dbContext.Events
            .AsNoTracking()
            .WherePubliclyEligible(_dbContext)
            .AnyAsync(@event => @event.TenantId == tenantId && @event.Id == eventId, cancellationToken);

    public async Task<Event?> GetRegistrationStatusEventForUpdateAsync(Guid id, Guid tenantId, CancellationToken cancellationToken)
    {
        await RelationalEntityRowFence.AcquireAsync<Event>(
            _dbContext, tenantId, target => target.Id, id, cancellationToken);
        return await _dbContext.Events.AsNoTracking()
            .Include(target => target.ParticipationConfiguration)
            .FirstOrDefaultAsync(target => target.Id == id && target.TenantId == tenantId, cancellationToken);
    }

    public async Task<Event?> GetAuthorizationTargetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _dbContext.Events
            .AsNoTracking()
            .IgnoreTenantFilter(TenantFilterBypassReasons.EventAuthorizationTargetResolution)
            .Include(e => e.ParticipationConfiguration)
            .Include(e => e.OrganizerActor)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Event>> GetAuthorizationTargetsByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        Guid[] normalizedIds = ids.Distinct().ToArray();
        if (normalizedIds.Any(id => id == Guid.Empty))
        {
            throw new ArgumentException("Authorization target ids must be non-empty.", nameof(ids));
        }

        if (normalizedIds.Length > IEventRepository.MaximumAuthorizationTargetBatchSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ids),
                $"Event authorization target batches cannot exceed {IEventRepository.MaximumAuthorizationTargetBatchSize} unique ids.");
        }

        if (normalizedIds.Length == 0)
        {
            return [];
        }

        return await _dbContext.Events
            .AsNoTracking()
            .Include(eventEntity => eventEntity.Actor)
            .Include(eventEntity => eventEntity.OrganizerActor)
            .Where(eventEntity => normalizedIds.Contains(eventEntity.Id))
            .OrderBy(eventEntity => eventEntity.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<AtprotoEventPublicationEntityGraph?> GetAtprotoPublicationGraphAsync(
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || eventId == Guid.Empty)
        {
            return null;
        }

        Event? eventEntity = await TenantScoped(_dbContext.Events, tenantId)
            .AsNoTrackingWithIdentityResolution()
            .AsSplitQuery()
            .IncludeStandardDetails()
            .Include(e => e.BackgroundImage)
            .Include(e => e.EventSeries)
                .ThenInclude(series => series!.FeaturedImage)
            .Include(e => e.EventSeries)
                .ThenInclude(series => series!.VisibilityType)
            .Include(e => e.EventSeries)
                .ThenInclude(series => series!.Actor)
                    .ThenInclude(actor => actor.Pii)
            .Include(e => e.EventSeries)
                .ThenInclude(series => series!.Actor)
                    .ThenInclude(actor => actor.AtprotoIdentities)
            .Include(e => e.Actor)
                .ThenInclude(actor => actor.Organization)
                    .ThenInclude(organization => organization!.Pii)
            .Include(e => e.Actor)
                .ThenInclude(actor => actor.Group)
            .Include(e => e.Actor)
                .ThenInclude(actor => actor.AtprotoIdentities)
            .FirstOrDefaultAsync(e => e.Id == eventId, cancellationToken);

        if (eventEntity is null)
        {
            return null;
        }

        List<EventLocation> eventLocations = await TenantScoped(_dbContext.EventLocations, tenantId)
            .AsNoTrackingWithIdentityResolution()
            .AsSplitQuery()
            .Include(placement => placement.Location)
                .ThenInclude(location => location!.Pii)
            .Include(placement => placement.Location)
                .ThenInclude(location => location!.Rooms)
            .Where(placement => placement.EventId == eventId)
            .ToListAsync(cancellationToken);

        List<EventSession> sessions = await TenantScoped(_dbContext.EventSessions, tenantId)
            .AsNoTracking()
            .Include(session => session.EventSessionKind)
            .Include(session => session.EventSessionStatus)
            .Include(session => session.RegistrationMode)
            .Include(session => session.FeaturedImage)
            .Include(session => session.IslamicAspect)
            .Where(session => session.EventId == eventId)
            .ToListAsync(cancellationToken);

        List<EventDay> days = await TenantScoped(_dbContext.EventDays, tenantId)
            .AsNoTracking()
            .Include(day => day.BannerImage)
            .Where(day => day.EventId == eventId)
            .ToListAsync(cancellationToken);

        List<EventSessionGroup> sessionGroups = await TenantScoped(_dbContext.EventSessionGroups, tenantId)
            .AsNoTracking()
            .Where(group => group.EventId == eventId)
            .ToListAsync(cancellationToken);

        List<EventSessionGroupSession> sessionGroupSessions = await TenantScoped(_dbContext.EventSessionGroupSessions, tenantId)
            .AsNoTracking()
            .Where(link => link.EventId == eventId)
            .ToListAsync(cancellationToken);

        List<EventAgendaItem> agendaItems = await TenantScoped(_dbContext.EventAgendaItems, tenantId)
            .AsNoTracking()
            .Include(item => item.Kind)
            .Where(item => item.EventId == eventId)
            .ToListAsync(cancellationToken);

        Guid[] sessionIds = sessions.Select(session => session.Id).ToArray();
        List<EventSessionAgendaItem> sessionAgendaItems = await TenantScoped(_dbContext.EventSessionAgendaItems, tenantId)
            .AsNoTracking()
            .Where(item => sessionIds.Contains(item.EventSessionId))
            .ToListAsync(cancellationToken);

        List<EventCategories> categories = await TenantScoped(_dbContext.EventCategories, tenantId)
            .AsNoTracking()
            .Include(link => link.Category)
                .ThenInclude(category => category.Parent)
            .Where(link => link.EventId == eventId)
            .ToListAsync(cancellationToken);

        List<EventTags> tags = await TenantScoped(_dbContext.EventTags, tenantId)
            .AsNoTracking()
            .Include(link => link.Tag)
            .Where(link => link.EventId == eventId)
            .ToListAsync(cancellationToken);

        List<EventSessionCategory> sessionCategories = await TenantScoped(_dbContext.EventSessionCategories, tenantId)
            .AsNoTracking()
            .Include(link => link.Category)
                .ThenInclude(category => category.Parent)
            .Where(link => sessionIds.Contains(link.EventSessionId))
            .ToListAsync(cancellationToken);

        List<EventSessionTag> sessionTags = await TenantScoped(_dbContext.EventSessionTags, tenantId)
            .AsNoTracking()
            .Include(link => link.Tag)
            .Where(link => sessionIds.Contains(link.EventSessionId))
            .ToListAsync(cancellationToken);

        List<EventSessionLanguage> sessionLanguages = await TenantScoped(_dbContext.EventSessionLanguages, tenantId)
            .AsNoTracking()
            .Include(link => link.Language)
            .Where(link => sessionIds.Contains(link.EventSessionId))
            .ToListAsync(cancellationToken);

        List<EventSessionSpeaker> sessionSpeakers = await TenantScoped(_dbContext.EventSessionSpeakers, tenantId)
            .AsNoTrackingWithIdentityResolution()
            .Include(link => link.Actor)
                .ThenInclude(actor => actor.Pii)
            .Include(link => link.Actor)
                .ThenInclude(actor => actor.AtprotoIdentities)
            .Where(link => sessionIds.Contains(link.EventSessionId))
            .ToListAsync(cancellationToken);

        List<EventCustomPropertyDefinition> customPropertyDefinitions = await TenantScoped(
                _dbContext.EventCustomPropertyDefinitions,
                tenantId)
            .AsNoTrackingWithIdentityResolution()
            .AsSplitQuery()
            .Include(definition => definition.Options)
            .Include(definition => definition.Values)
                .ThenInclude(value => value.Option)
            .Where(definition => definition.EventId == eventId)
            .ToListAsync(cancellationToken);

        List<EventSessionCustomPropertyDefinition> sessionCustomPropertyDefinitions = await TenantScoped(
                _dbContext.EventSessionCustomPropertyDefinitions,
                tenantId)
            .AsNoTrackingWithIdentityResolution()
            .AsSplitQuery()
            .Include(definition => definition.Options)
            .Include(definition => definition.Values)
                .ThenInclude(value => value.Option)
            .Where(definition => sessionIds.Contains(definition.EventSessionId))
            .ToListAsync(cancellationToken);

        return new(
            eventEntity,
            eventLocations,
            sessions,
            days,
            sessionGroups,
            sessionGroupSessions,
            agendaItems,
            sessionAgendaItems,
            categories,
            tags,
            sessionCategories,
            sessionTags,
            sessionLanguages,
            sessionSpeakers,
            customPropertyDefinitions,
            sessionCustomPropertyDefinitions);
    }

    public Task<Event?> GetAtprotoLifecycleStateAsync(
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken) =>
        TenantScoped(_dbContext.Events, tenantId)
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == eventId, cancellationToken);

    private static IQueryable<TEntity> TenantScoped<TEntity>(
        DbSet<TEntity> set,
        Guid tenantId)
        where TEntity : class, Explore.Domain.Interfaces.ITenantEntity
        => set
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .Where(entity => entity.TenantId == tenantId);

    public async Task<Event?> GetScheduleGraphForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _dbContext.Events
            .AsSplitQuery()
            .Include(e => e.Days)
            .Include(e => e.Sessions)
            .Include(e => e.AgendaItems)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
    }

    public async Task<List<Event>> GetMyEventsWithDetails(string userId)
    {
        Guid userGuid;
        bool isGuid = Guid.TryParse(userId, out userGuid);

        var query = _dbContext.Events
            .AsNoTrackingWithIdentityResolution()
            .AsSplitQuery()
            .IncludeStandardDetails()
            .AsQueryable();

        if (isGuid)
        {
            var userActorIds = _dbContext.Actors
                .Where(actor => actor.UserId == userGuid)
                .Select(actor => actor.Id);

            var orgActorIds = _dbContext.OrganizationMembers
                .Where(member => member.UserId == userGuid
                    && member.OrganizationTenant.Organization.Actor != null)
                .Select(member => member.OrganizationTenant.Organization.Actor!.Id);

            var groupActorIds = _dbContext.GroupMembers
                .Where(member => member.UserId == userGuid
                    && member.GroupTenant.Group.Actor != null)
                .Select(member => member.GroupTenant.Group.Actor!.Id);

            var allActorIds = userActorIds.Union(orgActorIds).Union(groupActorIds);

            query = query.Where(e => allActorIds.Contains(e.ActorId));
        }

        return await query.ToListAsync();
    }

    public async Task<IReadOnlyList<Event>> GetEventsByActorWithDetails(Guid actorId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Events
            .AsNoTrackingWithIdentityResolution()
            .AsSplitQuery()
            .IncludeStandardDetails()
            .Where(e => e.ActorId == actorId)
            .OrderByDescending(e => e.FirstSessionStartUtc)
            .ThenBy(e => e.Title)
            .ThenBy(e => e.Id)
            .ToListAsync(cancellationToken);
    }


    public async Task<(List<Event> Items, int TotalCount)> GetEventsWithDetailsPaged(int pageNumber, int pageSize)
    {
        var query = _dbContext.Events
            .AsNoTrackingWithIdentityResolution()
            .AsSplitQuery()
            .IncludeStandardDetails()
            .OrderByDescending(e => e.FirstSessionDate);

        var totalCount = await query.CountAsync();
        var items = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    /// <inheritdoc />
    public async Task<(List<Event> Items, int TotalCount)> GetEventsWithDetailsPaged(
        int pageNumber, int pageSize, EventQuerySpecification specification)
    {
        var query = _dbContext.Events
            .AsNoTrackingWithIdentityResolution()
            .AsSplitQuery()
            .IncludeStandardDetails()
            .AsQueryable();

        var now = _timeProvider.GetUtcNow();
        query = ApplySubqueryFilters(query, specification, now);
        query = ApplyProjectionFilters(query, specification);

        if (specification.Filters.OfType<EventFilter>().Any(filter =>
                filter.FilterType == EventFilterType.PubliclyDiscoverable))
        {
            query = query.WherePubliclyEligible(_dbContext);
        }

        // Apply direct filters and sorting via specification
        query = specification.Apply(query, now);

        // If no sort was specified by the specification, default to date descending
        if (!specification.HasSort)
        {
            query = query
                .OrderByDescending(e => e.FirstSessionStartUtc)
                .ThenBy(e => e.Id);
        }
        else if (query is IOrderedQueryable<Event> orderedQuery)
        {
            query = orderedQuery.ThenBy(e => e.Id);
        }

        var totalCount = await query.CountAsync();
        var items = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    /// <inheritdoc />
    public async Task<List<Event>> GetPublishedPublicEventsForSitemap(
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Events
            .AsNoTracking()
            .WherePubliclyEligible(_dbContext)
            .OrderByDescending(e => e.UpdatedAt ?? e.CreatedAt)
            .ThenBy(e => e.Id)
            .Take(maxCount)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Event>> SearchAiReferenceEventsAsync(
        string searchTerm,
        int limit,
        CancellationToken cancellationToken)
    {
        string trimmedTerm = searchTerm.Trim();

        if (string.IsNullOrWhiteSpace(trimmedTerm) || limit <= 0)
        {
            return [];
        }

        var specification = new EventQuerySpecification()
            .And(EventFilter.PubliclyDiscoverable())
            .And(EventFilter.SearchTerm(trimmedTerm));

        IQueryable<Event> query = _dbContext.Events
            .AsNoTracking()
            .Include(e => e.EventStatus)
            .Include(e => e.VisibilityType)
            .Include(e => e.EventFormat)
            .WherePubliclyEligible(_dbContext);

        query = specification.Apply(query, DateTimeOffset.UtcNow);

        return await query
            .OrderBy(e => e.FirstSessionStartUtc == null)
            .ThenBy(e => e.FirstSessionStartUtc)
            .ThenBy(e => e.Title)
            .ThenBy(e => e.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Applies subquery filters that require access to junction table DbSets,
    /// and JSONB filters that use PostgreSQL-specific operators.
    /// These filters use correlated subqueries (EXISTS pattern) for efficient SQL translation.
    /// </summary>
    private IQueryable<Event> ApplySubqueryFilters(
        IQueryable<Event> query, EventQuerySpecification specification, DateTimeOffset now)
    {
        var publicDiscoverySessionFacet = specification.Filters
            .OfType<EventFilter>()
            .Any(filter => filter.FilterType == EventFilterType.PubliclyDiscoverable);

        foreach (var subFilter in specification.SubqueryFilters)
        {
            query = subFilter.FilterType switch
            {
                EventSubqueryFilterType.Category => query.Where(e =>
                    _dbContext.EventCategories.Any(ec =>
                        ec.EventId == e.Id && ec.CategoryId == (Guid)subFilter.Value)),

                EventSubqueryFilterType.CategoriesIncludedAny => query.Where(e =>
                    _dbContext.EventCategories.Any(ec =>
                        ec.EventId == e.Id && ((List<Guid>)subFilter.Value).Contains(ec.CategoryId))),

                EventSubqueryFilterType.CategoriesExcludedAny => query.Where(e =>
                    !_dbContext.EventCategories.Any(ec =>
                        ec.EventId == e.Id && ((List<Guid>)subFilter.Value).Contains(ec.CategoryId))),

                // CategoriesIncludedAll and CategoriesExcludedAll are handled below (require loops)
                EventSubqueryFilterType.CategoriesIncludedAll => query,
                EventSubqueryFilterType.CategoriesExcludedAll => query,

                EventSubqueryFilterType.TagsIncludedAny => query.Where(e =>
                    _dbContext.EventTags.Any(et =>
                        et.EventId == e.Id && ((List<Guid>)subFilter.Value).Contains(et.TagId))),

                EventSubqueryFilterType.TagsExcludedAny => query.Where(e =>
                    !_dbContext.EventTags.Any(et =>
                        et.EventId == e.Id && ((List<Guid>)subFilter.Value).Contains(et.TagId))),

                // TagsIncludedAll and TagsExcludedAll are handled below (require loops)
                EventSubqueryFilterType.TagsIncludedAll => query,
                EventSubqueryFilterType.TagsExcludedAll => query,

                EventSubqueryFilterType.Location => query.Where(e =>
                    _dbContext.EventSessions.Any(es =>
                        es.EventId == e.Id &&
                        (!publicDiscoverySessionFacet ||
                            es.EventSessionStatusId == (int)EventSessionStatusEnum.Published &&
                            es.StartTime != null) &&
                        es.LocationId == (Guid)subFilter.Value)),

                EventSubqueryFilterType.Locations => query.Where(e =>
                    _dbContext.EventSessions.Any(es =>
                        es.EventId == e.Id &&
                        (!publicDiscoverySessionFacet ||
                            es.EventSessionStatusId == (int)EventSessionStatusEnum.Published &&
                            es.StartTime != null) &&
                        es.LocationId != null &&
                        ((List<Guid>)subFilter.Value).Contains(es.LocationId.Value))),

                EventSubqueryFilterType.RegistrationMode => query.Where(e =>
                    _dbContext.EventSessions.Any(es =>
                        es.EventId == e.Id &&
                        (!publicDiscoverySessionFacet ||
                            es.EventSessionStatusId == (int)EventSessionStatusEnum.Published &&
                            es.StartTime != null) &&
                        es.RegistrationModeId == (int)subFilter.Value)),

                EventSubqueryFilterType.RegistrationModes => query.Where(e =>
                    _dbContext.EventSessions.Any(es =>
                        es.EventId == e.Id &&
                        (!publicDiscoverySessionFacet ||
                            es.EventSessionStatusId == (int)EventSessionStatusEnum.Published &&
                            es.StartTime != null) &&
                        es.RegistrationModeId != null &&
                        ((List<int>)subFilter.Value).Contains(es.RegistrationModeId.Value))),

                EventSubqueryFilterType.Language => query.Where(e =>
                    _dbContext.EventSessions.Any(es =>
                        es.EventId == e.Id &&
                        (!publicDiscoverySessionFacet ||
                            es.EventSessionStatusId == (int)EventSessionStatusEnum.Published &&
                            es.StartTime != null) &&
                        _dbContext.EventSessionLanguages.Any(esl =>
                            esl.EventSessionId == es.Id && esl.LanguageId == (int)subFilter.Value))),

                EventSubqueryFilterType.Languages => query.Where(e =>
                    _dbContext.EventSessions.Any(es =>
                        es.EventId == e.Id &&
                        (!publicDiscoverySessionFacet ||
                            es.EventSessionStatusId == (int)EventSessionStatusEnum.Published &&
                            es.StartTime != null) &&
                        _dbContext.EventSessionLanguages.Any(esl =>
                            esl.EventSessionId == es.Id &&
                            ((List<int>)subFilter.Value).Contains(esl.LanguageId)))),

                EventSubqueryFilterType.FutureOnly => query.Where(e =>
                    e.LastSessionStartUtc == null || e.LastSessionStartUtc > (DateTimeOffset)subFilter.Value),

                EventSubqueryFilterType.CurrentOrUpcomingPublishedSession =>
                    ApplyTemporalFilter(query, TemporalView.UpcomingAndOngoing, now),

                EventSubqueryFilterType.TemporalView => ApplyTemporalFilter(query, subFilter),

                _ => query
            };

            // Loop-based filters that can't be expressed in a switch expression
            if (subFilter.FilterType == EventSubqueryFilterType.TagsIncludedAll)
            {
                var tagIds = (List<Guid>)subFilter.Value;
                foreach (var tagId in tagIds)
                {
                    query = query.Where(e =>
                        _dbContext.EventTags.Any(et => et.EventId == e.Id && et.TagId == tagId));
                }
            }
            else if (subFilter.FilterType == EventSubqueryFilterType.TagsExcludedAll)
            {
                var tagIds = (List<Guid>)subFilter.Value;
                // Logically: NOT (Exists(T1) AND Exists(T2) AND ...)
                // Is equivalent to: NOT Exists(T1) OR NOT Exists(T2) OR ...
                // But for EF compatibility, we use a single Where with a manual expression if needed,
                // or just stay with the existing one if we can fix it.
                // The issue is tid is a variable from outside.

                // Let's try a more robust pattern:
                query = query.Where(e => _dbContext.EventTags
                    .Where(et => et.EventId == e.Id && tagIds.Contains(et.TagId))
                    .Select(et => et.TagId)
                    .Distinct()
                    .Count() < tagIds.Count);
            }
            else if (subFilter.FilterType == EventSubqueryFilterType.CategoriesIncludedAll)
            {
                var categoryIds = (List<Guid>)subFilter.Value;
                foreach (var categoryId in categoryIds)
                {
                    query = query.Where(e =>
                        _dbContext.EventCategories.Any(ec => ec.EventId == e.Id && ec.CategoryId == categoryId));
                }
            }
            else if (subFilter.FilterType == EventSubqueryFilterType.CategoriesExcludedAll)
            {
                var categoryIds = (List<Guid>)subFilter.Value;
                query = query.Where(e => _dbContext.EventCategories
                    .Where(ec => ec.EventId == e.Id && categoryIds.Contains(ec.CategoryId))
                    .Select(ec => ec.CategoryId)
                    .Distinct()
                    .Count() < categoryIds.Count);
            }
        }

        return query;
    }

    private IQueryable<Event> ApplyProjectionFilters(
        IQueryable<Event> query, EventQuerySpecification specification)
    {
        foreach (var projFilter in specification.ProjectionFilters)
        {
            query = projFilter.FilterType switch
            {
                EventCustomPropertyProjectionFilterType.ExactMatch => ApplyExactMatchFilter(query, projFilter),
                EventCustomPropertyProjectionFilterType.OptionMatch => ApplyOptionMatchFilter(query, projFilter),
                EventCustomPropertyProjectionFilterType.OptionsMatchAny => ApplyOptionsMatchAnyFilter(query, projFilter),
                EventCustomPropertyProjectionFilterType.TextSearch => ApplyTextSearchFilter(query, projFilter),
                EventCustomPropertyProjectionFilterType.GlobalTextSearch => ApplyGlobalTextSearchFilter(query, projFilter),
                EventCustomPropertyProjectionFilterType.Exists => ApplyExistsFilter(query, projFilter),
                EventCustomPropertyProjectionFilterType.BooleanTrue => ApplyBooleanTrueFilter(query, projFilter),
                EventCustomPropertyProjectionFilterType.NumberRange => ApplyNumberRangeFilter(query, projFilter),
                EventCustomPropertyProjectionFilterType.DateRange => ApplyDateRangeFilter(query, projFilter),
                _ => query
            };
        }

        return query;
    }

    private IQueryable<Event> ApplyExactMatchFilter(IQueryable<Event> query, EventCustomPropertyProjectionFilter filter)
    {
        var (ns, key, normalizedValue) = ((string, string, string))filter.Value;
        var visibleExposureLevels = CustomPropertyExposureScope.VisibleAtOrBelow(filter.ExposureCeiling);
        return query.Where(e => _dbContext.EventCustomPropertyProjections.Any(p =>
            p.EventId == e.Id &&
            p.Namespace == ns &&
            p.Key == key &&
            visibleExposureLevels.Contains(p.ExposureLevel) &&
            p.IsFilterable &&
            p.NormalizedValue == normalizedValue));
    }

    private IQueryable<Event> ApplyOptionMatchFilter(IQueryable<Event> query, EventCustomPropertyProjectionFilter filter)
    {
        var (ns, key, optionId) = ((string, string, Guid))filter.Value;
        var visibleExposureLevels = CustomPropertyExposureScope.VisibleAtOrBelow(filter.ExposureCeiling);
        return query.Where(e => _dbContext.EventCustomPropertyProjections.Any(p =>
            p.EventId == e.Id &&
            p.Namespace == ns &&
            p.Key == key &&
            visibleExposureLevels.Contains(p.ExposureLevel) &&
            p.IsFilterable &&
            p.OptionId == optionId));
    }

    private IQueryable<Event> ApplyOptionsMatchAnyFilter(IQueryable<Event> query, EventCustomPropertyProjectionFilter filter)
    {
        var (ns, key, optionIds) = ((string, string, List<Guid>))filter.Value;
        var visibleExposureLevels = CustomPropertyExposureScope.VisibleAtOrBelow(filter.ExposureCeiling);
        return query.Where(e => _dbContext.EventCustomPropertyProjections.Any(p =>
            p.EventId == e.Id &&
            p.Namespace == ns &&
            p.Key == key &&
            visibleExposureLevels.Contains(p.ExposureLevel) &&
            p.IsFilterable &&
            p.OptionId != null &&
            optionIds.Contains(p.OptionId.Value)));
    }

    private IQueryable<Event> ApplyTextSearchFilter(IQueryable<Event> query, EventCustomPropertyProjectionFilter filter)
    {
        var (ns, key, searchTerm) = ((string, string, string))filter.Value;
        var pattern = $"%{searchTerm.ToLowerInvariant()}%";
        var visibleExposureLevels = CustomPropertyExposureScope.VisibleAtOrBelow(filter.ExposureCeiling);
        return query.Where(e => _dbContext.EventCustomPropertyProjections.Any(p =>
            p.EventId == e.Id &&
            p.Namespace == ns &&
            p.Key == key &&
            visibleExposureLevels.Contains(p.ExposureLevel) &&
            p.IsSearchable &&
            p.NormalizedValue != null &&
            EF.Functions.Like(p.NormalizedValue, pattern)));
    }

    private IQueryable<Event> ApplyGlobalTextSearchFilter(IQueryable<Event> query, EventCustomPropertyProjectionFilter filter)
    {
        var searchTerm = (string)filter.Value;
        var pattern = $"%{searchTerm.ToLowerInvariant()}%";
        var visibleExposureLevels = CustomPropertyExposureScope.VisibleAtOrBelow(filter.ExposureCeiling);
        return query.Where(e => _dbContext.EventCustomPropertyProjections.Any(p =>
            p.EventId == e.Id &&
            visibleExposureLevels.Contains(p.ExposureLevel) &&
            p.IsSearchable &&
            p.NormalizedValue != null &&
            EF.Functions.Like(p.NormalizedValue, pattern)));
    }

    private IQueryable<Event> ApplyExistsFilter(IQueryable<Event> query, EventCustomPropertyProjectionFilter filter)
    {
        var (ns, key) = ((string, string))filter.Value;
        var visibleExposureLevels = CustomPropertyExposureScope.VisibleAtOrBelow(filter.ExposureCeiling);
        return query.Where(e => _dbContext.EventCustomPropertyProjections.Any(p =>
            p.EventId == e.Id &&
            p.Namespace == ns &&
            p.Key == key &&
            visibleExposureLevels.Contains(p.ExposureLevel) &&
            p.IsFilterable));
    }

    private IQueryable<Event> ApplyBooleanTrueFilter(IQueryable<Event> query, EventCustomPropertyProjectionFilter filter)
    {
        var (ns, key) = ((string, string))filter.Value;
        var visibleExposureLevels = CustomPropertyExposureScope.VisibleAtOrBelow(filter.ExposureCeiling);
        return query.Where(e => _dbContext.EventCustomPropertyProjections.Any(p =>
            p.EventId == e.Id &&
            p.Namespace == ns &&
            p.Key == key &&
            visibleExposureLevels.Contains(p.ExposureLevel) &&
            p.IsFilterable &&
            p.BooleanValue == true));
    }

    private IQueryable<Event> ApplyNumberRangeFilter(IQueryable<Event> query, EventCustomPropertyProjectionFilter filter)
    {
        var (ns, key, min, max) = ((string, string, decimal?, decimal?))filter.Value;
        var visibleExposureLevels = CustomPropertyExposureScope.VisibleAtOrBelow(filter.ExposureCeiling);
        return query.Where(e => _dbContext.EventCustomPropertyProjections.Any(p =>
            p.EventId == e.Id &&
            p.Namespace == ns &&
            p.Key == key &&
            visibleExposureLevels.Contains(p.ExposureLevel) &&
            p.IsFilterable &&
            p.NumberValue != null &&
            (!min.HasValue || p.NumberValue >= min.Value) &&
            (!max.HasValue || p.NumberValue <= max.Value)));
    }

    private IQueryable<Event> ApplyDateRangeFilter(IQueryable<Event> query, EventCustomPropertyProjectionFilter filter)
    {
        var (ns, key, from, to) = ((string, string, DateTimeOffset?, DateTimeOffset?))filter.Value;
        var visibleExposureLevels = CustomPropertyExposureScope.VisibleAtOrBelow(filter.ExposureCeiling);
        return query.Where(e => _dbContext.EventCustomPropertyProjections.Any(p =>
            p.EventId == e.Id &&
            p.Namespace == ns &&
            p.Key == key &&
            visibleExposureLevels.Contains(p.ExposureLevel) &&
            p.IsFilterable &&
            p.DateTimeValue != null &&
            (!from.HasValue || p.DateTimeValue >= from.Value) &&
            (!to.HasValue || p.DateTimeValue <= to.Value)));
    }

    private IQueryable<Event> ApplyTemporalFilter(IQueryable<Event> query, EventSubqueryFilter filter)
    {
        var (view, now) = ((TemporalView, DateTimeOffset))filter.Value;
        return ApplyTemporalFilter(query, view, now);
    }

    private IQueryable<Event> ApplyTemporalFilter(IQueryable<Event> query, TemporalView view, DateTimeOffset now) =>
        EventDirectoryTemporalQuery.Apply(_dbContext, query, view, now);

    public async Task<(List<Event> Items, int TotalCount)> GetMyEventsWithDetailsPaged(string userId, int pageNumber, int pageSize)
    {
        Guid userGuid;
        bool isGuid = Guid.TryParse(userId, out userGuid);

        var query = _dbContext.Events
            .AsNoTrackingWithIdentityResolution()
            .AsSplitQuery()
            .IncludeStandardDetails()
            .AsQueryable();

        if (isGuid)
        {
            var userActorIds = _dbContext.Actors
                .Where(actor => actor.UserId == userGuid)
                .Select(actor => actor.Id);

            var orgActorIds = _dbContext.OrganizationMembers
                .Where(member => member.UserId == userGuid
                    && member.OrganizationTenant.Organization.Actor != null)
                .Select(member => member.OrganizationTenant.Organization.Actor!.Id);

            var groupActorIds = _dbContext.GroupMembers
                .Where(member => member.UserId == userGuid
                    && member.GroupTenant.Group.Actor != null)
                .Select(member => member.GroupTenant.Group.Actor!.Id);

            var allActorIds = userActorIds.Union(orgActorIds).Union(groupActorIds);

            query = query.Where(e => allActorIds.Contains(e.ActorId));
        }

        var orderedQuery = query.OrderByDescending(e => e.FirstSessionDate);
        var totalCount = await orderedQuery.CountAsync();
        var items = await orderedQuery
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }
}
