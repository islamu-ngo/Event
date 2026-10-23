namespace Explore.Application.Contracts.Persistence;

/// <summary>Enumerates bounded current tenant owners without exposing private audit rows.</summary>
public interface IEventResourceAuditOwnerDiscoveryRepository
{
    Task<IReadOnlyList<Guid>> GetTenantIdsWithAuditAsync(
        Guid? afterTenantId, int limit, CancellationToken cancellationToken);
}
