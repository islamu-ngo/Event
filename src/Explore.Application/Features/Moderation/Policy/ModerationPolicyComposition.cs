using System.Collections.Immutable;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;

namespace Explore.Application.Features.Moderation.Policy;

/// <summary>
/// An exact native operation partition. Distinct versions, participants, purposes, windows or
/// jurisdictions must have distinct keys. Strings identify native operations, never expressions.
/// These coordinates are preview inputs, not proof that the owning resolver qualified a target.
/// </summary>
public sealed record ModerationPolicyTarget(
    AuthorityScope Scope, ModerationSubjectKind SubjectKind, Guid SubjectId,
    Guid TargetId, long TargetRevision, string Operation, DateTimeOffset? WindowEnd);

public sealed record ModerationPolicyEffectTarget(ModerationReferenceEffect Effect, ModerationPolicyTarget Target);
public sealed record ModerationPolicyConsequenceTarget(ModerationAdditiveStrategyKind? Strategy, ModerationPolicyTarget Target);

/// <summary>Separate case/simulator input; never part of portable policy configuration.</summary>
public sealed record ModerationPolicyContribution(
    Guid Id, ModerationRuleReference Rule, AuthorityScope Scope, ModerationSubjectKind SubjectKind,
    Guid SubjectId, DateTimeOffset StartsAt, DateTimeOffset? EventWindowEnd,
    ImmutableArray<ModerationPolicyEffectTarget> Effects,
    ImmutableArray<ModerationPolicyConsequenceTarget> Consequences);

public sealed record ModerationPolicyBoundEffect(
    Guid ContributionId, ModerationRuleReference Rule, ModerationReferenceEffect Effect,
    ModerationPolicyTarget Target, ModerationReferenceInterval Interval, ModerationPolicyContribution Source);

public sealed record ModerationPolicyBoundConsequence(
    Guid ContributionId, ModerationRuleReference Rule, ModerationReferenceGround Ground,
    ModerationProtectionPurpose? ProtectionPurpose, ModerationAdditiveStrategyKind? Strategy,
    ModerationPolicyTarget Target, ModerationReferenceInterval Interval, ModerationConsequenceModes Plan,
    ModerationPolicyContribution Source);

/// <summary>
/// Ordered only inside the same qualified operation. All contributions remain necessary, including
/// masked reviews/guards and their distinct intervals. OverlayDenials never includes NativeOwner.
/// Empty denials mean ordinary authorization still decides, never a capability grant.
/// </summary>
public sealed record ModerationPolicyOperationComposition(
    ModerationPolicyTarget Target, ImmutableArray<ModerationPolicyBoundEffect> Contributions,
    ImmutableArray<ModerationCapability> OverlayDenials);

public enum ModerationPolicyTransitionDisposition { OwningRequalificationRequired = 1, ConflictingCausesRequireResolution = 2 }

/// <summary>
/// One owning transition per target, with the full cause set. No seat/refund/credential action may
/// be inferred from the first cause. Conflicting causes require actual current owner resolution.
/// </summary>
public sealed record ModerationPolicyTransitionComposition(
    ModerationPolicyTarget Target, ImmutableArray<ModerationPolicyBoundConsequence> Contributions,
    ModerationPolicyTransitionDisposition Disposition);

public enum ModerationPolicyCompositionOutcome { Invalid = 1, Preview = 2, RequiresOwningResolution = 3 }

public sealed class ModerationPolicyCompositionResult
{
    internal ModerationPolicyCompositionResult(
        ModerationPolicyCompositionOutcome outcome, ImmutableArray<ModerationPolicyBoundEffect> effects,
        ImmutableArray<ModerationPolicyBoundConsequence> consequences,
        ImmutableArray<ModerationPolicyOperationComposition> operations,
        ImmutableArray<ModerationPolicyTransitionComposition> transitions,
        ImmutableArray<ModerationPolicyDiagnostic> diagnostics)
        => (Outcome, Effects, Consequences, Operations, Transitions, Diagnostics) =
            (outcome, effects, consequences, operations, transitions, diagnostics);

    public ModerationPolicyCompositionOutcome Outcome { get; }
    public ImmutableArray<ModerationPolicyBoundEffect> Effects { get; }
    public ImmutableArray<ModerationPolicyBoundConsequence> Consequences { get; }
    public ImmutableArray<ModerationPolicyOperationComposition> Operations { get; }
    public ImmutableArray<ModerationPolicyTransitionComposition> Transitions { get; }
    public ImmutableArray<ModerationPolicyDiagnostic> Diagnostics { get; }
}

public static partial class ModerationPolicyProposedStateCompiler
{
    /// <summary>
    /// Deterministic, side-effect-free preview at an explicit instant. Neither a coherent preview nor
    /// a target string certifies native support, actual findings, approvals or current eligibility.
    /// Bindings include every effect and all thirteen dimensions of every base/additive plan.
    /// </summary>
    public static ModerationPolicyCompositionResult Compose(
        ModerationPolicyCompiledDraft draft, ImmutableArray<ModerationPolicyContribution> contributions,
        DateTimeOffset at) => ComposeCore(draft, contributions, at, requireSupport: true);

    /// <summary>
    /// Reconstructs retained contribution meaning even after support for new activations is retired.
    /// The native caller must qualify the actual retained transition and its disclosure separately.
    /// This result remains a preview with owning requalification obligations, never activation authority.
    /// </summary>
    public static ModerationPolicyCompositionResult ComposeRetained(
        ModerationPolicyCompiledDraft draft, ImmutableArray<ModerationPolicyContribution> contributions,
        DateTimeOffset at) => ComposeCore(draft, contributions, at, requireSupport: false);

    private static ModerationPolicyCompositionResult ComposeCore(
        ModerationPolicyCompiledDraft draft, ImmutableArray<ModerationPolicyContribution> contributions,
        DateTimeOffset at, bool requireSupport)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var issues = ImmutableArray.CreateBuilder<ModerationPolicyDiagnostic>();
        var effects = ImmutableArray.CreateBuilder<ModerationPolicyBoundEffect>();
        var consequences = ImmutableArray.CreateBuilder<ModerationPolicyBoundConsequence>();
        if (!Unique(contributions, allowEmpty: true) || !Instant(at)
            || contributions.Select(item => item.Id).Distinct().Count() != contributions.Length)
            return InvalidComposition();
        foreach (var contribution in contributions.OrderBy(item => item.Id))
        {
            var profile = draft.Profiles.SingleOrDefault(item => item.Definition.Rule == contribution.Rule);
            if (profile is null || !Version7(contribution.Id) || !Instant(contribution.StartsAt)
                || contribution.SubjectId == Guid.Empty || contribution.Scope is null
                || requireSupport && !draft.Proposed.SupportedProfiles.Contains(contribution.Rule)
                || !profile.Reference.AcceptsScope(draft.Proposed.Jurisdiction, contribution.Scope)
                || !profile.Definition.ScopeKinds.Contains(contribution.Scope.Kind)
                || !profile.Definition.SubjectKinds.Contains(contribution.SubjectKind)
                || !Unique(contribution.Effects) || !Unique(contribution.Consequences)
                || contribution.Effects.Any(item => item.Effect is null
                    || !ValidTarget(item.Target, contribution))
                || contribution.Consequences.Any(item => !ValidTarget(item.Target, contribution))
                || !SameSet(contribution.Effects.Select(item => item.Effect), profile.Definition.Effects)
                || contribution.Effects.Select(item => item.Effect).Distinct().Count() != contribution.Effects.Length
                || !SameSet(contribution.Consequences.Select(item => item.Strategy),
                    new ModerationAdditiveStrategyKind?[] { null }.Concat(profile.Definition.Strategies.Select(item =>
                        (ModerationAdditiveStrategyKind?)item)))
                || contribution.Consequences.Select(item => item.Strategy).Distinct().Count() != contribution.Consequences.Length
                || !TryBind(profile.Definition.Timing, contribution, draft.Bounds, out var interval))
                return InvalidComposition(contribution.Rule);

            foreach (var binding in contribution.Effects.OrderBy(item => item.Effect.Kind).ThenBy(item => item.Effect.Capability))
                effects.Add(new(contribution.Id, contribution.Rule, binding.Effect, binding.Target, interval, contribution));
            foreach (var binding in contribution.Consequences.OrderBy(item => item.Strategy))
            {
                var plan = binding.Strategy is { } strategy
                    ? profile.Consequences[Array.IndexOf(profile.Definition.Strategies.Order().ToArray(), strategy) + 1]
                    : profile.Consequences[0];
                consequences.Add(new(contribution.Id, contribution.Rule, profile.Reference.Ground,
                    profile.Definition.ProtectionPurpose, binding.Strategy, binding.Target, interval, plan, contribution));
            }
        }

        // Keep ended/future contributions in the explanation; only the current projection filters them.
        var operations = effects.Where(item => Current(item.Interval, at)).GroupBy(item => item.Target)
            .Select(group => new ModerationPolicyOperationComposition(group.Key,
                group.OrderByDescending(item => Priority(item.Effect.Mode)).ThenBy(item => item.ContributionId)
                    .ThenBy(item => item.Effect.Kind).ThenBy(item => item.Effect.Capability).ToImmutableArray(),
                group.Where(item => item.Effect.Execution == ModerationReferenceExecution.OverlayDenial)
                    .Select(item => item.Effect.Capability!.Value).Distinct().Order().ToImmutableArray()))
            .OrderBy(item => TargetOrder(item.Target), StringComparer.Ordinal).ToImmutableArray();
        var transitions = consequences.Where(item => Current(item.Interval, at)).GroupBy(item => item.Target)
            .Select(group =>
            {
                var causes = group.OrderBy(item => item.ContributionId).ThenBy(item => item.Strategy).ToImmutableArray();
                var irreversible = causes.Where(item =>
                    item.Plan.Capacity == ModerationCapacityMode.OwningParticipantTransitionExactlyOnce
                    || item.Plan.ExistingAdmission is ModerationExistingAdmissionMode.SelectedParticipantHoldOrRevocation
                        or ModerationExistingAdmissionMode.SelectedCommunityParticipantRevocation
                        or ModerationExistingAdmissionMode.InvalidateProvedBadEntitlement).ToArray();
                var conflict = irreversible.Select(item => (item.Ground, item.ProtectionPurpose,
                    item.Plan.Settlement, item.Plan.ExistingAdmission, item.Plan.Credentials)).Distinct().Count() > 1;
                if (conflict)
                    issues.Add(new(ModerationPolicyDiagnosticKind.IrreversibleCauseConflict,
                        ModerationPolicyDiagnosticSeverity.ActivationBlocker));
                return new ModerationPolicyTransitionComposition(group.Key, causes, conflict
                    ? ModerationPolicyTransitionDisposition.ConflictingCausesRequireResolution
                    : ModerationPolicyTransitionDisposition.OwningRequalificationRequired);
            }).OrderBy(item => TargetOrder(item.Target), StringComparer.Ordinal).ToImmutableArray();
        return new(issues.Count == 0 ? ModerationPolicyCompositionOutcome.Preview
                : ModerationPolicyCompositionOutcome.RequiresOwningResolution,
            effects.ToImmutable(), consequences.ToImmutable(), operations, transitions, issues.ToImmutable());
    }

    private static ModerationPolicyCompositionResult InvalidComposition(ModerationRuleReference? rule = null) =>
        new(ModerationPolicyCompositionOutcome.Invalid, [], [], [], [],
            [new(ModerationPolicyDiagnosticKind.InvalidContribution, ModerationPolicyDiagnosticSeverity.Error, rule)]);

    private static bool Instant(DateTimeOffset value) =>
        value.Offset == TimeSpan.Zero && value != DateTimeOffset.MinValue && value != DateTimeOffset.MaxValue;

    private static bool ValidTarget(ModerationPolicyTarget target, ModerationPolicyContribution contribution) =>
        target?.Scope is not null && contribution.Scope.Contains(target.Scope)
        && Enum.IsDefined(target.SubjectKind) && target.SubjectId != Guid.Empty
        // A content control resolves the actual viewer separately from its object subject.
        && (contribution.SubjectKind == ModerationSubjectKind.Content
            || target.SubjectKind == contribution.SubjectKind && target.SubjectId == contribution.SubjectId)
        && target.TargetId != Guid.Empty && target.TargetRevision > 0
        && Text(target.Operation, 100) && target.Operation.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_')
        && (target.WindowEnd is null || Instant(target.WindowEnd.Value))
        && (contribution.EventWindowEnd is null || target.WindowEnd == contribution.EventWindowEnd);

    private static bool TryBind(
        ModerationPolicyTiming timing, ModerationPolicyContribution contribution, ModerationPolicyBounds bounds,
        out ModerationReferenceInterval interval)
    {
        interval = null!;
        var start = contribution.StartsAt;
        var eventEnd = contribution.EventWindowEnd;
        var eventTiming = timing.Kind is ModerationReferenceTimingKind.EarlierOfDayOrEventEnd
            or ModerationReferenceTimingKind.FixedEventEnd;
        if (eventTiming && (eventEnd is null || !Instant(eventEnd.Value) || eventEnd <= start)
            || !eventTiming && eventEnd is not null
            || start.UtcTicks > DateTimeOffset.MaxValue.UtcTicks - timing.Duration.Ticks)
            return false;
        var boundary = start + timing.Duration;
        var end = timing.Kind switch
        {
            ModerationReferenceTimingKind.Finite or ModerationReferenceTimingKind.RecoveryOrDayReviewBoundary
                or ModerationReferenceTimingKind.ProtectiveDayWithReview => (DateTimeOffset?)boundary,
            ModerationReferenceTimingKind.FixedEventEnd => eventEnd,
            ModerationReferenceTimingKind.EarlierOfDayOrEventEnd => eventEnd < boundary ? eventEnd : boundary,
            _ => null
        };
        var review = timing.Kind switch
        {
            ModerationReferenceTimingKind.ReviewedIndefinite or ModerationReferenceTimingKind.ObjectReviewWithinDay
                or ModerationReferenceTimingKind.RecoveryOrDayReviewBoundary
                or ModerationReferenceTimingKind.ProtectiveDayWithReview => (DateTimeOffset?)boundary,
            ModerationReferenceTimingKind.EarlierOfDayOrEventEnd => end,
            _ => null
        };
        if (end is { } ends && ends - start > bounds.MaximumFiniteDuration
            || review is { } reviews && reviews - start > bounds.MaximumReviewDelay)
            return false;
        interval = new(timing.Kind, start, end, review, eventEnd);
        return true;
    }

    private static bool Current(ModerationReferenceInterval interval, DateTimeOffset at) =>
        at >= interval.StartsAt && (interval.EndsAt is null || at < interval.EndsAt);

    private static int Priority(ModerationReferenceEffectMode mode) => mode switch
    {
        ModerationReferenceEffectMode.Revoke => 7,
        ModerationReferenceEffectMode.Deny => 6,
        ModerationReferenceEffectMode.Quarantine => 5,
        ModerationReferenceEffectMode.Hold => 4,
        ModerationReferenceEffectMode.RequireReview => 3,
        ModerationReferenceEffectMode.Guard => 2,
        ModerationReferenceEffectMode.Correct => 1,
        ModerationReferenceEffectMode.Notice => 0,
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    private static string TargetOrder(ModerationPolicyTarget target) =>
        $"{target.Scope.TrustDomainId:N}/{target.Scope.Id:N}/{(int)target.SubjectKind}/{target.SubjectId:N}/"
        + $"{target.TargetId:N}/{target.TargetRevision:D20}/{target.Operation}/{target.WindowEnd?.UtcTicks:D20}";
}
