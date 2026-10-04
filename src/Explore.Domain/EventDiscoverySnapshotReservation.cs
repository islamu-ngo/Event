using Explore.Domain.Interfaces;

namespace Explore.Domain;

/// <summary>
/// A tenant's native snapshot reservation fence, not public authority or mutable capacity metadata.
/// Kept independently of source rows so capture and purge never reverse the public writer lock order.
/// </summary>
public sealed class EventDiscoverySnapshotReservation : ITenantEntity
{
    public Guid TenantId { get; set; }
}
