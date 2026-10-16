using Explore.Application.Models.Tenants;

namespace Explore.Application.Contracts.Services;

public interface ITenantLookupSource
{
    /// <summary>Reads current Active tenant routing identities, without publishing private tenant state.</summary>
    Task<IReadOnlyList<TenantLookupRecord>> GetTenantLookupsAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads one unambiguous, currently active reservation without a positive routing cache.</summary>
    Task<Guid?> GetCurrentDomainTenantAsync(string domain, bool customDomain, CancellationToken cancellationToken = default);
}
