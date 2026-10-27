using System.Collections.Frozen;

namespace ISLAMU.Moderation.Domain;

public enum ModerationPolicyOperationPurpose
{
    Propose = 1,
    Activate = 2,
    Protect = 3
}

/// <summary>Immutable adopted meaning and ceilings; adoption alone enables no restrictive effect.</summary>
public sealed class ModerationPolicyRule
{
    private ModerationPolicyRule()
    {
    }

    public Guid Id { get; private set; }
    public Guid PolicyId { get; private set; }
    public long PolicyRevision { get; private set; }
    public Guid RuleOwnerId { get; private set; }
    public string RuleCode { get; private set; } = string.Empty;
    public long RuleRevision { get; private set; }
    public string Meaning { get; private set; } = string.Empty;
    public int PurposeMask { get; private set; }
    public int CapabilityMask { get; private set; }
    public int ActiveCapabilityMask { get; private set; }
    public long MaximumFiniteDurationTicks { get; private set; }
    public bool AllowsIndefinite { get; private set; }
    public long MaximumReviewDelayTicks { get; private set; }
    public int MinimumApprovals { get; private set; }
    public int MaximumApprovals { get; private set; }

    public ModerationRuleReference Rule => ModerationRuleReference.Create(RuleOwnerId, RuleCode, RuleRevision);
    public TimeSpan MaximumFiniteDuration => TimeSpan.FromTicks(MaximumFiniteDurationTicks);
    public TimeSpan MaximumReviewDelay => TimeSpan.FromTicks(MaximumReviewDelayTicks);
    public IReadOnlySet<ModerationCapability> Capabilities => EffectiveRestrictions.FromCapabilityMask(CapabilityMask);
    public IReadOnlySet<ModerationCapability> ActiveCapabilities => ActiveCapabilityMask == 0
        ? Array.Empty<ModerationCapability>().ToFrozenSet()
        : EffectiveRestrictions.FromCapabilityMask(ActiveCapabilityMask);
    public IReadOnlySet<ModerationPolicyOperationPurpose> Purposes => Enum.GetValues<ModerationPolicyOperationPurpose>()
        .Where(purpose => (PurposeMask & (1 << (int)purpose)) != 0).ToFrozenSet();

    public static ModerationPolicyRule Define(
        Guid id, ModerationRuleReference rule, string meaning,
        IEnumerable<ModerationPolicyOperationPurpose> purposes, IEnumerable<ModerationCapability> capabilities,
        TimeSpan maximumFiniteDuration, bool allowsIndefinite, TimeSpan maximumReviewDelay,
        int minimumApprovals, int maximumApprovals)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(purposes);
        var purposeSet = purposes.ToFrozenSet();
        if (purposeSet.Count == 0 || purposeSet.Any(purpose => !Enum.IsDefined(purpose)))
        {
            throw new ArgumentException("At least one finite operation purpose is required.", nameof(purposes));
        }

        var terms = new ModerationPolicyRule
        {
            Id = id,
            RuleOwnerId = rule.OwnerId,
            RuleCode = rule.Code,
            RuleRevision = rule.Revision,
            Meaning = meaning,
            PurposeMask = purposeSet.Aggregate(0, (mask, purpose) => mask | (1 << (int)purpose)),
            CapabilityMask = EffectiveRestrictions.CreateCapabilityMask(capabilities),
            MaximumFiniteDurationTicks = maximumFiniteDuration.Ticks,
            AllowsIndefinite = allowsIndefinite,
            MaximumReviewDelayTicks = maximumReviewDelay.Ticks,
            MinimumApprovals = minimumApprovals,
            MaximumApprovals = maximumApprovals
        };
        terms.ValidateDefinition();
        return terms;
    }

    /// <summary>Checks child-row structure, not current native prerequisite or consumer support.</summary>
    public void ValidatePersistedState()
    {
        ValidateDefinition();
        ModerationPolicy.RequireVersion7(PolicyId, nameof(PolicyId));
        if (PolicyRevision < 1 || (ActiveCapabilityMask & ~CapabilityMask) != 0)
        {
            throw new ArgumentException("A published rule requires an owning revision and a supported capability subset.");
        }

        _ = ActiveCapabilities;
    }

    /// <summary>Tests adopted ceilings only; it neither qualifies a caller nor proves active effect support.</summary>
    public bool Permits(
        ModerationPolicyOperationPurpose purpose, IEnumerable<ModerationCapability> capabilities,
        DateTimeOffset startsAt, DateTimeOffset? endsAt, DateTimeOffset? reviewAt, int requiredApprovals)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        var requested = capabilities.ToFrozenSet();
        if (!Enum.IsDefined(purpose) || !Purposes.Contains(purpose) || requested.Count == 0
            || requested.Any(capability => !Capabilities.Contains(capability))
            || requiredApprovals < MinimumApprovals || requiredApprovals > MaximumApprovals
            || (purpose == ModerationPolicyOperationPurpose.Protect && (endsAt is null || reviewAt is null))
            || (endsAt is null && (!AllowsIndefinite || requiredApprovals != 2 || reviewAt is null))
            || (endsAt is not null && (endsAt <= startsAt || endsAt.Value - startsAt > MaximumFiniteDuration))
            || (reviewAt is not null && (reviewAt <= startsAt || reviewAt.Value - startsAt > MaximumReviewDelay
                || (endsAt is not null && reviewAt > endsAt))))
        {
            return false;
        }

        return true;
    }

    internal ModerationPolicyRule Publish(Guid policyId, long revision, int activeMask)
    {
        ValidateDefinition();
        var row = new ModerationPolicyRule
        {
            Id = Id,
            PolicyId = policyId,
            PolicyRevision = revision,
            RuleOwnerId = RuleOwnerId,
            RuleCode = RuleCode,
            RuleRevision = RuleRevision,
            Meaning = Meaning,
            PurposeMask = PurposeMask,
            CapabilityMask = CapabilityMask,
            ActiveCapabilityMask = activeMask,
            MaximumFiniteDurationTicks = MaximumFiniteDurationTicks,
            AllowsIndefinite = AllowsIndefinite,
            MaximumReviewDelayTicks = MaximumReviewDelayTicks,
            MinimumApprovals = MinimumApprovals,
            MaximumApprovals = MaximumApprovals
        };
        row.ValidatePersistedState();
        return row;
    }

    internal bool HasSameDefinition(ModerationPolicyRule other) =>
        Id == other.Id && Rule == other.Rule && Meaning == other.Meaning
        && PurposeMask == other.PurposeMask && CapabilityMask == other.CapabilityMask
        && MaximumFiniteDurationTicks == other.MaximumFiniteDurationTicks
        && AllowsIndefinite == other.AllowsIndefinite && MaximumReviewDelayTicks == other.MaximumReviewDelayTicks
        && MinimumApprovals == other.MinimumApprovals && MaximumApprovals == other.MaximumApprovals;

    internal void ValidateDefinition()
    {
        ModerationPolicy.RequireVersion7(Id, nameof(Id));
        _ = Rule;
        _ = Capabilities;
        if (string.IsNullOrWhiteSpace(Meaning) || Meaning.Length > 4000
            || PurposeMask == 0 || (PurposeMask & ~0b1110) != 0
            || MaximumFiniteDurationTicks <= 0 || MaximumReviewDelayTicks <= 0
            || MinimumApprovals is < 1 or > 2 || MaximumApprovals < MinimumApprovals || MaximumApprovals > 2
            || (AllowsIndefinite && MaximumApprovals != 2))
        {
            throw new ArgumentException("Adopted rule meaning, purposes, interval and quorum ceilings are inconsistent.");
        }
    }
}
