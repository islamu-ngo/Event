using Explore.Application.Authentication;
using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IUserExternalLoginRepository : IGenericRepository<UserExternalLogin, Guid>
{
    Task<UserExternalLogin?> GetByProviderAndKey(ProviderAccountKey accountKey);
    Task<List<UserExternalLogin>> GetByUser(Guid userId);
}
