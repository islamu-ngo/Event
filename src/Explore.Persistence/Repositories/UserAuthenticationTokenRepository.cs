using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class UserAuthenticationTokenRepository : GenericRepository<UserAuthenticationToken, Guid>, IUserAuthenticationTokenRepository
{
    private readonly ExploreDbContext _dbContext;

    public UserAuthenticationTokenRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<UserAuthenticationToken?> GetAtprotoSessionForReadAsync(
        Guid tenantId,
        Guid userId,
        string provider,
        string subjectDid,
        CancellationToken cancellationToken = default) =>
        QueryAtprotoSession(tenantId, userId, provider, subjectDid)
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

    public Task<UserAuthenticationToken?> GetAtprotoSessionForUpdateAsync(
        Guid tenantId,
        Guid userId,
        string provider,
        string subjectDid,
        CancellationToken cancellationToken = default) =>
        QueryAtprotoSession(tenantId, userId, provider, subjectDid)
            .SingleOrDefaultAsync(cancellationToken);

    public Task DeleteAtprotoSessionAsync(
        Guid tenantId,
        Guid userId,
        string provider,
        string subjectDid,
        CancellationToken cancellationToken = default) =>
        QueryAtprotoSession(tenantId, userId, provider, subjectDid)
            .ExecuteDeleteAsync(cancellationToken);

    public async Task<UserAuthenticationToken> CreateAtprotoSessionAsync(
        UserAuthenticationToken session,
        CancellationToken cancellationToken = default)
    {
        await _dbContext.UserAuthenticationTokens.AddAsync(session, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return session;
    }

    public async Task UpdateAtprotoSessionAsync(
        UserAuthenticationToken session,
        CancellationToken cancellationToken = default)
    {
        if (_dbContext.Entry(session).State == EntityState.Detached)
        {
            throw new InvalidOperationException("ATProto OAuth session update requires a tracked entity.");
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<UserAuthenticationToken?> GetByUserAndProvider(
        Guid userId,
        string provider,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.UserAuthenticationTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.UserId == userId && t.Provider == provider, cancellationToken);
    }

    public async Task<List<UserAuthenticationToken>> GetByUser(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.UserAuthenticationTokens
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .ToListAsync(cancellationToken);
    }

    public async Task<UserAuthenticationToken?> GetByIdForUser(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.UserAuthenticationTokens
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, cancellationToken);
    }

    public async Task<UserAuthenticationToken?> GetUserAuthenticationTokenWithDetailsForUser(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.UserAuthenticationTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, cancellationToken);
    }

    public async Task<List<UserAuthenticationToken>> GetUserAuthenticationTokensWithDetailsForUser(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.UserAuthenticationTokens
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<UserAuthenticationToken>> GetAtprotoSessionsForReadAsync(
        Guid tenantId,
        Guid userId,
        string provider,
        CancellationToken cancellationToken = default) =>
        await _dbContext.UserAuthenticationTokens
            .AsNoTracking()
            .Where(token =>
                token.TenantId == tenantId
                && token.UserId == userId
                && token.Provider == provider)
            .OrderBy(token => token.SubjectDid)
            .Take(2)
            .ToListAsync(cancellationToken);

    private IQueryable<UserAuthenticationToken> QueryAtprotoSession(
        Guid tenantId,
        Guid userId,
        string provider,
        string subjectDid) =>
        _dbContext.UserAuthenticationTokens.Where(token =>
            token.TenantId == tenantId
            && token.UserId == userId
            && token.Provider == provider
            && token.SubjectDid == subjectDid);
}
