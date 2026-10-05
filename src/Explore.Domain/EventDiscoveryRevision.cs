using Explore.Domain.Interfaces;

namespace Explore.Domain;

/// <summary>Tenant-wide relationship and disclosure epochs shared by discovery traversal.</summary>
public sealed class EventDiscoveryRevision : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public long IdentityEpoch { get; set; }
    public long DisclosureEpoch { get; set; }

    public void AdvanceDisclosure() => DisclosureEpoch = checked(DisclosureEpoch + 1);
}
