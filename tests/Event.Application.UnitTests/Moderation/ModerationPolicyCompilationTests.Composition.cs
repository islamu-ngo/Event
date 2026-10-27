using System.Collections.Immutable;
using Explore.Application.Features.Moderation.Policy;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;

namespace Event.Application.UnitTests.Moderation;

public sealed partial class ModerationPolicyCompilationTests
{
    [Test]
    [Arguments(ModerationReferenceProfileId.CFG05)]
    [Arguments(ModerationReferenceProfileId.CFG06)]
    [Arguments(ModerationReferenceProfileId.CFG09)]
    public async Task Retained_contributions_keep_complete_meaning_after_support_retirement(
        ModerationReferenceProfileId reference)
    {
        var state = State(reference);
        var original = Compile(state).Draft!;
        var contribution = Contribution(original, 0, 700);
        var declared = Compile(state with { SupportedProfiles = [] }).Draft!;

        var proposed = ModerationPolicyProposedStateCompiler.Compose(declared, [contribution], Now);
        var retained = ModerationPolicyProposedStateCompiler.ComposeRetained(declared, [contribution], Now);

        await Assert.That(proposed.Outcome).IsEqualTo(ModerationPolicyCompositionOutcome.Invalid);
        await Assert.That(retained.Outcome).IsEqualTo(ModerationPolicyCompositionOutcome.Preview);
        await Assert.That(retained.Effects.Length).IsEqualTo(contribution.Effects.Length);
        await Assert.That(retained.Consequences.Single().Plan).IsEqualTo(original.Profiles[0].Consequences[0]);
        await Assert.That(retained.Operations.Single().OverlayDenials.IsEmpty)
            .IsEqualTo(reference != ModerationReferenceProfileId.CFG06);
        await Assert.That(retained.Transitions.Single().Disposition)
            .IsEqualTo(ModerationPolicyTransitionDisposition.OwningRequalificationRequired);
    }

    [Test]
    public async Task Same_target_denial_precedes_review_without_destroying_the_later_review_contribution()
    {
        var state = State(ModerationReferenceProfileId.CFG05, ModerationReferenceProfileId.CFG06);
        state = state with
        {
            Profiles = [state.Profiles[0], state.Profiles[1] with
                { Timing = new(ModerationReferenceTimingKind.Finite, TimeSpan.FromHours(1)) }]
        };
        var draft = Compile(state).Draft!;
        var review = Contribution(draft, 0, 700);
        var denial = Contribution(draft, 1, 701);
        var first = ModerationPolicyProposedStateCompiler.Compose(draft, [review, denial], Now);
        var reversed = ModerationPolicyProposedStateCompiler.Compose(draft, [denial, review], Now);
        await Assert.That(first.Operations.Single().Contributions[0].Effect.Mode).IsEqualTo(ModerationReferenceEffectMode.Deny);
        await Assert.That(first.Operations.Single().Contributions.Select(item => item.ContributionId)
            .SequenceEqual(reversed.Operations.Single().Contributions.Select(item => item.ContributionId))).IsTrue();
        await Assert.That(first.Effects.Length).IsEqualTo(2);
        var later = ModerationPolicyProposedStateCompiler.Compose(draft, [denial, review], Now.AddHours(2));
        await Assert.That(later.Effects.Length).IsEqualTo(2);
        await Assert.That(later.Operations.Single().Contributions.Single().Effect.Mode)
            .IsEqualTo(ModerationReferenceEffectMode.RequireReview);
        await Assert.That(later.Operations.Single().OverlayDenials.IsEmpty).IsTrue();
        await Assert.That(later.Effects.Single(item => item.ContributionId == denial.Id).Interval.EndsAt)
            .IsEqualTo(Now.AddHours(1));
    }

    [Test]
    public async Task Different_native_operation_partitions_cannot_mask_each_other()
    {
        var draft = Compile(State(ModerationReferenceProfileId.CFG05, ModerationReferenceProfileId.CFG06)).Draft!;
        var review = Contribution(draft, 0, 700);
        var denial = Contribution(draft, 1, 701);
        denial = denial with
        {
            Effects = denial.Effects.Select(item => item with
            { Target = item.Target with { Operation = "other.publication" } }).ToImmutableArray()
        };
        var result = ModerationPolicyProposedStateCompiler.Compose(draft, [denial, review], Now);
        await Assert.That(result.Operations.Length).IsEqualTo(2);
        await Assert.That(result.Operations.Single(item => item.Target.Operation == "Publish")
            .Contributions.Single().Effect.Mode).IsEqualTo(ModerationReferenceEffectMode.RequireReview);
    }

    [Test]
    public async Task Broader_scope_contributions_preserve_original_scope_when_resolved_to_the_same_native_target()
    {
        var state = State(ModerationReferenceProfileId.CFG05, ModerationReferenceProfileId.CFG06);
        state = state with { Profiles = [state.Profiles[0], state.Profiles[1] with { ScopeKinds = [ModerationScopeKind.Tenant] }] };
        var draft = Compile(state).Draft!;
        var review = Contribution(draft, 0, 700);
        var denial = Contribution(draft, 1, 701);
        denial = denial with
        {
            Effects = denial.Effects.Select(item => item with { Target = item.Target with { Scope = Resource } }).ToImmutableArray(),
            Consequences = denial.Consequences.Select(item => item with { Target = item.Target with { Scope = Resource } }).ToImmutableArray()
        };
        var result = ModerationPolicyProposedStateCompiler.Compose(draft, [review, denial], Now);
        await Assert.That(result.Operations.Length).IsEqualTo(1);
        await Assert.That(result.Effects.Single(item => item.ContributionId == denial.Id).Source.Scope).IsEqualTo(Tenant);
        await Assert.That(result.Effects.Single(item => item.ContributionId == review.Id).Source.Scope).IsEqualTo(Resource);
    }

    [Test]
    public async Task Irreversible_same_target_causes_require_native_resolution_instead_of_highest_grade_winning()
    {
        var state = State(ModerationReferenceProfileId.CFG17, ModerationReferenceProfileId.CFG19);
        state = state with { Profiles = [state.Profiles[0], state.Profiles[1] with { ScopeKinds = [ModerationScopeKind.Community] }] };
        var draft = Compile(state).Draft!;
        var result = ModerationPolicyProposedStateCompiler.Compose(draft,
            [Contribution(draft, 0, 700), Contribution(draft, 1, 701)], Now);
        await Assert.That(result.Outcome).IsEqualTo(ModerationPolicyCompositionOutcome.RequiresOwningResolution);
        await Assert.That(result.Transitions.Length).IsEqualTo(1);
        await Assert.That(result.Transitions.Single().Contributions.Length).IsEqualTo(2);
        await Assert.That(result.Transitions.Single().Disposition)
            .IsEqualTo(ModerationPolicyTransitionDisposition.ConflictingCausesRequireResolution);
    }

    [Test]
    public async Task Repeated_compatible_causes_produce_one_owner_transition_with_all_contributions()
    {
        var draft = Compile(State(ModerationReferenceProfileId.CFG19)).Draft!;
        var result = ModerationPolicyProposedStateCompiler.Compose(draft,
            [Contribution(draft, 0, 700), Contribution(draft, 0, 701)], Now);
        await Assert.That(result.Outcome).IsEqualTo(ModerationPolicyCompositionOutcome.Preview);
        await Assert.That(result.Transitions.Length).IsEqualTo(1);
        await Assert.That(result.Transitions.Single().Contributions.Length).IsEqualTo(2);
        await Assert.That(result.Transitions.Single().Disposition)
            .IsEqualTo(ModerationPolicyTransitionDisposition.OwningRequalificationRequired);
    }

    [Test]
    public async Task Object_quarantine_keeps_its_source_subject_and_masks_only_the_resolved_resource_version()
    {
        var draft = Compile(State(ModerationReferenceProfileId.CFG08, ModerationReferenceProfileId.CFG09)).Draft!;
        var read = Contribution(draft, 0, 700);
        var quarantine = Contribution(draft, 1, 701) with { SubjectId = Id(800) };
        quarantine = quarantine with
        {
            Effects = quarantine.Effects.Select(item => item with
            { Target = read.Effects.Single().Target }).ToImmutableArray()
        };
        var result = ModerationPolicyProposedStateCompiler.Compose(draft, [read, quarantine], Now);
        await Assert.That(result.Operations.Length).IsEqualTo(1);
        await Assert.That(result.Operations.Single().Contributions.Length).IsEqualTo(2);
        await Assert.That(result.Operations.Single().OverlayDenials.SequenceEqual([ModerationCapability.ReadResource])).IsTrue();
        await Assert.That(result.Effects.Single(item => item.ContributionId == quarantine.Id).Source.SubjectId).IsEqualTo(Id(800));
        var otherVersion = quarantine with
        {
            Effects = quarantine.Effects.Select(item => item with { Target = item.Target with { TargetRevision = 2 } }).ToImmutableArray()
        };
        var separate = ModerationPolicyProposedStateCompiler.Compose(draft, [read, otherVersion], Now);
        await Assert.That(separate.Operations.Length).IsEqualTo(2);
    }

    [Test]
    public async Task Object_review_deadline_never_automatically_releases_quarantined_bytes()
    {
        var draft = Compile(State(ModerationReferenceProfileId.CFG09)).Draft!;
        var result = ModerationPolicyProposedStateCompiler.Compose(draft, [Contribution(draft, 0, 700)], Now.AddDays(2));
        await Assert.That(result.Effects.Single().Interval.ReviewAt).IsEqualTo(Now.AddDays(1));
        await Assert.That(result.Effects.Single().Interval.EndsAt is null).IsTrue();
        await Assert.That(result.Operations.Single().Contributions.Single().Effect.Mode).IsEqualTo(ModerationReferenceEffectMode.Quarantine);
    }

    [Test]
    public async Task Physical_hold_is_bounded_by_the_captured_event_end_and_never_reissues_history()
    {
        var draft = Compile(State(ModerationReferenceProfileId.CFG15)).Draft!;
        var contribution = Contribution(draft, 0, 700, eventEnd: Now.AddHours(3));
        var before = ModerationPolicyProposedStateCompiler.Compose(draft, [contribution], Now);
        await Assert.That(before.Effects.All(item => item.Interval.EndsAt == Now.AddHours(3)
            && item.Interval.ReviewAt == Now.AddHours(3))).IsTrue();
        var after = ModerationPolicyProposedStateCompiler.Compose(draft, [contribution], Now.AddHours(3));
        await Assert.That(after.Operations.IsEmpty).IsTrue();
        await Assert.That(after.Transitions.IsEmpty).IsTrue();
        await Assert.That(after.Consequences.Single().Plan.Restoration)
            .IsEqualTo(ModerationRestorationMode.CurrentRightsNeverReissueCancelledTickets);
        await Assert.That(after.Consequences.Single().Interval.EventWindowEnd).IsEqualTo(Now.AddHours(3));
    }

    [Test]
    public async Task Revocation_precedes_hold_only_for_the_same_participant_event_window()
    {
        var draft = Compile(State(ModerationReferenceProfileId.CFG15, ModerationReferenceProfileId.CFG16)).Draft!;
        var hold = Contribution(draft, 0, 700, eventEnd: Now.AddHours(3));
        var revoke = Contribution(draft, 1, 701, eventEnd: Now.AddHours(3));
        var result = ModerationPolicyProposedStateCompiler.Compose(draft, [hold, revoke], Now);
        var admission = result.Operations.Single(item => item.Target.Operation == "Admission");
        await Assert.That(admission.Contributions[0].Effect.Mode).IsEqualTo(ModerationReferenceEffectMode.Revoke);
        await Assert.That(admission.Contributions.Length).IsEqualTo(2);
        await Assert.That(result.Outcome).IsEqualTo(ModerationPolicyCompositionOutcome.RequiresOwningResolution);
    }

    [Test]
    [Arguments("missing-effect")]
    [Arguments("duplicate-effect")]
    [Arguments("missing-consequence")]
    [Arguments("wrong-person")]
    [Arguments("outside-scope")]
    [Arguments("duplicate-contribution")]
    [Arguments("overflow-time")]
    public async Task Malformed_contribution_sets_cannot_produce_a_partial_effective_plan(string mutation)
    {
        var draft = Compile(State(ModerationReferenceProfileId.CFG06)).Draft!;
        var contribution = Contribution(draft, 0, 700);
        contribution = mutation switch
        {
            "missing-effect" => contribution with { Effects = [] },
            "duplicate-effect" => contribution with { Effects = [.. contribution.Effects, contribution.Effects[0]] },
            "missing-consequence" => contribution with { Consequences = [] },
            "wrong-person" => contribution with { SubjectId = Id(999) },
            "outside-scope" => contribution with
            {
                Effects = contribution.Effects.Select(item => item with { Target = item.Target with { Scope = Trust } }).ToImmutableArray()
            },
            "overflow-time" => contribution with { StartsAt = DateTimeOffset.MaxValue.AddMinutes(-1) },
            "duplicate-contribution" => contribution,
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        ImmutableArray<ModerationPolicyContribution> contributions =
            mutation == "duplicate-contribution" ? [contribution, contribution] : [contribution];
        foreach (var result in new[]
        {
            ModerationPolicyProposedStateCompiler.Compose(draft, contributions, Now),
            ModerationPolicyProposedStateCompiler.ComposeRetained(draft, contributions, Now)
        })
        {
            await Assert.That(result.Outcome).IsEqualTo(ModerationPolicyCompositionOutcome.Invalid);
            await Assert.That(result.Effects.IsEmpty).IsTrue();
            await Assert.That(result.Transitions.IsEmpty).IsTrue();
        }
    }
}
