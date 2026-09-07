namespace Explore.Application.Contracts.Persistence;

using Explore.Domain;

public interface IUserAppearancePreferenceRepository : IGenericRepository<UserAppearancePreference, Guid>
{
    Task<UserAppearancePreference?> GetByUserAndTenantAsync(Guid userId, Guid? tenantId);
    Task<UserAppearancePreference> GetOrCreateAsync(Guid userId, Guid? tenantId, Guid fallbackProfileId);
}
