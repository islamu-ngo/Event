using System.Collections.Immutable;
using System.Text.Json;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;

namespace Event.Application.UnitTests.Moderation;

/// <summary>Pure source semantics only; none of these authored cases is a native adoption receipt.</summary>
public sealed class ModerationNoticeSnapshotTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Snapshot_preserves_independent_retention_and_the_original_selected_calendar_floor()
    {
        var fixture = Fixture();
        var snapshot = Capture(fixture)!;
        await Assert.That(snapshot.RetainUntil).IsEqualTo(fixture.Source.Approval.RetainUntil);
        await Assert.That(snapshot.ContestationCalendarMonths)
            .IsEqualTo(fixture.Selected.Profiles[0].Definition.ContestationCalendarMonths);
        await Assert.That(snapshot.RetainUntil > fixture.Source.ValidUntil).IsTrue();
    }

    [Test]
    public async Task Snapshot_cannot_extend_retention_to_fit_the_protected_calendar_floor()
    {
        var fixture = Fixture();
        var months = fixture.Selected.Profiles[0].Definition.ContestationCalendarMonths;
        var retainUntil = Now.AddMonths(months).AddTicks(-1);
        var source = fixture.Source with { Approval = fixture.Source.Approval with { RetainUntil = retainUntil } };
        await Assert.That(Capture(fixture with { Source = source })).IsNull();
        await Assert.That(source.Approval.RetainUntil).IsEqualTo(retainUntil);
    }

    [Test]
    public async Task Retiring_activation_support_does_not_remove_a_qualified_transition_explanation()
    {
        var fixture = Fixture(supported: false);
        var snapshot = Capture(fixture);

        await Assert.That(snapshot).IsNotNull();
        await Assert.That(snapshot!.Effects.Single().Effect.Capability).IsEqualTo(ModerationCapability.Publish);
        await Assert.That(snapshot.Consequences.Single().Plan).IsEqualTo(fixture.Selected.Profiles[0].Consequences[0]);
        await Assert.That(fixture.Effect.CompletedAt is null).IsTrue();
    }

    [Test]
    public async Task Snapshot_retains_exact_compiled_plan_target_owner_interval_and_safe_meaning()
    {
        var fixture = Fixture();
        var snapshot = Capture(fixture)!;
        await Assert.That(snapshot.Consequences.Length).IsEqualTo(1);
        await Assert.That(snapshot.Consequences[0].Plan).IsEqualTo(
            fixture.Source.AdoptedProfile.Profiles[0].Consequences[0]);
        await Assert.That(snapshot.Consequences[0].Target.Source).IsEqualTo(fixture.Source.Targets[0].CurrentSource);
        await Assert.That(snapshot.Consequences[0].Target.TargetRevision).IsEqualTo(4L);
        await Assert.That(snapshot.Consequences[0].Interval.EndsAt).IsEqualTo(fixture.Decision.EndsAt);
        await Assert.That(snapshot.Rule.Revision).IsEqualTo(fixture.Decision.Rule.Revision);
        await Assert.That(snapshot.RuleMeaning).IsEqualTo(fixture.Source.Approval.RuleMeaning);
        await Assert.That(fixture.Effect.CompletedAt is null).IsTrue();
    }

    [Test]
    [Arguments("transaction")]
    [Arguments("approval_expired")]
    [Arguments("profile_source")]
    [Arguments("profile_generation")]
    [Arguments("profile_id")]
    [Arguments("facts_missing")]
    [Arguments("reason_meaning")]
    [Arguments("targets_missing")]
    [Arguments("target_source")]
    [Arguments("target_expired")]
    [Arguments("selection")]
    [Arguments("decision_revision")]
    [Arguments("reviewer_case")]
    [Arguments("reviewer_scope")]
    [Arguments("reviewer_source")]
    [Arguments("reviewer_expired")]
    [Arguments("reviewer_lifetime")]
    [Arguments("rule_meaning")]
    public async Task Missing_or_changed_qualified_sources_fail_closed(string change)
    {
        var fixture = Fixture();
        var source = fixture.Source;
        var target = source.Targets[0];
        var changed = change switch
        {
            "transaction" => source with { TransactionId = Id(90) },
            "approval_expired" => source with { Approval = source.Approval with { ValidUntil = Now } },
            "profile_source" => source with { CurrentProfileSource = source.CurrentProfileSource with { Revision = 2 } },
            "profile_generation" => source with
            { CurrentProfileSource = source.CurrentProfileSource with { WriterGeneration = "generation-2" } },
            "profile_id" => source with { Approval = source.Approval with { ProfileId = Id(91) } },
            "facts_missing" => source with { Approval = source.Approval with { Facts = [] } },
            "reason_meaning" => source with
            { Approval = source.Approval with { ReasonMeaning = new("Unapproved substituted meaning") } },
            "targets_missing" => source with { Targets = [] },
            "target_source" => source with
            { Targets = [target with { CurrentSource = target.CurrentSource with { Revision = 5 } }] },
            "target_expired" => source with { Targets = [target with { ValidUntil = Now }] },
            "selection" => source with
            {
                SelectedContribution = source.SelectedContribution with
                { Consequences = [new(null, target.Target with { TargetId = Id(92) })] }
            },
            "decision_revision" => source with
            { Approval = source.Approval with { DecisionRevision = fixture.Decision.Revision + 1 } },
            "reviewer_case" => source with
            { ReviewerQualification = source.ReviewerQualification with { CaseId = Id(93) } },
            "reviewer_scope" => source with
            { ReviewerQualification = source.ReviewerQualification with { Scope = fixture.Decision.Subject.Origin } },
            "reviewer_source" => source with
            {
                ReviewerQualification = source.ReviewerQualification with
                { CurrentSource = source.ReviewerQualification.CurrentSource with { Revision = 2 } }
            },
            "reviewer_expired" => source with
            { ReviewerQualification = source.ReviewerQualification with { ValidUntil = Now } },
            "reviewer_lifetime" => source with
            { ReviewerQualification = source.ReviewerQualification with { MaximumLifetime = TimeSpan.Zero } },
            "rule_meaning" => source with
            { Approval = source.Approval with { RuleMeaning = new("Unapproved structural rule meaning") } },
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        };
        await Assert.That(Capture(fixture with { Source = changed }) is null).IsTrue();
    }

    [Test]
    public async Task Same_publication_coordinates_do_not_authorize_substituted_adopted_meaning()
    {
        var fixture = Fixture();
        var selected = fixture.Selected;
        var proposed = selected.Proposed with
        {
            Classes = [selected.Proposed.Classes[0] with { Meaning = "Substituted published class meaning." }]
        };
        var registry = ModerationCapabilityRegistry.Parse(ModerationCapabilityRegistry.ReadInventory());
        var substituted = ModerationPolicyProposedStateCompiler.Compile(proposed,
            new(selected.Publication.HostBindingId, selected.Publication.AuthoritySourceId,
                selected.Publication.AuthorityRevision, selected.Publication.WriterGeneration,
                selected.Bounds, null), registry).Draft
            ?? throw new InvalidOperationException("The altered semantic fixture did not compile.");
        var source = fixture.Source with
        {
            AdoptedProfile = substituted,
            Approval = fixture.Source.Approval with { RuleMeaning = new(proposed.Classes[0].Meaning) }
        };
        await Assert.That(substituted.Publication).IsEqualTo(selected.Publication);
        await Assert.That(Capture(fixture with { Source = source }) is null).IsTrue();
    }

    [Test]
    public async Task Typed_snapshot_round_trip_preserves_all_consequence_dimensions()
    {
        var original = Capture(Fixture())!;
        var restored = JsonSerializer.Deserialize<ModerationNoticeSnapshot>(JsonSerializer.Serialize(original))!;
        await Assert.That(restored.Consequences[0]).IsEqualTo(original.Consequences[0]);
        await Assert.That(restored.SubjectOrigin).IsEqualTo(original.SubjectOrigin);
        await Assert.That(restored.RuleMeaning).IsEqualTo(original.RuleMeaning);
        await Assert.That(restored.Facts[0]).IsEqualTo(original.Facts[0]);
    }

    [Test]
    public async Task Later_decision_transition_does_not_rewrite_the_captured_explanation()
    {
        var fixture = Fixture();
        var original = Capture(fixture)!;
        fixture.Decision.RecordResponse(fixture.Decision.Revision, Now.AddMinutes(1));
        await Assert.That(original.DecisionState).IsEqualTo(ModerationDecisionState.Proposed);
        await Assert.That(original.DecisionRevision).IsEqualTo(1L);
        await Assert.That(Capture(fixture) is null).IsTrue();
    }

    private static ModerationNoticeSnapshot? Capture(NoticeFixture fixture) =>
        ModerationNoticeSnapshotFactory.Create(Id(1), fixture.Effect, fixture.Decision, null,
            fixture.Selected, fixture.Source, Now);

    private static NoticeFixture Fixture(bool supported = true)
    {
        var trust = AuthorityScope.TrustDomain(Id(10));
        var instance = AuthorityScope.Instance(AuthorityScope.Solution(trust, Id(11)), Id(12));
        var tenant = AuthorityScope.Tenant(instance, Id(13));
        var reference = ModerationReferenceCatalogue.GetProfile(ModerationReferenceProfileId.CFG06);
        var response = reference.Responses[0];
        var interval = response.Timing.Bind(Now);
        var registry = ModerationCapabilityRegistry.Parse(ModerationCapabilityRegistry.ReadInventory());
        var rule = ModerationRuleReference.Create(trust.Id, "publication.pause", 1);
        var reason = ModerationRuleReference.Create(trust.Id, "publication.abuse", 1);
        var classification = ModerationRuleReference.Create(trust.Id, "publication", 1);
        var definition = new ModerationPolicyProfileDefinition(Id(20), rule, reason, reference.Id,
            reference.Revision, response.Grade, [ModerationScopeKind.Tenant], [ModerationSubjectKind.Human],
            reference.Effects.ToImmutableArray(), reference.Bundle, reference.ProtectionPurpose, [],
            new(response.Timing.Kind, response.Timing.Duration), response.RequiredApprovals, 6, reference.Recognition);
        var proposed = new ModerationPolicyProposedState(Id(21), 0, Id(22), trust,
            ModerationReferenceCatalogue.Revision, registry.Revision, registry.Fingerprint,
            ModerationPolicyRequestedState.Draft, [ModerationNativeMode.Embedded],
            [new(classification, "Publication conduct", reference.Ground, reference.Basis)],
            [new(reason, "Confirmed publication abuse", classification)], [definition], supported ? [rule] : []);
        var context = new ModerationPolicyCompilationContext(Id(23), Id(24), 1, "profile-writer-1",
            new(trust, Enum.GetValues<ModerationScopeKind>().ToImmutableArray(),
                Enum.GetValues<ModerationSubjectKind>().ToImmutableArray(),
                reference.Effects.ToImmutableArray(), Enum.GetValues<ModerationReferenceTimingKind>().ToImmutableArray(),
                TimeSpan.FromDays(365), TimeSpan.FromDays(90), 0, 2, 6), null);
        var draft = ModerationPolicyProposedStateCompiler.Compile(proposed, context, registry).Draft
            ?? throw new InvalidOperationException("The native compiler rejected the semantic fixture.");
        var subject = SubjectReference.Create(ModerationSubjectKind.Human, Id(30), instance);
        var decision = ModerationDecision.Propose(Id(31), Id(32), subject,
            SubjectReference.Create(ModerationSubjectKind.Human, Id(33), instance), trust, tenant, rule,
            [ModerationCapability.Publish], Now, interval.EndsAt, interval.ReviewAt, response.RequiredApprovals, Now);
        var effect = ModerationEffectIntent.Create(Id(34), Id(35), decision.Id, decision.Revision,
            null, null, trust.Id, (int)tenant.Kind, tenant.Id, (int)subject.Kind, subject.Id, 1, 1, 1, Now, null, null);
        var target = new ModerationPolicyTarget(tenant, subject.Kind, subject.Id, Id(40), 4, "event.publish", null);
        var contribution = new ModerationPolicyContribution(Id(41), rule, tenant, subject.Kind, subject.Id, Now, null,
            definition.Effects.Select(item => new ModerationPolicyEffectTarget(item, target)).ToImmutableArray(),
            [new(null, target)]);
        var profileSource = new ModerationNoticeSourceStamp(proposed.PublicationId, 1, context.WriterGeneration);
        var targetSource = new ModerationNoticeSourceStamp(Id(43), 4, "publication-writer-1");
        var approval = new ModerationNoticeContentApproval(Id(44), 1, effect.Id, decision.Id, decision.Revision,
            subject, tenant, profileSource, definition.Id, contribution.Id,
            ModerationNoticeSnapshotFactory.SelectionDigest(contribution),
            new(proposed.Classes[0].Meaning), new(proposed.Reasons[0].Meaning),
            [new("The approved publication was reviewed against the recorded rule.")],
            new("Use the protected contestation and support routes."),
            new("Request independent review of this decision."),
            new("A human selected the response."), new("The adopted case retention policy applies."),
            Now, Now.AddHours(1), Now.AddYears(1));
        var reviewerSource = new ModerationNoticeSourceStamp(Id(45), 1, "reviewer-policy-v1");
        var reviewerQualification = new ModerationNoticeReviewerQualification(decision.CaseId,
            decision.Scope, reviewerSource, reviewerSource, TimeSpan.FromDays(30), Now, Now.AddHours(1));
        var observation = new ModerationNoticeSourceObservation(Id(1), Now, Now.AddHours(1),
            approval, profileSource, draft, contribution,
            [new(target, "event.publication", targetSource, targetSource, Now, Now.AddHours(1))],
            reviewerQualification);
        return new(effect, decision, draft, observation);
    }

    private sealed record NoticeFixture(ModerationEffectIntent Effect, ModerationDecision Decision,
        ModerationPolicyCompiledDraft Selected, ModerationNoticeSourceObservation Source);

    private static Guid Id(int value) => Guid.Parse($"019a0000-0000-7000-8000-{value:x12}");
}
