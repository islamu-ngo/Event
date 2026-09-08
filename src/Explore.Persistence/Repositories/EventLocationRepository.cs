using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Explore.Persistence.Repositories;

public sealed class EventLocationRepository(ExploreDbContext dbContext) : IEventLocationRepository
{
    public async Task<EventLocation> AddAsync(
        EventLocation eventLocation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(eventLocation);
        RequireTenant(eventLocation.TenantId);
        EventLocationDisclosureAudit initialAudit = eventLocation.CreateInitialDisclosureAudit();
        dbContext.EventLocations.Add(eventLocation);
        dbContext.EventLocationDisclosureAudits.Add(initialAudit);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return eventLocation;
        }
        catch (DbUpdateException exception) when (IsActivePairUniquenessViolation(exception))
        {
            dbContext.Entry(initialAudit).State = EntityState.Detached;
            dbContext.Entry(eventLocation).State = EntityState.Detached;
            return eventLocation.IsToBeAnnounced
                ? await FindActiveToBeAnnouncedAsync(eventLocation.EventId, cancellationToken)
                    ?? throw new InvalidOperationException("The winning TBA EventLocation was not visible after a uniqueness race.", exception)
                : await FindActivePhysicalAsync(
                    eventLocation.EventId,
                    eventLocation.LocationId!.Value,
                    cancellationToken)
                    ?? throw new InvalidOperationException("The winning physical EventLocation was not visible after a uniqueness race.", exception);
        }
    }

    public Task<EventLocation?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        RequireId(id, nameof(id));
        RequireTenant();
        return dbContext.EventLocations
            .Include(item => item.Location)
            .Include(item => item.FullDetailsAudience)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<EventLocation>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        RequireTenant();
        Guid[] normalizedIds = ids.Distinct().ToArray();
        if (normalizedIds.Length > IEventLocationRepository.MaximumBatchSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ids),
                $"EventLocation batches cannot exceed {IEventLocationRepository.MaximumBatchSize} unique ids.");
        }

        if (normalizedIds.Length == 0)
        {
            return [];
        }

        return await dbContext.EventLocations
            .AsNoTracking()
            .Include(item => item.Location)
            .Include(item => item.FullDetailsAudience)
            .Where(item => normalizedIds.Contains(item.Id))
            .OrderBy(item => item.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EventLocation>> GetByEventIdAsync(
        Guid eventId,
        CancellationToken cancellationToken)
    {
        RequireId(eventId, nameof(eventId));
        RequireTenant();
        return await dbContext.EventLocations
            .AsNoTracking()
            .Include(item => item.Location)
            .Include(item => item.FullDetailsAudience)
            .Where(item => item.EventId == eventId)
            .OrderBy(item => item.Id)
            .ToListAsync(cancellationToken);
    }

    public Task<EventLocation?> FindActivePhysicalAsync(
        Guid eventId,
        Guid locationId,
        CancellationToken cancellationToken)
    {
        RequireId(eventId, nameof(eventId));
        RequireId(locationId, nameof(locationId));
        RequireTenant();
        return dbContext.EventLocations
            .AsNoTracking()
            .Include(item => item.Location)
            .Include(item => item.FullDetailsAudience)
            .SingleOrDefaultAsync(
                item => item.EventId == eventId
                    && item.LocationId == locationId
                    && !item.IsToBeAnnounced,
                cancellationToken);
    }

    public Task<EventLocation?> FindActiveToBeAnnouncedAsync(
        Guid eventId,
        CancellationToken cancellationToken)
    {
        RequireId(eventId, nameof(eventId));
        RequireTenant();
        return dbContext.EventLocations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.EventId == eventId && item.IsToBeAnnounced,
                cancellationToken);
    }

    public async Task<bool> HasActiveCarrierReferencesAsync(
        Guid eventLocationId,
        CancellationToken cancellationToken)
    {
        RequireId(eventLocationId, nameof(eventLocationId));
        RequireTenant();
        return await dbContext.EventSessions.AnyAsync(
                item => item.EventLocationId == eventLocationId,
                cancellationToken)
            || await dbContext.EventSessionGroups.AnyAsync(
                item => item.EventLocationId == eventLocationId,
                cancellationToken)
            || await dbContext.EventAgendaItems.AnyAsync(
                item => item.EventLocationId == eventLocationId,
                cancellationToken)
            || await dbContext.EventSessionAgendaItems.AnyAsync(
                item => item.EventLocationId == eventLocationId,
                cancellationToken);
    }

    public async Task<IReadOnlyList<EventLocation>> GetActiveForGovernanceUpdateAsync(
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A non-empty tenant id is required when tenant scope is selected.", nameof(tenantId));
        }

        IQueryable<EventLocation> query = dbContext.EventLocations
            .IgnoreTenantFilter(tenantId.HasValue
                ? TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate
                : TenantFilterBypassReasons.InstanceLocationPrivacyGovernance)
            .Include(item => item.Location);

        if (tenantId.HasValue)
        {
            query = query.Where(item => item.TenantId == tenantId.Value);
        }

        return await query
            .OrderBy(item => item.TenantId)
            .ThenBy(item => item.EventId)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken);
    }

    public Task<int> CountNeedingPrivacyReviewAsync(CancellationToken cancellationToken) =>
        dbContext.EventLocations
            .IgnoreTenantFilter(TenantFilterBypassReasons.InstanceLocationPrivacyReviewQueueDepth)
            .CountAsync(item => item.NeedsPrivacyReview, cancellationToken);

    public async Task SaveGovernanceChangesAsync(
        IReadOnlyCollection<EventLocationDisclosureAudit> audits,
        IReadOnlyCollection<OutboxMessage> outboxMessages,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audits);
        ArgumentNullException.ThrowIfNull(outboxMessages);
        dbContext.EventLocationDisclosureAudits.AddRange(audits);
        dbContext.OutboxMessages.AddRange(outboxMessages);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);

    private static void RequireId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty id is required.", parameterName);
        }
    }

    private bool IsActivePairUniquenessViolation(DbUpdateException exception)
    {
        if (exception.InnerException is not PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: { } constraintName
            })
        {
            return false;
        }

        string activePhysical = RelationalConstraintDescriptorResolver.UniqueIndex<EventLocation>(
            dbContext,
            nameof(EventLocation.TenantId),
            nameof(EventLocation.EventId),
            nameof(EventLocation.LocationId)).Name;
        string activeToBeAnnounced = RelationalConstraintDescriptorResolver.UniqueIndex<EventLocation>(
            dbContext,
            nameof(EventLocation.TenantId),
            nameof(EventLocation.EventId)).Name;
        return constraintName == activePhysical || constraintName == activeToBeAnnounced;
    }

    private void RequireTenant(Guid? entityTenantId = null)
    {
        if (dbContext.IsTenantFilterBypassed)
        {
            return;
        }

        Guid tenantId = dbContext.TenantFilterTenantId
            ?? throw new InvalidOperationException("A tenant context is required for EventLocation persistence.");
        if (entityTenantId.HasValue && entityTenantId.Value != tenantId)
        {
            throw new InvalidOperationException("EventLocation must belong to the current tenant.");
        }
    }
}
