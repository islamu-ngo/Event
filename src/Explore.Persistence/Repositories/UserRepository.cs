using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class UserRepository : GenericRepository<User, Guid>, IUserRepository
{
    private readonly ExploreDbContext _dbContext;

    public UserRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public new async Task<User?> GetById(Guid id)
    {
        return await _dbContext.Users
            .Include(u => u.Pii)
            .FirstOrDefaultAsync(u => u.Id == id);
    }

    public Task<User?> GetUserWithDetails(Guid id) =>
        GetUserWithDetails(id, CancellationToken.None);

    public async Task SaveIdentityProfileAsync(User user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        cancellationToken.ThrowIfCancellationRequested();
        _dbContext.RequireBoundTenantLease();
        if (_dbContext.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Identity profile synchronization requires its original account transaction.");
        _dbContext.ChangeTracker.DetectChanges();
        var entry = _dbContext.Entry(user);
        if (entry.State is not (EntityState.Unchanged or EntityState.Modified)
            || entry.Property(row => row.Id).OriginalValue != user.Id
            || user.Pii is null)
            throw new InvalidOperationException("Identity profile synchronization requires its existing tracked user.");
        var profile = _dbContext.Entry(user.Pii);
        if (profile.State is not (EntityState.Unchanged or EntityState.Modified)
            || profile.Property(row => row.UserId).OriginalValue != user.Id || user.Pii.UserId != user.Id
            || entry.Properties.Any(property => property.IsModified && property.Metadata.Name != nameof(User.EmailVerified))
            || profile.Properties.Any(property => property.IsModified && property.Metadata.Name != nameof(UserPii.Email)))
            throw new InvalidOperationException("Identity profile synchronization may change only its email mirror.");
        if (profile.Property(row => row.Email).IsModified && entry.State == EntityState.Unchanged)
            entry.Property(row => row.ConcurrencyStamp).IsModified = true;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<User?> GetUserWithDetails(Guid id, CancellationToken cancellationToken)
    {
        return await _dbContext.Users
            .AsNoTracking()
            .Include(u => u.Pii)
            .Include(u => u.IdentityEmailClaims).ThenInclude(claim => claim.Evidence)
            .Include(u => u.Actor)
                .ThenInclude(a => a!.Pii)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
    }

    public async Task<User?> GetUserByEmail(string email)
    {
        return await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Pii != null && u.Pii.Email == email);
    }

    public async Task<IReadOnlyList<User>> GetUsersByNormalizedEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        string normalizedEmail = email.Trim().ToLowerInvariant();
        return await _dbContext.Users
            .AsNoTracking()
            .Where(user => user.Pii != null && user.Pii.Email.ToLower() == normalizedEmail)
            .Take(2)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<User>> GetUsersByIdsAsync(List<Guid> ids)
    {
        if (ids.Count == 0)
            return [];

        var results = new List<User>(ids.Count);
        foreach (var chunk in ids.Chunk(100))
        {
            var chunkResults = await _dbContext.Users
                .AsNoTracking()
                .Include(u => u.Pii)
                .Include(u => u.IdentityEmailClaims).ThenInclude(claim => claim.Evidence)
                .Where(u => chunk.Contains(u.Id))
                .ToListAsync();
            results.AddRange(chunkResults);
        }
        return results;
    }

    public async Task<bool> ExistsByEmail(string email)
    {
        return await _dbContext.Users
            .AsNoTracking()
            .AnyAsync(u => u.Pii != null && u.Pii.Email == email);
    }

    public async Task<int> ForgetPiiAsync(Guid userId)
    {
        return await _dbContext.ExecuteDisclosureMutationAsync(async token =>
        {
            await _dbContext.DisclosureMutations.EnlistUserAsync(userId, token);
            return await _dbContext.UserPii.Where(p => p.UserId == userId).ExecuteDeleteAsync(token);
        }, CancellationToken.None);
    }
}
