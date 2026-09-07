namespace Explore.Persistence.Repositories;

using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

public class UserAppearancePreferenceRepository : GenericRepository<UserAppearancePreference, Guid>, IUserAppearancePreferenceRepository
{
    private readonly ExploreDbContext _dbContext;

    public UserAppearancePreferenceRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<UserAppearancePreference?> GetByUserAndTenantAsync(Guid userId, Guid? tenantId)
    {
        return await _dbContext.UserAppearancePreferences
            .AsNoTracking()
            .Include(p => p.ActiveProfile)
            .FirstOrDefaultAsync(p => p.UserId == userId && p.TenantId == tenantId);
    }

    public async Task<UserAppearancePreference> GetOrCreateAsync(Guid userId, Guid? tenantId, Guid fallbackProfileId)
    {
        var existing = await _dbContext.UserAppearancePreferences
            .Include(p => p.ActiveProfile)
            .FirstOrDefaultAsync(p => p.UserId == userId && p.TenantId == tenantId);

        if (existing is not null)
        {
            return existing;
        }

        var preference = new UserAppearancePreference
        {
            UserId = userId,
            TenantId = tenantId,
            ActiveProfileId = fallbackProfileId,
            ThemeMode = Domain.Enums.AppearanceThemeMode.System,
            Direction = "auto",
            Language = "en"
        };

        await _dbContext.UserAppearancePreferences.AddAsync(preference);
        await _dbContext.SaveChangesAsync();
        return preference;
    }
}
