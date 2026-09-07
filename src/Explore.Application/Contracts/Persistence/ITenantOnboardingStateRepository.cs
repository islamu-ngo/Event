using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface ITenantOnboardingStateRepository : IGenericRepository<TenantOnboardingState, Guid>
{
    Task<TenantOnboardingState?> GetByTenantId(Guid tenantId);
}
