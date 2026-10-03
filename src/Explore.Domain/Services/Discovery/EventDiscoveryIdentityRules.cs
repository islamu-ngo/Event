namespace Explore.Domain.Services.Discovery;

public static class EventDiscoveryIdentityRules
{
    public static void Review(
        EventDiscoveryRevision revision, long expectedRevision,
        IReadOnlyCollection<EventDiscoveryIdentity> graph,
        Guid memberId, Guid primaryId, Guid reviewerId, string reasonCode, DateTime reviewedAtUtc)
    {
        var identities = ValidateDecision(revision, expectedRevision, graph, memberId, primaryId,
            reviewerId, reasonCode, reviewedAtUtc);
        var member = identities[memberId];
        var primary = identities[primaryId];
        var memberRoot = member.Alias?.PrimaryIdentityId ?? member.Id;
        var primaryRoot = primary.Alias?.PrimaryIdentityId ?? primary.Id;
        if (memberRoot == primaryRoot && primary.Alias is null)
            throw new InvalidOperationException("discovery_relationship_unchanged");
        var nextRevision = checked(revision.IdentityEpoch + 1);
        var affected = graph.Where(identity =>
            (identity.Alias?.PrimaryIdentityId ?? identity.Id) == memberRoot ||
            (identity.Alias?.PrimaryIdentityId ?? identity.Id) == primaryRoot).ToArray();

        // All validation and checked arithmetic precede the first mutation.
        foreach (var identity in affected)
        {
            if (identity.Id == primaryId)
            {
                identity.Alias = null;
                continue;
            }
            if (identity.Alias?.PrimaryIdentityId == primaryId)
                continue;
            var alias = identity.Alias ?? new EventDiscoveryAlias
            {
                Id = Guid.CreateVersion7(), TenantId = revision.TenantId,
                MemberIdentityId = identity.Id, Member = identity,
                CreatedAt = reviewedAtUtc, CreatedBy = reviewerId
            };
            alias.PrimaryIdentityId = primaryId;
            alias.Primary = primary;
            alias.RelationshipRevision = nextRevision;
            alias.ReviewerId = reviewerId;
            alias.ReasonCode = reasonCode;
            alias.ReviewedAtUtc = reviewedAtUtc;
            alias.UpdatedAt = reviewedAtUtc;
            alias.UpdatedBy = reviewerId;
            identity.Alias = alias;
        }
        revision.IdentityEpoch = nextRevision;
    }

    public static void Reverse(
        EventDiscoveryRevision revision, long expectedRevision,
        IReadOnlyCollection<EventDiscoveryIdentity> graph,
        Guid memberId, Guid primaryId, Guid reviewerId, string reasonCode, DateTime reviewedAtUtc)
    {
        var identities = ValidateDecision(revision, expectedRevision, graph, memberId, primaryId,
            reviewerId, reasonCode, reviewedAtUtc);
        if (identities[memberId].Alias?.PrimaryIdentityId != primaryId || identities[primaryId].Alias is not null)
            throw new InvalidOperationException("discovery_relationship_changed");
        var nextRevision = checked(revision.IdentityEpoch + 1);
        identities[memberId].Alias = null;
        revision.IdentityEpoch = nextRevision;
    }

    /// <summary>
    /// Eligibility comes from the existing public source authority, including local binding and remote
    /// tombstone suppression. This method selects one whole member, never fields from its private primary.
    /// </summary>
    public static EventDiscoveryIdentity? SelectRepresentation(
        IReadOnlyCollection<EventDiscoveryIdentity> group,
        IReadOnlySet<Guid> publiclyEligibleIdentityIds)
    {
        if (!IsDirectGroup(group))
            return null;
        return group.Where(identity => !identity.IsDeleted && publiclyEligibleIdentityIds.Contains(identity.Id))
            .OrderBy(identity => identity.Alias is not null)
            .ThenBy(identity => identity.SourceKind)
            .ThenBy(identity => identity.SourceKey, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>A guidance target needs its own public authority, not the member's card eligibility.</summary>
    public static EventDiscoveryIdentity? SelectPublicPrimary(
        IReadOnlyCollection<EventDiscoveryIdentity> group,
        IReadOnlySet<Guid> independentlyPublicIdentityIds) =>
        IsDirectGroup(group)
            ? group.SingleOrDefault(identity => identity.Alias is null && !identity.IsDeleted &&
                independentlyPublicIdentityIds.Contains(identity.Id))
            : null;

    private static Dictionary<Guid, EventDiscoveryIdentity> ValidateDecision(
        EventDiscoveryRevision revision, long expectedRevision,
        IReadOnlyCollection<EventDiscoveryIdentity> graph,
        Guid memberId, Guid primaryId, Guid reviewerId, string reasonCode, DateTime reviewedAtUtc)
    {
        if (revision.TenantId == Guid.Empty || revision.IdentityEpoch < 0 || revision.DisclosureEpoch < 0 ||
            expectedRevision != revision.IdentityEpoch)
            throw new InvalidOperationException("discovery_revision_conflict");
        if (memberId == Guid.Empty || primaryId == Guid.Empty || memberId == primaryId)
            throw new InvalidOperationException("discovery_relationship_invalid");
        if (reviewerId == Guid.Empty || reviewedAtUtc == default || reviewedAtUtc.Kind != DateTimeKind.Utc ||
            string.IsNullOrEmpty(reasonCode) || reasonCode.Length > 80 ||
            reasonCode.Any(character => !(character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_')))
            throw new InvalidOperationException("discovery_decision_invalid");
        if (graph.Any(identity => identity.TenantId != revision.TenantId || identity.Id == Guid.Empty ||
                identity.IsDeleted) || graph.Select(identity => identity.Id).Distinct().Count() != graph.Count ||
            graph.Select(identity => (identity.SourceKind, identity.SourceKey)).Distinct().Count() != graph.Count)
            throw new InvalidOperationException("discovery_graph_invalid");
        var identities = graph.ToDictionary(identity => identity.Id);
        if (!identities.ContainsKey(memberId) || !identities.ContainsKey(primaryId))
            throw new InvalidOperationException("discovery_identity_unavailable");
        foreach (var identity in graph)
        {
            if (identity.Alias is not { } alias)
                continue;
            if (alias.TenantId != revision.TenantId || alias.MemberIdentityId != identity.Id ||
                alias.PrimaryIdentityId == identity.Id || alias.RelationshipRevision <= 0 ||
                alias.RelationshipRevision > revision.IdentityEpoch ||
                !identities.TryGetValue(alias.PrimaryIdentityId, out var primary) || primary.Alias is not null)
                throw new InvalidOperationException("discovery_graph_invalid");
        }
        return identities;
    }

    private static bool IsDirectGroup(IReadOnlyCollection<EventDiscoveryIdentity> group)
    {
        if (group.Count == 0 || group.Select(identity => identity.TenantId).Distinct().Count() != 1 ||
            group.Any(identity => identity.TenantId == Guid.Empty || identity.Id == Guid.Empty) ||
            group.Select(identity => identity.Id).Distinct().Count() != group.Count)
            return false;
        var roots = group.Select(identity => identity.Alias?.PrimaryIdentityId ?? identity.Id).Distinct().ToArray();
        return roots.Length == 1 && roots[0] != Guid.Empty &&
            group.All(identity => identity.Alias is not { } alias ||
            alias.TenantId == identity.TenantId && alias.MemberIdentityId == identity.Id &&
            alias.PrimaryIdentityId != identity.Id && alias.RelationshipRevision > 0);
    }
}
