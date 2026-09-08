namespace Explore.Persistence.Repositories;

using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

public class UserAppearanceProfileRepository : GenericRepository<UserAppearanceProfile, Guid>, IUserAppearanceProfileRepository
{
    private readonly ExploreDbContext _dbContext;

    public UserAppearanceProfileRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<UserAppearanceProfile>> GetProfilesForUserAsync(Guid userId, Guid? tenantId, bool includeArchived = false)
    {
        var query = _dbContext.UserAppearanceProfiles
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.TenantId == tenantId);

        if (!includeArchived)
        {
            query = query.Where(p => !p.IsArchived);
        }

        return await query
            .OrderByDescending(p => p.IsDefault)
            .ThenBy(p => p.Name)
            .ToListAsync();
    }

    public async Task<UserAppearanceProfile?> GetDefaultProfileAsync(Guid userId, Guid? tenantId)
    {
        return await _dbContext.UserAppearanceProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId && p.TenantId == tenantId && p.IsDefault && !p.IsArchived);
    }

    public async Task<UserAppearanceProfile?> GetExistingCloneAsync(Guid userId, Guid? tenantId, Guid sourcePresetId)
    {
        return await _dbContext.UserAppearanceProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p =>
                p.UserId == userId
                && p.TenantId == tenantId
                && p.SourcePresetId == sourcePresetId
                && !p.IsArchived);
    }

    public async Task ClearDefaultAsync(Guid userId, Guid? tenantId, Guid? excludingProfileId = null)
    {
        var defaults = await _dbContext.UserAppearanceProfiles
            .Where(p => p.UserId == userId && p.TenantId == tenantId && p.IsDefault)
            .Where(p => !excludingProfileId.HasValue || p.Id != excludingProfileId.Value)
            .ToListAsync();

        foreach (var profile in defaults)
        {
            profile.IsDefault = false;
        }

        await _dbContext.SaveChangesAsync();
    }
}
