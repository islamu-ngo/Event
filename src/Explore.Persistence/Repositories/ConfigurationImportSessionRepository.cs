namespace Explore.Persistence.Repositories;

using Explore.Application.Features.ConfigurationManifest.Importing;
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
        string authorityKey = target.AuthorityKey;
        return dbContext.Set<ConfigurationImportSession>()
            .SingleOrDefaultAsync(
                session => session.SessionId == sessionId
                    && session.TargetAuthorityKey == authorityKey,
                cancellationToken);
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
        return await dbContext.Set<ConfigurationImportSession>()
            .Where(session =>
                session.ExpiresAt <= occurredAt
                && session.State != ConfigurationImportSessionState.Expired)
            .OrderBy(session => session.ExpiresAt)
            .ThenBy(session => session.SessionId)
            .Take(maximumCount)
            .ToListAsync(cancellationToken);
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
