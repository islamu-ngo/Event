using Explore.Application.Contracts.Persistence;
using Explore.Application.Authentication;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class UserExternalLoginRepository : GenericRepository<UserExternalLogin, Guid>,
    IUserExternalLoginRepository, Explore.Application.Contracts.PrivacyErasure.IPrivacyIdentityBindingReader
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

    public async Task<IReadOnlyList<UserExternalLogin>> ReadExternalBindingsAfterAsync(
        Guid? afterId, int limit, CancellationToken cancellationToken) =>
        await _dbContext.UserExternalLogins.AsNoTracking()
            .Where(login => login.AuthenticationProviderId != (int)Explore.Domain.Enums.AuthenticationProviderKind.Local
                && (!afterId.HasValue || login.Id.CompareTo(afterId.Value) > 0))
            .OrderBy(login => login.Id).Take(limit).ToListAsync(cancellationToken);

}
