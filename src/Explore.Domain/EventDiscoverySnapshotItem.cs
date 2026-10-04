using Explore.Domain.Interfaces;

namespace Explore.Domain;

public enum EventDiscoveryCanonicalKind
{
    LocalEvent = 1,
    AtprotoRecord = 2,
    ReviewedIdentity = 3
}

/// <summary>Frozen discovery membership, not a copy of card data or attendee resource identity.</summary>
public sealed class EventDiscoverySnapshotItem : ITenantEntity
{
    private EventDiscoverySnapshotItem() { }

    public Guid TenantId { get; set; }
    public Guid SnapshotId { get; private set; }
    public long Ordinal { get; private set; }
    public EventDiscoverySourceKind SourceKind { get; private set; }
    public Guid SourceId { get; private set; }
    public EventDiscoveryCanonicalKind CanonicalKind { get; private set; }
    public Guid CanonicalId { get; private set; }
    public Guid? MatchingSessionId { get; private set; }

    public static EventDiscoverySnapshotItem Create(
        EventDiscoverySourceKind sourceKind, Guid sourceId,
        EventDiscoveryCanonicalKind canonicalKind, Guid canonicalId, Guid? matchingSessionId)
    {
        if (!Enum.IsDefined(sourceKind) || !Enum.IsDefined(canonicalKind) ||
            sourceId == Guid.Empty || canonicalId == Guid.Empty || matchingSessionId == Guid.Empty)
            throw new ArgumentException("Snapshot membership requires supported namespaces and nonempty identifiers.");
        return new EventDiscoverySnapshotItem
        {
            SourceKind = sourceKind,
            SourceId = sourceId,
            CanonicalKind = canonicalKind,
            CanonicalId = canonicalId,
            MatchingSessionId = matchingSessionId
        };
    }

    internal EventDiscoverySnapshotItem Bind(Guid tenantId, Guid snapshotId, long ordinal) => new()
    {
        TenantId = tenantId,
        SnapshotId = snapshotId,
        Ordinal = ordinal,
        SourceKind = SourceKind,
        SourceId = SourceId,
        CanonicalKind = CanonicalKind,
        CanonicalId = CanonicalId,
        MatchingSessionId = MatchingSessionId
    };
}
