using System.Collections.Frozen;

namespace ISLAMU.Moderation.Domain;

/// <summary>
/// A current native source fact supplied after positive qualification by the publication writer.
/// For consumers, the source is the conformance-passing registration; for prerequisites it is
/// the adopted prerequisite owner. Domain validates identity and binding, not native authority.
/// </summary>
public sealed record ModerationPolicyNativeEvidence
{
    public ModerationPolicyNativeEvidence(
        string code, Guid sourceId, long revision, AuthorityScope jurisdiction, DateTimeOffset verifiedAt)
    {
        ArgumentNullException.ThrowIfNull(jurisdiction);
        ModerationPolicy.RequireVersion7(sourceId, nameof(sourceId));
        if (string.IsNullOrWhiteSpace(code) || code.Length > 100 || revision < 1)
        {
            throw new ArgumentException("Native evidence requires a bounded code and a positive source revision.");
        }

        Code = code;
        SourceId = sourceId;
        Revision = revision;
        Jurisdiction = jurisdiction;
        VerifiedAt = verifiedAt.ToUniversalTime();
    }

    public string Code { get; }
    public Guid SourceId { get; }
    public long Revision { get; }
    public AuthorityScope Jurisdiction { get; }
    public DateTimeOffset VerifiedAt { get; }
}

/// <summary>
/// A native writer's revision-bound evidence for one rule effect. The writer must derive required sets
/// from its authoritative inventory, positively prove prerequisites and terminal conformance, and
/// retain their current source fences through publication. Domain checks the supplied structure,
/// not the truth or ongoing freshness of native facts. Receipt identity alone grants nothing.
/// </summary>
public sealed class ModerationPolicyEffectEvidence
{
    public ModerationPolicyEffectEvidence(
        Guid policyId, long policyRevision, AuthorityScope jurisdiction, ModerationRuleReference rule,
        ModerationCapability capability, Guid publicationId, DateTimeOffset verifiedAt,
        IEnumerable<string> requiredPrerequisites, IEnumerable<ModerationPolicyNativeEvidence> satisfiedPrerequisites,
        IEnumerable<string> requiredConsumers, IEnumerable<ModerationPolicyNativeEvidence> registeredConsumers)
    {
        ModerationPolicy.RequireVersion7(policyId, nameof(policyId));
        ModerationPolicy.RequireVersion7(publicationId, nameof(publicationId));
        ArgumentNullException.ThrowIfNull(jurisdiction);
        ArgumentNullException.ThrowIfNull(rule);
        if (policyRevision < 1 || rule.OwnerId != jurisdiction.Id
            || !Enum.IsDefined(capability) || EffectiveRestrictions.IsProtectedRemedy(capability))
        {
            throw new ArgumentException("Effect evidence requires an exact owner, revision and restrictive capability.");
        }

        RequiredPrerequisites = Snapshot(requiredPrerequisites);
        SatisfiedPrerequisites = Facts(satisfiedPrerequisites, jurisdiction, verifiedAt);
        RequiredConsumers = Snapshot(requiredConsumers);
        RegisteredConsumers = Facts(registeredConsumers, jurisdiction, verifiedAt);
        if (!RequiredPrerequisites.SetEquals(SatisfiedPrerequisites.Select(fact => fact.Code))
            || !RequiredConsumers.SetEquals(RegisteredConsumers.Select(fact => fact.Code)))
        {
            throw new ArgumentException("Every required prerequisite and terminal consumer must be positively proved.");
        }

        PolicyId = policyId;
        PolicyRevision = policyRevision;
        Jurisdiction = jurisdiction;
        Rule = rule;
        Capability = capability;
        PublicationId = publicationId;
        VerifiedAt = verifiedAt.ToUniversalTime();
    }

    public Guid PolicyId { get; }
    public long PolicyRevision { get; }
    public AuthorityScope Jurisdiction { get; }
    public ModerationRuleReference Rule { get; }
    public ModerationCapability Capability { get; }
    public Guid PublicationId { get; }
    public DateTimeOffset VerifiedAt { get; }
    public IReadOnlySet<string> RequiredPrerequisites { get; }
    public IReadOnlyList<ModerationPolicyNativeEvidence> SatisfiedPrerequisites { get; }
    public IReadOnlySet<string> RequiredConsumers { get; }
    public IReadOnlyList<ModerationPolicyNativeEvidence> RegisteredConsumers { get; }

    private static IReadOnlyList<ModerationPolicyNativeEvidence> Facts(
        IEnumerable<ModerationPolicyNativeEvidence> values, AuthorityScope jurisdiction, DateTimeOffset verifiedAt)
    {
        ArgumentNullException.ThrowIfNull(values);
        var facts = values.ToArray();
        if (facts.Length == 0 || facts.Any(fact => fact is null || fact.Jurisdiction != jurisdiction
                || fact.VerifiedAt != verifiedAt.ToUniversalTime())
            || facts.Select(fact => fact.Code).Distinct(StringComparer.Ordinal).Count() != facts.Length
            || facts.Select(fact => fact.SourceId).Distinct().Count() != facts.Length)
        {
            throw new ArgumentException("Native facts require unique current source identities and the exact publication scope.", nameof(values));
        }

        return Array.AsReadOnly(facts);
    }

    private static FrozenSet<string> Snapshot(IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var supplied = values.ToArray();
        if (supplied.Length == 0 || supplied.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 100)
            || supplied.Distinct(StringComparer.Ordinal).Count() != supplied.Length)
        {
            throw new ArgumentException("Evidence requires nonempty, unique bounded native inventory codes.", nameof(values));
        }

        return supplied.ToFrozenSet(StringComparer.Ordinal);
    }
}

/// <summary>
/// A complete immutable adopted-policy snapshot. Replacement produces a new revision without
/// mutating this snapshot. Definitions are retained to prevent rule-version meaning reuse;
/// omitting effect evidence retires support without deleting adopted meaning or sanctions.
/// </summary>
public sealed class ModerationPolicy
{
    private readonly List<ModerationPolicyRule> _rules = [];

    private ModerationPolicy()
    {
    }

    public Guid Id { get; private set; }
    public long Revision { get; private set; }
    public ModerationScopeKind JurisdictionKind { get; private set; }
    public Guid JurisdictionId { get; private set; }
    public Guid JurisdictionTrustDomainId { get; private set; }
    public Guid? JurisdictionSolutionId { get; private set; }
    public Guid? JurisdictionInstanceId { get; private set; }
    public Guid? JurisdictionTenantId { get; private set; }
    public Guid? JurisdictionCommunityId { get; private set; }
    public Guid? JurisdictionResourceId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid PublicationId { get; private set; }
    public int RuleCount { get; private set; }
    public bool IsActive => _rules.Any(rule => rule.ActiveCapabilityMask != 0);
    public IReadOnlyList<ModerationPolicyRule> Rules => _rules.AsReadOnly();
    public AuthorityScope Jurisdiction
    {
        get => AuthorityScope.FromCoordinates(JurisdictionKind, JurisdictionTrustDomainId, JurisdictionSolutionId,
            JurisdictionInstanceId, JurisdictionTenantId, JurisdictionCommunityId, JurisdictionResourceId);
        private set
        {
            JurisdictionId = value.Id;
            (JurisdictionKind, JurisdictionTrustDomainId, JurisdictionSolutionId,
                JurisdictionInstanceId, JurisdictionTenantId, JurisdictionCommunityId, JurisdictionResourceId) = value.Coordinates;
        }
    }

    /// <summary>Adopts immutable meaning in an explicitly inactive snapshot; no effect evidence is inferred.</summary>
    public static ModerationPolicy Adopt(
        Guid id, AuthorityScope jurisdiction, IEnumerable<ModerationPolicyRule> rules,
        Guid publicationId, DateTimeOffset now) =>
        Compile(id, 1, jurisdiction, rules, [], publicationId, now, now);

    /// <summary>Publishes an initially active snapshot using positively supplied native evidence.</summary>
    public static ModerationPolicy Publish(
        Guid id, AuthorityScope jurisdiction, IEnumerable<ModerationPolicyRule> rules,
        IEnumerable<ModerationPolicyEffectEvidence> evidence, Guid publicationId, DateTimeOffset now)
    {
        var proofs = RequireActiveEvidence(evidence);
        return Compile(id, 1, jurisdiction, rules, proofs, publicationId, now, now);
    }

    /// <summary>Activates an inactive adoption under a new checked publication revision.</summary>
    public ModerationPolicy Activate(
        AuthorityScope jurisdiction, long expectedRevision, IEnumerable<ModerationPolicyEffectEvidence> evidence,
        Guid publicationId, DateTimeOffset now)
    {
        if (IsActive)
        {
            throw new InvalidOperationException("An active snapshot requires complete-state replacement.");
        }

        return Replace(jurisdiction, expectedRevision, Rules, evidence, publicationId, now);
    }

    /// <summary>Requires exact ownership and expected revision; even overflow leaves the prior snapshot intact.</summary>
    public ModerationPolicy Replace(
        AuthorityScope jurisdiction, long expectedRevision, IEnumerable<ModerationPolicyRule> rules,
        IEnumerable<ModerationPolicyEffectEvidence> evidence, Guid publicationId, DateTimeOffset now)
    {
        var proofs = RequireActiveEvidence(evidence);
        return ReplaceState(jurisdiction, expectedRevision, rules, proofs, publicationId, now);
    }

    /// <summary>Explicitly replaces inactive adoption or retires all active support, retaining every definition.</summary>
    public ModerationPolicy ReplaceAdoption(
        AuthorityScope jurisdiction, long expectedRevision, IEnumerable<ModerationPolicyRule> rules,
        Guid publicationId, DateTimeOffset now) =>
        ReplaceState(jurisdiction, expectedRevision, rules, [], publicationId, now);

    private ModerationPolicy ReplaceState(
        AuthorityScope jurisdiction, long expectedRevision, IEnumerable<ModerationPolicyRule> rules,
        IEnumerable<ModerationPolicyEffectEvidence> evidence, Guid publicationId, DateTimeOffset now)
    {
        ValidatePersistedState();
        if (jurisdiction != Jurisdiction || expectedRevision != Revision || now < UpdatedAt
            || publicationId == PublicationId)
        {
            throw new InvalidOperationException("Replacement requires the exact current jurisdiction, revision and a new publication.");
        }

        var nextRevision = checked(Revision + 1);
        var next = Compile(Id, nextRevision, jurisdiction, rules, evidence, publicationId, CreatedAt, now);
        foreach (var adopted in _rules)
        {
            var retained = next._rules.SingleOrDefault(rule => rule.Rule == adopted.Rule);
            if (retained is null || !adopted.HasSameDefinition(retained))
            {
                throw new ArgumentException("Adopted rule versions must retain their immutable definitions; changes require a new version.");
            }
        }

        return next;
    }

    private static ModerationPolicyEffectEvidence[] RequireActiveEvidence(IEnumerable<ModerationPolicyEffectEvidence> evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var proofs = evidence.ToArray();
        if (proofs.Length == 0)
        {
            throw new ArgumentException("An active publication requires positive effect evidence; use inactive adoption explicitly.", nameof(evidence));
        }

        return proofs;
    }

    /// <summary>Checks scalar ancestry and complete child structure, never fabricating current activation evidence.</summary>
    public void ValidatePersistedState()
    {
        RequireVersion7(Id, nameof(Id));
        RequireVersion7(PublicationId, nameof(PublicationId));
        var jurisdiction = Jurisdiction;
        if (JurisdictionId != jurisdiction.Id || Revision < 1 || UpdatedAt < CreatedAt || (Revision == 1 && UpdatedAt != CreatedAt)
            || RuleCount < 1 || _rules.Count != RuleCount
            || _rules.Any(rule => rule is null)
            || _rules.Select(rule => rule.Id).Distinct().Count() != RuleCount
            || _rules.Select(rule => rule.Rule).Distinct().Count() != RuleCount)
        {
            throw new ArgumentException("The persisted policy revision, timestamps or complete rule collection are inconsistent.");
        }

        foreach (var rule in _rules)
        {
            rule.ValidatePersistedState();
            if (rule.PolicyId != Id || rule.PolicyRevision != Revision || rule.RuleOwnerId != jurisdiction.Id)
            {
                throw new ArgumentException("Every rule must belong to this exact policy revision and jurisdiction.");
            }
        }
    }

    /// <summary>
    /// Checks the published support snapshot and adopted ceilings. The native lifecycle writer
    /// must separately requalify current policy, prerequisites, consumers and ordinary caller authority.
    /// </summary>
    public bool Permits(
        AuthorityScope jurisdiction, ModerationRuleReference rule, ModerationPolicyOperationPurpose purpose,
        IEnumerable<ModerationCapability> capabilities, DateTimeOffset startsAt, DateTimeOffset? endsAt,
        DateTimeOffset? reviewAt, int requiredApprovals)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        var requested = capabilities.ToArray();
        var adopted = _rules.SingleOrDefault(terms => terms.Rule == rule);
        return jurisdiction == Jurisdiction && adopted is not null
            && requested.All(capability => adopted.ActiveCapabilities.Contains(capability))
            && (!(jurisdiction.Kind is ModerationScopeKind.TrustDomain or ModerationScopeKind.Solution)
                || requiredApprovals == 2)
            && adopted.Permits(purpose, requested, startsAt, endsAt, reviewAt, requiredApprovals);
    }

    internal static void RequireVersion7(Guid id, string parameter)
    {
        if (id == Guid.Empty || id.Version != 7 || id.Variant is < 8 or > 11)
        {
            throw new ArgumentException("A UUIDv7 identifier is required.", parameter);
        }
    }

    private static ModerationPolicy Compile(
        Guid id, long revision, AuthorityScope jurisdiction, IEnumerable<ModerationPolicyRule> rules,
        IEnumerable<ModerationPolicyEffectEvidence> evidence, Guid publicationId,
        DateTimeOffset createdAt, DateTimeOffset now)
    {
        RequireVersion7(id, nameof(id));
        RequireVersion7(publicationId, nameof(publicationId));
        ArgumentNullException.ThrowIfNull(jurisdiction);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(evidence);
        var definitions = rules.ToArray();
        var proofs = evidence.ToArray();
        if (definitions.Length == 0 || definitions.Any(rule => rule is null)
            || definitions.Select(rule => rule.Id).Distinct().Count() != definitions.Length
            || definitions.Select(rule => rule.Rule).Distinct().Count() != definitions.Length)
        {
            throw new ArgumentException("Publication requires a complete unique set of adopted definitions.", nameof(rules));
        }

        if (proofs.Any(proof => proof is null || proof.PolicyId != id || proof.PolicyRevision != revision
                || proof.Jurisdiction != jurisdiction || proof.PublicationId != publicationId
                || proof.VerifiedAt != now.ToUniversalTime()
                || !definitions.Any(rule => rule.Rule == proof.Rule && rule.Capabilities.Contains(proof.Capability)))
            || proofs.Select(proof => (proof.Rule, proof.Capability)).Distinct().Count() != proofs.Length)
        {
            throw new ArgumentException("Effect evidence must match the entire proposed publication exactly.", nameof(evidence));
        }

        var policy = new ModerationPolicy
        {
            Id = id,
            Revision = revision,
            Jurisdiction = jurisdiction,
            CreatedAt = createdAt.ToUniversalTime(),
            UpdatedAt = now.ToUniversalTime(),
            PublicationId = publicationId,
            RuleCount = definitions.Length
        };
        foreach (var rule in definitions)
        {
            var supported = proofs.Where(proof => proof.Rule == rule.Rule).Select(proof => proof.Capability).ToArray();
            var mask = supported.Length == 0 ? 0 : EffectiveRestrictions.CreateCapabilityMask(supported);
            policy._rules.Add(rule.Publish(id, revision, mask));
        }

        policy.ValidatePersistedState();
        return policy;
    }
}
