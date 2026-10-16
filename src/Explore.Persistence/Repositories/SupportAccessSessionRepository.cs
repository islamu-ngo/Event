using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class SupportAccessSessionRepository : ISupportAccessSessionRepository
{
    private const int MaxListLimit = 500;
    private readonly ExploreDbContext _dbContext;

    public SupportAccessSessionRepository(ExploreDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<SupportAccessSession> CreateAsync(SupportAccessSession session, CancellationToken cancellationToken = default)
    {
        await _dbContext.SupportAccessSessions.AddAsync(session, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return session;
    }

    public async Task UpdateAsync(SupportAccessSession session, CancellationToken cancellationToken = default)
    {
        _dbContext.Entry(session).State = EntityState.Modified;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<SupportAccessSession?> GetByIdAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        return _dbContext.SupportAccessSessions
            .FirstOrDefaultAsync(session => session.Id == sessionId, cancellationToken);
    }

    public Task<SupportAccessSession?> GetActiveForActorAsync(
        Guid actorUserId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        return SupportAccessTemporalQueries.GetActiveForActorAsync(_dbContext, actorUserId, nowUtc, cancellationToken);
    }

    public async Task<SupportAccessSession?> GetActiveOwnedSessionAsync(
        Guid sessionId,
        Guid actorUserId,
        Guid? targetTenantId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        return await SupportAccessTemporalQueries.GetActiveOwnedSessionAsync(
            _dbContext, sessionId, actorUserId, targetTenantId, nowUtc, cancellationToken);
    }

    public Task<SupportAccessSession?> GetOwnedSessionAsync(
        Guid sessionId,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.SupportAccessSessions
            .Where(session => session.Id == sessionId)
            .Where(session => session.ActorUserId == actorUserId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> HasActiveSessionForActorAsync(Guid actorUserId, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        return await SupportAccessTemporalQueries.HasActiveSessionForActorAsync(
            _dbContext, actorUserId, nowUtc, cancellationToken);
    }

    public async Task<IReadOnlyList<SupportAccessSession>> ListForTargetTenantAsync(
        Guid targetTenantId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        int boundedLimit = Math.Clamp(limit, 1, MaxListLimit);

        return await SupportAccessTemporalQueries.ListSessionsForTargetTenantAsync(
            _dbContext, targetTenantId, boundedLimit, cancellationToken);
    }
}
