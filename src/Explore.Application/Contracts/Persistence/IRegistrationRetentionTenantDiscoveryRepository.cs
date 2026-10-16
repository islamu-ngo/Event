namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Enumerates only tenant identities eligible under the existing active-state
/// retention policy. Discovery grants no authority to read or delete their data.
/// </summary>
public interface IRegistrationRetentionTenantDiscoveryRepository
{
    const int PageSize = 128;

    Task<IReadOnlyList<Guid>> GetTenantIdsAsync(
        Guid? afterTenantId, int limit, CancellationToken cancellationToken);
}
