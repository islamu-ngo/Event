using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class EventLocationExactReadAuditRepository(ExploreDbContext dbContext)
    : IEventLocationExactReadAuditRepository
{
    public async Task<EventLocationExactReadAudit> AppendAsync(
        EventLocationExactReadAudit audit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audit);
        await AppendManyAsync([audit], cancellationToken);
        return audit;
    }

    public async Task AppendManyAsync(
        IReadOnlyCollection<EventLocationExactReadAudit> audits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audits);
        RequireTenant();
        if (audits.Count > IEventLocationExactReadAuditRepository.MaximumBatchSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(audits),
                $"Exact-read audit batches cannot exceed {IEventLocationExactReadAuditRepository.MaximumBatchSize} records.");
        }

        if (audits.Count == 0)
        {
            return;
        }

        foreach (EventLocationExactReadAudit audit in audits)
        {
            ArgumentNullException.ThrowIfNull(audit);
            RequireTenant(audit.TenantId);
        }

        Guid[] eventLocationIds = audits
            .Select(audit => audit.EventLocationId)
            .Distinct()
            .ToArray();
        Guid[] existingIds = await dbContext.EventLocations
            .AsNoTracking()
            .Where(item => eventLocationIds.Contains(item.Id))
            .Select(item => item.Id)
            .ToArrayAsync(cancellationToken);
        if (existingIds.Length != eventLocationIds.Length)
        {
            throw new InvalidOperationException("Exact-read evidence requires active EventLocations in the current tenant.");
        }

        dbContext.EventLocationExactReadAudits.AddRange(audits);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EventLocationExactReadAudit>> GetByEventLocationsAsync(
        IReadOnlyCollection<Guid> eventLocationIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(eventLocationIds);
        RequireTenant();
        Guid[] normalizedIds = eventLocationIds.Distinct().ToArray();
        if (normalizedIds.Length > IEventLocationExactReadAuditRepository.MaximumBatchSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(eventLocationIds),
                $"Exact-read audit batches cannot exceed {IEventLocationExactReadAuditRepository.MaximumBatchSize} unique ids.");
        }

        if (normalizedIds.Length == 0)
        {
            return [];
        }

        return await dbContext.EventLocationExactReadAudits
            .AsNoTracking()
            .Where(item => normalizedIds.Contains(item.EventLocationId))
            .OrderBy(item => item.OccurredAtUtc)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken);
    }

    private void RequireTenant(Guid? entityTenantId = null)
    {
        if (dbContext.IsTenantFilterBypassed)
        {
            return;
        }

        Guid tenantId = dbContext.TenantFilterTenantId
            ?? throw new InvalidOperationException("A tenant context is required for EventLocation privacy persistence.");
        if (entityTenantId.HasValue && entityTenantId.Value != tenantId)
        {
            throw new InvalidOperationException("EventLocation privacy evidence must belong to the current tenant.");
        }
    }
}
