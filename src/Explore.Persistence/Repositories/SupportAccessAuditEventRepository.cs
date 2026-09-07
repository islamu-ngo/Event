using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class SupportAccessAuditEventRepository : ISupportAccessAuditEventRepository
{
    private const int MaxListLimit = 1000;
    private readonly ExploreDbContext _dbContext;

    public SupportAccessAuditEventRepository(ExploreDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<SupportAccessAuditEvent> CreateAsync(
        SupportAccessAuditEvent auditEvent,
        CancellationToken cancellationToken = default)
    {
        await _dbContext.SupportAccessAuditEvents.AddAsync(auditEvent, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return auditEvent;
    }

    public async Task<IReadOnlyList<SupportAccessAuditEvent>> ListForSessionAsync(
        Guid sessionId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        int boundedLimit = Math.Clamp(limit, 1, MaxListLimit);

        return await _dbContext.SupportAccessAuditEvents
            .AsNoTracking()
            .Where(auditEvent => auditEvent.SupportAccessSessionId == sessionId)
            .OrderByDescending(auditEvent => auditEvent.OccurredAtUtc)
            .Take(boundedLimit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SupportAccessAuditEvent>> ListForTargetTenantAsync(
        Guid targetTenantId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        int boundedLimit = Math.Clamp(limit, 1, MaxListLimit);

        return await _dbContext.SupportAccessAuditEvents
            .AsNoTracking()
            .Where(auditEvent => auditEvent.TargetTenantId == targetTenantId)
            .OrderByDescending(auditEvent => auditEvent.OccurredAtUtc)
            .Take(boundedLimit)
            .ToListAsync(cancellationToken);
    }
}
