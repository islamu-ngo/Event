using Explore.Application.Models.Tenants;

namespace Explore.Application.Contracts.Services;

public interface ITenantLookupSource
{
    Task<IReadOnlyList<TenantLookupRecord>> GetTenantLookupsAsync(CancellationToken cancellationToken = default);
}
