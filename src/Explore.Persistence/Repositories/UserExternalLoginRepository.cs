using Explore.Application.Contracts.Persistence;
using Explore.Application.Authentication;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class UserExternalLoginRepository : GenericRepository<UserExternalLogin, Guid>, IUserExternalLoginRepository
{
    private readonly ExploreDbContext _dbContext;

    public UserExternalLoginRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<UserExternalLogin?> GetByProviderAndKey(
        ProviderAccountKey accountKey)
    {
        return await _dbContext.UserExternalLogins
            .AsNoTracking()
            .FirstOrDefaultAsync(login =>
                login.AuthenticationProviderId == (int)accountKey.ProviderKind
                && login.ProviderKey == accountKey.Value);
    }

    public async Task<List<UserExternalLogin>> GetByUser(Guid userId)
    {
        return await _dbContext.UserExternalLogins
            .AsNoTracking()
            .Where(l => l.UserId == userId)
            .ToListAsync();
    }

}
