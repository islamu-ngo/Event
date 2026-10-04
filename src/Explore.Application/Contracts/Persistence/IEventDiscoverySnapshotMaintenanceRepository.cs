using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IEventDiscoverySnapshotMaintenanceRepository
{
    /// <summary>Durable snapshot ownership survives deletion of public tenant authority.</summary>
    Task<IReadOnlyList<EventDiscoverySnapshotReservation>> GetExpiredOwnersAsync(
        Guid? afterTenantId, DateTime nowUtc, int take, CancellationToken cancellationToken);

    /// <summary>Trusted maintenance only; caller owns Serializable and the exact tenant is enforced throughout.</summary>
    Task<int> PurgeTenantBatchAsync(
        Guid tenantId, DateTime nowUtc, int take, CancellationToken cancellationToken);
}
