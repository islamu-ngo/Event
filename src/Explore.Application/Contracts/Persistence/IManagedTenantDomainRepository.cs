using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Finds an exact reserved domain host independently of request tenant authority.</summary>
public interface IManagedTenantDomainRepository
{
    Task<TenantSetting?> GetByDomainHostAsync(string normalizedHost, CancellationToken cancellationToken = default);
}
