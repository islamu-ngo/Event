using System.Collections.Immutable;
using System.Text.Json;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;

namespace Event.Application.UnitTests.Moderation;

/// <summary>Metadata semantics only, not source authenticity, current read permission or delivery evidence.</summary>
public sealed class ModerationNoticeCorrectionCaptureTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments(ModerationNoticeAudience.Subject)]
    [Arguments(ModerationNoticeAudience.Reviewer)]
    [Arguments(ModerationNoticeAudience.ContentRepresentative)]
    public async Task Pending_occurrences_retain_recipient_identity_without_native_intents_or_delivery(
        ModerationNoticeAudience audience)
    {
        var fixture = Fixture(ModerationSubjectKind.Content);
        var source = ModerationNoticeSource.Capture(fixture.Effect, fixture.Pending.WriterTenantId,
            JsonSerializer.Serialize(fixture.Pending), fixture.Pending.RetainUntil);
        var recipient = fixture.Pending.PendingCorrection!.Recipients.Single(row => row.Audience == audience);
        var occurrence = ModerationNotice.CreatePendingCorrection(Id(60), source, audience,
            recipient.RecipientSubjectKind, recipient.RecipientSubjectId);

        await Assert.That(occurrence.EffectId).IsEqualTo(fixture.Effect.Id);
        await Assert.That(occurrence.Audience).IsEqualTo(recipient.Audience);
        await Assert.That(occurrence.RecipientSubjectKind).IsEqualTo(recipient.RecipientSubjectKind);
        await Assert.That(occurrence.RecipientSubjectId).IsEqualTo(recipient.RecipientSubjectId);
        await Assert.That(occurrence.DeliveryDisposition)
            .IsEqualTo(ModerationNoticeDeliveryDisposition.RecipientUnavailable);
        await Assert.That(occurrence.NativeIntentId).IsNull();
        await Assert.That(source.RetainUntil).IsEqualTo(fixture.Source.RetainUntil);
        await Assert.That(fixture.Effect.CompletedAt).IsNull();
    }

    [Test]
    [Arguments(ModerationNoticeAudience.Subject, 1, 7)]
    [Arguments(ModerationNoticeAudience.Subject, 4, 90)]
    [Arguments(ModerationNoticeAudience.Reviewer, 2, 21)]
    [Arguments(ModerationNoticeAudience.Reviewer, 1, 7)]
    [Arguments(ModerationNoticeAudience.Reviewer, 1, 90)]
    [Arguments(ModerationNoticeAudience.ContentRepresentative, 2, 33)]
    [Arguments(ModerationNoticeAudience.ContentRepresentative, 1, 90)]
    [Arguments((ModerationNoticeAudience)4, 1, 21)]
    public void Pending_occurrences_reject_tuples_not_in_the_retained_obligations(
        ModerationNoticeAudience audience, int kind, int subjectId)
    {
        var fixture = Fixture(ModerationSubjectKind.Content);
        var source = ModerationNoticeSource.Capture(fixture.Effect, fixture.Pending.WriterTenantId,
            JsonSerializer.Serialize(fixture.Pending), fixture.Pending.RetainUntil);

        Assert.Throws<ArgumentException>(() => ModerationNotice.CreatePendingCorrection(
            Id(60), source, audience, kind, Id(subjectId)));
    }

    [Test]
    [Arguments(ModerationSubjectKind.Human)]
    [Arguments(ModerationSubjectKind.Service)]
    [Arguments(ModerationSubjectKind.Organization)]
    [Arguments(ModerationSubjectKind.Content)]
    public async Task Pending_obligations_are_substantive_without_a_body_or_delivery_grant(ModerationSubjectKind kind)
    {
        var fixture = Fixture(kind);
        var capture = fixture.Pending;

        await Assert.That(capture.Matches(fixture.Effect)).IsTrue();
        await Assert.That(capture.HasNoSubstantiveWork).IsFalse();
        await Assert.That(capture.RetainUntil).IsEqualTo(fixture.Original.RetainUntil);
        await Assert.That(capture.Notice is null && capture.Finding is null).IsTrue();
        await Assert.That(capture.Reviewers.IsEmpty && capture.ContentRepresentatives.IsEmpty
            && capture.ReviewerPolicySource is null).IsTrue();
        await Assert.That(capture.PendingCorrection!.Recipients.All(row =>
            row.Disposition == ModerationNoticeDeliveryDisposition.RecipientUnavailable)).IsTrue();
        await Assert.That(fixture.Effect.CompletedAt is null).IsTrue();
    }

    [Test]
    public async Task Pending_metadata_round_trips_without_replacing_the_original_hashes_or_deadline()
    {
        var fixture = Fixture();
        var restored = JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(
            JsonSerializer.Serialize(fixture.Pending))!;

        await Assert.That(restored.Matches(fixture.Effect)).IsTrue();
        await Assert.That(restored.PendingCorrection!.OriginalSnapshotHash)
            .IsEqualTo(fixture.Source.SnapshotHash);
        await Assert.That(restored.PendingCorrection.OriginalEffectBindingHash)
            .IsEqualTo(fixture.Source.EffectBindingHash);
        await Assert.That(restored.PendingCorrection.Recipients.SequenceEqual(
            fixture.Pending.PendingCorrection!.Recipients)).IsTrue();
        await Assert.That(restored.RetainUntil).IsEqualTo(fixture.Source.RetainUntil);
        await Assert.That(restored.Notice is null && fixture.Effect.CompletedAt is null).IsTrue();
    }

    [Test]
    [Arguments("version")]
    [Arguments("generation")]
    [Arguments("host_binding")]
    [Arguments("host_solution")]
    [Arguments("host_instance")]
    [Arguments("writer_tenant")]
    [Arguments("authority_source")]
    [Arguments("qualification_revision")]
    [Arguments("decision_id")]
    [Arguments("decision_revision")]
    [Arguments("scope_kind")]
    [Arguments("scope_id")]
    [Arguments("subject_kind")]
    [Arguments("subject_id")]
    [Arguments("sanction_id")]
    [Arguments("superseded_sanction_id")]
    [Arguments("finding")]
    [Arguments("notice")]
    [Arguments("reviewers_default")]
    [Arguments("reviewers_present")]
    [Arguments("representatives_default")]
    [Arguments("representatives_present")]
    [Arguments("reviewer_policy")]
    [Arguments("deadline_missing")]
    [Arguments("deadline_changed")]
    [Arguments("deadline_offset")]
    public async Task Pending_shape_preserves_base_coordinates_and_cannot_borrow_disclosure_proofs(string change)
    {
        var fixture = Fixture();
        var capture = fixture.Pending;
        var decision = capture.Decision;
        var changed = change switch
        {
            "version" => capture with { Version = 1 },
            "generation" => capture with { WriterGeneration = "replacement" },
            "host_binding" => capture with { HostBindingId = Guid.Empty },
            "host_solution" => capture with { HostSolutionId = Guid.Empty },
            "host_instance" => capture with { HostInstanceId = Guid.Empty },
            "writer_tenant" => capture with { WriterTenantId = Guid.Empty },
            "authority_source" => capture with { AuthoritySourceId = Guid.Empty },
            "qualification_revision" => capture with { QualificationRevision = 2 },
            "decision_id" => capture with { Decision = decision with { Id = Id(90) } },
            "decision_revision" => capture with { Decision = decision with { Revision = decision.Revision + 1 } },
            "scope_kind" => capture with { Decision = decision with { Scope = decision.Scope with
                { Kind = ModerationScopeKind.Instance } } },
            "scope_id" => capture with { Decision = decision with { Scope = decision.Scope with { Id = Id(90) } } },
            "subject_kind" => capture with { Decision = decision with { SubjectKind = ModerationSubjectKind.Service } },
            "subject_id" => capture with { Decision = decision with { SubjectId = Id(90) } },
            "sanction_id" => capture with { Decision = decision with { SanctionId = Id(90) } },
            "superseded_sanction_id" => capture with { Decision = decision with { SupersededSanctionId = Id(90) } },
            "finding" => capture with { Finding = fixture.Original.Finding },
            "notice" => capture with { Notice = fixture.Original.Notice },
            "reviewers_default" => capture with { Reviewers = default },
            "reviewers_present" => capture with { Reviewers = fixture.Original.Reviewers },
            "representatives_default" => capture with { ContentRepresentatives = default },
            "representatives_present" => capture with { ContentRepresentatives = [new(Id(90), 1, Id(91))] },
            "reviewer_policy" => capture with { ReviewerPolicySource = fixture.Original.ReviewerPolicySource },
            "deadline_missing" => capture with { ValidUntil = null },
            "deadline_changed" => capture with { ValidUntil = capture.ValidUntil!.Value.AddTicks(1) },
            "deadline_offset" => capture with { ValidUntil = capture.ValidUntil!.Value.ToOffset(TimeSpan.FromHours(1)) },
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        };

        await Assert.That(changed.Matches(fixture.Effect)).IsFalse();
        await Assert.That(changed.HasNoSubstantiveWork).IsFalse();
    }

    [Test]
    [Arguments("effect_empty")]
    [Arguments("effect_same")]
    [Arguments("effect_v4")]
    [Arguments("effect_variant")]
    [Arguments("snapshot_hash_missing")]
    [Arguments("snapshot_hash_short")]
    [Arguments("snapshot_hash_uppercase")]
    [Arguments("snapshot_hash_nonhex")]
    [Arguments("binding_hash_missing")]
    [Arguments("binding_hash_long")]
    [Arguments("binding_hash_uppercase")]
    [Arguments("binding_hash_nonhex")]
    [Arguments("retention_expired")]
    [Arguments("retention_offset")]
    [Arguments("retention_unbounded")]
    public async Task Invalid_original_references_hash_formats_and_retention_fail_closed(string change)
    {
        var fixture = Fixture();
        var pending = fixture.Pending.PendingCorrection!;
        var changed = change switch
        {
            "effect_empty" => pending with { OriginalEffectId = Guid.Empty },
            "effect_same" => pending with { OriginalEffectId = fixture.Effect.Id },
            "effect_v4" => pending with { OriginalEffectId = Guid.Parse("019a0000-0000-4000-8000-000000000090") },
            "effect_variant" => pending with { OriginalEffectId = Guid.Parse("019a0000-0000-7000-0000-000000000090") },
            "snapshot_hash_missing" => pending with { OriginalSnapshotHash = null! },
            "snapshot_hash_short" => pending with { OriginalSnapshotHash = new string('a', 63) },
            "snapshot_hash_uppercase" => pending with { OriginalSnapshotHash = new string('A', 64) },
            "snapshot_hash_nonhex" => pending with { OriginalSnapshotHash = new string('g', 64) },
            "binding_hash_missing" => pending with { OriginalEffectBindingHash = null! },
            "binding_hash_long" => pending with { OriginalEffectBindingHash = new string('a', 65) },
            "binding_hash_uppercase" => pending with { OriginalEffectBindingHash = new string('F', 64) },
            "binding_hash_nonhex" => pending with { OriginalEffectBindingHash = new string(' ', 64) },
            "retention_expired" => pending with { RetainUntil = fixture.Effect.CreatedAt },
            "retention_offset" => pending with { RetainUntil = pending.RetainUntil.ToOffset(TimeSpan.FromHours(1)) },
            "retention_unbounded" => pending with { RetainUntil = DateTimeOffset.MaxValue },
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        };
        var capture = fixture.Pending with { PendingCorrection = changed, ValidUntil = changed.RetainUntil };

        await Assert.That(capture.Matches(fixture.Effect)).IsFalse();
        await Assert.That(capture.HasNoSubstantiveWork).IsFalse();
    }

    [Test]
    [Arguments("default")]
    [Arguments("empty")]
    [Arguments("null_row")]
    [Arguments("duplicate_notice")]
    [Arguments("duplicate_identity")]
    [Arguments("missing_subject")]
    [Arguments("additional_subject")]
    [Arguments("notice_empty")]
    [Arguments("notice_v4")]
    [Arguments("notice_variant")]
    [Arguments("audience")]
    [Arguments("kind_low")]
    [Arguments("kind_high")]
    [Arguments("subject_empty")]
    [Arguments("subject_replaced")]
    [Arguments("subject_kind_replaced")]
    [Arguments("reviewer_not_human")]
    [Arguments("reviewer_is_subject")]
    [Arguments("representative_not_human")]
    public async Task Pending_recipients_require_one_exact_subject_and_unique_valid_original_occurrences(string change)
    {
        var fixture = Fixture();
        var pending = fixture.Pending.PendingCorrection!;
        var subject = pending.Recipients[0];
        var reviewer = pending.Recipients[1];
        var representative = pending.Recipients[2];
        ImmutableArray<ModerationNoticeCorrectionRecipient> recipients = change switch
        {
            "default" => default,
            "empty" => [],
            "null_row" => [subject, null!],
            "duplicate_notice" => [subject, reviewer with { OriginalNoticeId = subject.OriginalNoticeId }],
            "duplicate_identity" => [subject, reviewer, reviewer with { OriginalNoticeId = Id(90) }],
            "missing_subject" => [reviewer, representative],
            "additional_subject" => [subject, subject with { OriginalNoticeId = Id(90) }],
            "notice_empty" => [subject with { OriginalNoticeId = Guid.Empty }],
            "notice_v4" => [subject with { OriginalNoticeId = Guid.Parse("019a0000-0000-4000-8000-000000000090") }],
            "notice_variant" => [subject with { OriginalNoticeId = Guid.Parse("019a0000-0000-7000-0000-000000000090") }],
            "audience" => [subject, reviewer with { Audience = (ModerationNoticeAudience)4 }],
            "kind_low" => [subject, reviewer with { RecipientSubjectKind = 0 }],
            "kind_high" => [subject, reviewer with { RecipientSubjectKind = 5 }],
            "subject_empty" => [subject with { RecipientSubjectId = Guid.Empty }],
            "subject_replaced" => [subject with { RecipientSubjectId = Id(90) }],
            "subject_kind_replaced" => [subject with { RecipientSubjectKind = 2 }],
            "reviewer_not_human" => [subject, reviewer with { RecipientSubjectKind = 2 }],
            "reviewer_is_subject" => [subject, reviewer with { RecipientSubjectId = subject.RecipientSubjectId }],
            "representative_not_human" => [subject, representative with { RecipientSubjectKind = 4 }],
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        };

        await Assert.That((fixture.Pending with { PendingCorrection = pending with { Recipients = recipients } })
            .Matches(fixture.Effect)).IsFalse();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(7)]
    public async Task Pending_disposition_cannot_claim_routing_native_intents_or_delivery(int disposition)
    {
        var fixture = Fixture();
        var pending = fixture.Pending.PendingCorrection!;
        foreach (var recipient in pending.Recipients)
        {
            var recipients = pending.Recipients.Replace(recipient,
                recipient with { Disposition = (ModerationNoticeDeliveryDisposition)disposition });
            await Assert.That((fixture.Pending with { PendingCorrection = pending with { Recipients = recipients } })
                .Matches(fixture.Effect)).IsFalse();
        }
    }

    [Test]
    [Arguments(ModerationOperationKind.RecordResponse)]
    [Arguments(ModerationOperationKind.AcceptFinding)]
    [Arguments(ModerationOperationKind.Approve)]
    [Arguments(ModerationOperationKind.Activate)]
    [Arguments(ModerationOperationKind.Reject)]
    [Arguments(ModerationOperationKind.Withdraw)]
    [Arguments(ModerationOperationKind.Propose)]
    [Arguments(ModerationOperationKind.Reconcile)]
    [Arguments(ModerationOperationKind.Supersede)]
    public async Task Only_revoke_can_carry_pending_correction_obligations(ModerationOperationKind operation)
    {
        var fixture = Fixture();
        var effect = Effect(fixture.Pending.Decision, fixture.Effect.SanctionId,
            fixture.Effect.SanctionRevision, operation);
        var capture = fixture.Pending with { Decision = fixture.Pending.Decision with
            { SupersededSanctionId = effect.SupersededSanctionId } };

        await Assert.That(capture.Matches(effect)).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Revoke_requires_an_actual_sanction_and_a_post_creation_revision(bool hasSanction)
    {
        var fixture = Fixture();
        var sanctionId = hasSanction ? fixture.Effect.SanctionId : null;
        var effect = Effect(fixture.Pending.Decision, sanctionId, hasSanction ? 1 : null);
        var capture = fixture.Pending with { Decision = fixture.Pending.Decision with { SanctionId = sanctionId } };

        await Assert.That(capture.Matches(effect)).IsFalse();
        await Assert.That(capture.HasNoSubstantiveWork).IsFalse();
    }

    [Test]
    public async Task Subject_only_and_distinct_audiences_for_the_same_human_are_valid_metadata()
    {
        var fixture = Fixture();
        var pending = fixture.Pending.PendingCorrection!;
        var subjectOnly = fixture.Pending with { PendingCorrection = pending with { Recipients = [pending.Recipients[0]] } };
        var sharedHuman = fixture.Pending with { PendingCorrection = pending with { Recipients =
            [pending.Recipients[0], pending.Recipients[1], pending.Recipients[2] with
                { RecipientSubjectId = pending.Recipients[1].RecipientSubjectId }] } };

        await Assert.That(subjectOnly.Matches(fixture.Effect)).IsTrue();
        await Assert.That(sharedHuman.Matches(fixture.Effect)).IsTrue();
    }

    [Test]
    public async Task Ordinary_inert_capture_stays_inert_but_cannot_hide_pending_obligations()
    {
        var fixture = Fixture();
        var decision = Decision(ModerationSubjectKind.Human);
        var capture = Capture(decision, null);
        var effect = Effect(capture.Decision, null, null, ModerationOperationKind.Propose);

        await Assert.That(capture.HasNoSubstantiveWork && capture.Matches(effect)).IsTrue();
        await Assert.That(capture.RetainUntil).IsNull();
        var changed = capture with { PendingCorrection = fixture.Pending.PendingCorrection };
        await Assert.That(changed.HasNoSubstantiveWork).IsFalse();
        await Assert.That(changed.Matches(effect)).IsFalse();
        await Assert.That((fixture.Pending with { PendingCorrection = null }).Matches(fixture.Effect)).IsFalse();
    }

    [Test]
    public async Task Ordinary_substantive_capture_stays_valid_but_cannot_also_carry_pending_obligations()
    {
        var fixture = Fixture();
        await Assert.That(fixture.Original.Matches(fixture.OriginalEffect)).IsTrue();
        await Assert.That(fixture.Original.HasNoSubstantiveWork).IsFalse();
        await Assert.That(fixture.Original.RetainUntil).IsEqualTo(fixture.Original.Notice!.RetainUntil);
        var changed = fixture.Original with { PendingCorrection = fixture.Pending.PendingCorrection! with
            { RetainUntil = Now.AddYears(2) } };

        await Assert.That(changed.Matches(fixture.OriginalEffect)).IsFalse();
        await Assert.That(changed.RetainUntil).IsEqualTo(fixture.Original.Notice.RetainUntil);
    }

    [Test]
    public async Task Diagnostic_strings_do_not_disclose_original_coordinates_or_hashes()
    {
        var fixture = Fixture();
        await Assert.That(fixture.Pending.ToString()).IsEqualTo(nameof(ModerationNoticeCaptureSnapshot));
        await Assert.That(fixture.Pending.PendingCorrection!.ToString())
            .IsEqualTo(nameof(ModerationNoticeCorrectionObligations));
        await Assert.That(fixture.Pending.PendingCorrection.Recipients[0].ToString())
            .IsEqualTo(nameof(ModerationNoticeCorrectionRecipient));
    }

    private static CorrectionFixture Fixture(ModerationSubjectKind kind = ModerationSubjectKind.Human)
    {
        var decision = Decision(kind);
        decision.RecordResponse(decision.Revision, Now);
        decision.AcceptFinding(Id(20), decision.Revision, Now);
        decision.Approve(SubjectReference.Create(ModerationSubjectKind.Human, Id(21), decision.Subject.Origin),
            decision.Scope, decision.Revision, Now);
        var sanction = decision.Activate(Id(22), decision.Revision, Now);
        var originalEffect = Effect(OriginalDecision(decision, sanction.Id), sanction.Id, sanction.Revision,
            ModerationOperationKind.Activate, Id(23), Now);
        var original = OrdinaryCapture(decision, sanction, originalEffect);
        var source = ModerationNoticeSource.Capture(originalEffect, Id(4),
            JsonSerializer.Serialize(original), original.RetainUntil);
        sanction.Revoke(sanction.Revision, Now.AddMinutes(1));
        var effect = Effect(OriginalDecision(decision, sanction.Id), sanction.Id, sanction.Revision);
        var pending = Capture(decision, sanction.Id) with
        {
            ValidUntil = source.RetainUntil,
            PendingCorrection = new(originalEffect.Id, source.SnapshotHash, source.EffectBindingHash,
                source.RetainUntil!.Value,
                [new(Id(30), ModerationNoticeAudience.Subject, effect.SubjectKind, effect.SubjectId,
                    ModerationNoticeDeliveryDisposition.RecipientUnavailable),
                 new(Id(31), ModerationNoticeAudience.Reviewer, 1, Id(21),
                    ModerationNoticeDeliveryDisposition.RecipientUnavailable),
                 new(Id(32), ModerationNoticeAudience.ContentRepresentative, 1, Id(33),
                    ModerationNoticeDeliveryDisposition.RecipientUnavailable)])
        };
        return new(effect, pending, originalEffect, original, source);
    }

    private static ModerationDecision Decision(ModerationSubjectKind kind)
    {
        var trust = AuthorityScope.TrustDomain(Id(1));
        var instance = AuthorityScope.Instance(AuthorityScope.Solution(trust, Id(2)), Id(3));
        return ModerationDecision.Propose(Id(5), Id(6), SubjectReference.Create(kind, Id(7), instance),
            SubjectReference.Create(ModerationSubjectKind.Human, Id(8), instance), instance,
            AuthorityScope.Tenant(instance, Id(4)), ModerationRuleReference.Create(trust.Id, "publication.pause", 1),
            [ModerationCapability.Publish], Now, Now.AddDays(1), null, 1, Now);
    }

    private static ModerationNoticeOriginalDecision OriginalDecision(ModerationDecision decision, Guid? sanctionId) =>
        new(decision.Id, decision.Revision, decision.CaseId, decision.State,
            ModerationNoticeScope.From(decision.Scope), decision.SubjectKind, decision.SubjectId,
            ModerationNoticeScope.From(decision.Subject.Origin),
            new(decision.Rule.OwnerId, decision.Rule.Code, decision.Rule.Revision), decision.CapabilityMask,
            decision.StartsAt, decision.EndsAt, decision.ReviewAt, decision.FindingId,
            decision.Approvals.Count, sanctionId, null);

    private static ModerationNoticeCaptureSnapshot Capture(ModerationDecision decision, Guid? sanctionId) =>
        new(2, "event.moderation.capture.v2", Id(9), Id(2), Id(3), Id(4), Id(10), 1,
            OriginalDecision(decision, sanctionId), null, null, [], null);

    private static ModerationEffectIntent Effect(ModerationNoticeOriginalDecision decision, Guid? sanctionId,
        long? sanctionRevision, ModerationOperationKind operation = ModerationOperationKind.Revoke,
        Guid? id = null, DateTimeOffset? at = null) =>
        ModerationEffectIntent.Create(id ?? Id(24), Id(25), decision.Id, decision.Revision, sanctionId,
            sanctionRevision, Id(1), (int)decision.Scope.Kind, decision.Scope.Id, (int)decision.SubjectKind,
            decision.SubjectId, (int)operation, 1, 1, at ?? Now.AddMinutes(1),
            operation == ModerationOperationKind.Supersede ? Id(26) : null,
            operation == ModerationOperationKind.Supersede ? 2 : null);

    private static ModerationNoticeCaptureSnapshot OrdinaryCapture(ModerationDecision decision,
        ModerationSanction sanction, ModerationEffectIntent effect)
    {
        var capture = Capture(decision, sanction.Id);
        var original = capture.Decision;
        var stamp = new ModerationNoticeSourceStamp(Id(40), 1, "semantic-test-source");
        var target = new ModerationNoticeTarget(original.Scope, original.SubjectKind, original.SubjectId,
            Id(41), 1, "event.publish", null, "event.publication", stamp);
        var reference = ModerationReferenceCatalogue.GetProfile(ModerationReferenceProfileId.CFG06);
        var interval = new ModerationReferenceInterval(ModerationReferenceTimingKind.Finite,
            Now, Now.AddDays(1), null, null);
        // Ordinary-shape fixture only; these values are not a native disclosure approval.
        var safe = new ModerationNoticeSafeText("Original independently approved explanation.");
        var notice = new ModerationNoticeSnapshot(1, effect.Id, effect.OperationId, decision.Id,
            decision.Revision, decision.State, sanction.Id, sanction.Revision, sanction.State, null, null,
            effect.OperationKind, original.Scope, original.SubjectKind, original.SubjectId, original.SubjectOrigin,
            Id(42), 1, Id(43), 1, Id(44), reference.Id, reference.Revision, stamp, Id(45), 1, Id(46),
            original.Rule, original.Rule, safe, safe, [safe], safe, safe, safe, safe,
            [new(reference.Effects[0], target, interval)],
            [new(null, null, target, interval,
                ModerationReferenceCatalogue.GetBundle(reference.Bundle).Resolve(reference.ProtectionPurpose))],
            Now, Now.AddMonths(7), 6);
        return capture with
        {
            Finding = new(decision.FindingId!.Value, 1, ModerationFindingState.Final, original.Rule),
            Notice = notice,
            Reviewers = [new(Id(47), 1, Id(21), Id(21), Id(48), 1, Id(49), Id(50), 1, Id(49), Now.AddMinutes(30))],
            ValidUntil = Now.AddMinutes(1),
            ReviewerPolicySource = stamp,
            ContentRepresentatives = decision.SubjectKind == ModerationSubjectKind.Content ? [new(Id(51), 1, Id(33))] : []
        };
    }

    private sealed record CorrectionFixture(ModerationEffectIntent Effect, ModerationNoticeCaptureSnapshot Pending,
        ModerationEffectIntent OriginalEffect, ModerationNoticeCaptureSnapshot Original, ModerationNoticeSource Source);

    private static Guid Id(int value) => Guid.Parse($"019a0000-0000-7000-8000-{value:x12}");
}
