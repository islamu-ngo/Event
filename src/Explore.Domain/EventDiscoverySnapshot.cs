using Explore.Domain.Interfaces;
using Explore.Domain.Services.Discovery;

namespace Explore.Domain;

/// <summary>
/// Short-lived public membership. No viewer, card text, or location is retained.
/// Epoch and current disclosure checks remain the traversal caller's responsibility.
/// </summary>
public sealed class EventDiscoverySnapshot : ITenantEntity
{
    private readonly List<EventDiscoverySnapshotItem> _items = [];
    private EventDiscoverySnapshot() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public string CriteriaHash { get; private set; } = string.Empty;
    public long IdentityEpoch { get; private set; }
    public long DisclosureEpoch { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public int ItemCount { get; private set; }
    public bool Truncated { get; private set; }
    public bool LocalSourceComplete { get; private set; }
    public bool RemoteSourceComplete { get; private set; }
    public IReadOnlyList<EventDiscoverySnapshotItem> Items => _items.AsReadOnly();

    /// <summary>The caller reserves the ID and timestamps before entering a retried transaction delegate.</summary>
    public static EventDiscoverySnapshot Create(
        Guid id, Guid tenantId, string criteriaHash, long identityEpoch, long disclosureEpoch,
        DateTime createdAtUtc, DateTime expiresAtUtc, bool truncated,
        bool localSourceComplete, bool remoteSourceComplete,
        IEnumerable<EventDiscoverySnapshotItem> items, EventDiscoveryTraversalLimits limits)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(limits);
        if (id == Guid.Empty)
            throw new ArgumentException("A snapshot requires its preallocated identifier.", nameof(id));
        if (tenantId == Guid.Empty)
            throw new ArgumentException("A snapshot requires its tenant.", nameof(tenantId));
        if (criteriaHash is null || criteriaHash.Length != 64 ||
            criteriaHash.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new ArgumentException("Criteria must be represented by a lowercase SHA-256 digest.", nameof(criteriaHash));
        ArgumentOutOfRangeException.ThrowIfNegative(identityEpoch);
        ArgumentOutOfRangeException.ThrowIfNegative(disclosureEpoch);
        if (createdAtUtc.Kind != DateTimeKind.Utc || expiresAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Snapshot timestamps must be UTC.");
        // PostgreSQL and MySQL-family timestamps retain microseconds, not every
        // .NET tick. Authenticate the same conservative expiry on every engine.
        createdAtUtc = createdAtUtc.AddTicks(-(createdAtUtc.Ticks % 10));
        expiresAtUtc = expiresAtUtc.AddTicks(-(expiresAtUtc.Ticks % 10));
        if (expiresAtUtc <= createdAtUtc || expiresAtUtc - createdAtUtc > limits.Lifetime)
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc));
        var snapshot = new EventDiscoverySnapshot
        {
            Id = id, TenantId = tenantId, CriteriaHash = criteriaHash,
            IdentityEpoch = identityEpoch, DisclosureEpoch = disclosureEpoch,
            CreatedAtUtc = createdAtUtc, ExpiresAtUtc = expiresAtUtc, Truncated = truncated,
            LocalSourceComplete = localSourceComplete, RemoteSourceComplete = remoteSourceComplete
        };
        var keys = new HashSet<(EventDiscoveryCanonicalKind, Guid)>();
        foreach (var item in items)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (snapshot._items.Count == limits.MaxIdentities)
                throw new ArgumentException("Snapshot membership exceeds its finite budget.", nameof(items));
            if (!keys.Add((item.CanonicalKind, item.CanonicalId)))
                throw new ArgumentException("Snapshot canonical membership must be unique.", nameof(items));
            snapshot._items.Add(item.Bind(tenantId, snapshot.Id, snapshot._items.Count));
        }
        snapshot.ItemCount = snapshot._items.Count;
        return snapshot;
    }
}
