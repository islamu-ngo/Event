namespace Explore.Persistence.Repositories;

using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;

public sealed class ConfigurationImportSessionRepository(
    ExploreDbContext dbContext)
    : IConfigurationImportSessionRepository
{
    public async Task AddAsync(
        ConfigurationImportSession session,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        await dbContext.Set<ConfigurationImportSession>()
            .AddAsync(session, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<ConfigurationImportSession?> GetForUpdateAsync(
        Guid sessionId,
        ConfigurationImportTarget target,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(sessionId, Guid.Empty);
        ArgumentNullException.ThrowIfNull(target);
        string authorityKey = target.GetSessionStorageKey();
        return AcquireAsync(sessionId, authorityKey, cancellationToken);
    }

    public async Task<IReadOnlyList<ConfigurationImportSession>>
        ListExpiredForUpdateAsync(
            DateTime occurredAt,
            int maximumCount,
            CancellationToken cancellationToken)
    {
        if (occurredAt.Kind != DateTimeKind.Utc)
            throw new ArgumentException("UTC timestamp required.", nameof(occurredAt));
        if (maximumCount is < 1 or > 1_000)
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        var candidates = await dbContext.Set<ConfigurationImportSession>().AsNoTracking()
            .Where(session =>
                session.ExpiresAt <= occurredAt
                && session.State != ConfigurationImportSessionState.Expired)
            .OrderBy(session => session.ExpiresAt)
            .ThenBy(session => session.SessionId)
            .Take(maximumCount)
            .Select(session => new { session.SessionId, session.TargetAuthorityKey })
            .ToListAsync(cancellationToken);
        List<ConfigurationImportSession> sessions = [];
        foreach (var candidate in candidates)
        {
            var session = await AcquireAsync(candidate.SessionId, candidate.TargetAuthorityKey, cancellationToken);
            if (session is not null && session.ExpiresAt <= occurredAt
                && session.State != ConfigurationImportSessionState.Expired)
                sessions.Add(session);
        }
        return sessions;
    }

    private async Task<ConfigurationImportSession?> AcquireAsync(
        Guid sessionId, string authorityKey, CancellationToken cancellationToken)
    {
        if (!await dbContext.Set<ConfigurationImportSession>().AsNoTracking().AnyAsync(
            session => session.SessionId == sessionId && session.TargetAuthorityKey == authorityKey,
            cancellationToken)) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ConfigurationImportSession>(
            dbContext, sessionId, cancellationToken);
        var session = await dbContext.Set<ConfigurationImportSession>().SingleOrDefaultAsync(
            item => item.SessionId == sessionId && item.TargetAuthorityKey == authorityKey, cancellationToken);
        if (session is null) return null;
        var entry = dbContext.Entry(session);
        if (entry.State != EntityState.Unchanged)
            throw new InvalidOperationException("Session authority cannot be refreshed over pending changes.");
        await entry.ReloadAsync(cancellationToken);
        return entry.State != EntityState.Detached && session.TargetAuthorityKey == authorityKey ? session : null;
    }

    public async Task UpdateAsync(
        ConfigurationImportSession session,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (dbContext.Entry(session).State == EntityState.Detached)
            dbContext.Update(session);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
