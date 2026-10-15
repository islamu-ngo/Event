using Explore.Domain;
using Explore.Domain.Enums;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;

namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationRecognitionPendingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Received_request_is_not_a_computed_receipt_or_settlement()
    {
        var pending = Pending();
        await Assert.That(pending.State).IsEqualTo(ModerationRecognitionPendingState.Received);
        await Assert.That(pending.HasCompleteMaterial).IsFalse();
        await Assert.That(() => pending.BindAuthority(Fact(pending), Now)).Throws<InvalidOperationException>();
        await Assert.That(pending.AuthoritySequence).IsNull();
    }

    [Test]
    public async Task EventCaptureRetainsExactJurisdictionAndReceiptsItsEventIdentifier()
    {
        var pending = Pending();
        var (decision, sanction) = Final(pending.SubjectId, eventScope: true);
        var item = Item(pending, decision, sanction, Guid.CreateVersion7(), false);
        pending.Capture([item], Now);
        await Assert.That(item.Scope).IsEqualTo(decision.Scope);
        await Assert.That(item.ScopeEventId).IsEqualTo(decision.Scope.Id);
        await Assert.That(item.ScopeResourceId).IsNull();
        await Assert.That(item.MatchesCurrent(decision, sanction, Now)).IsTrue();
        var originalReceipt = item.ComputeMaterialReceipt();

        typeof(ModerationRecognitionCaptureItem).GetProperty(nameof(item.ScopeEventId))!
            .SetValue(item, Guid.CreateVersion7());

        await Assert.That(item.ComputeMaterialReceipt()).IsNotEqualTo(originalReceipt);
        await Assert.That(pending.HasCompleteMaterial).IsFalse();
        await Assert.That(item.MatchesCurrent(decision, sanction, Now)).IsFalse();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task Deadline_is_finite_original_and_cannot_be_extended(int defect)
    {
        await Assert.That(() => ModerationRecognitionPending.Receive(
            Guid.CreateVersion7(), Guid.CreateVersion7(), 1, PrivacyErasureReasonCode.AccountDeletion,
            Guid.CreateVersion7(), Guid.CreateVersion7(), Now,
            defect == 0 ? Now : defect == 1 ? DateTimeOffset.MaxValue : Now.AddHours(1),
            defect == 2 ? Now.AddHours(2) : Now.AddMinutes(5), Hash(), Now.AddDays(1)))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Missing_computed_material_escalates_at_exact_owned_deadline_without_completion()
    {
        var pending = Pending();
        pending.ObserveFailure(ModerationRecognitionPendingFailure.KeyUnavailable, Now.AddMinutes(5));
        await Assert.That(pending.State).IsEqualTo(ModerationRecognitionPendingState.EscalationRequired);
        await Assert.That(pending.SettleBy).IsEqualTo(Now.AddHours(1));
        await Assert.That(pending.AuthoritySequence).IsNull();
        pending.ObserveFailure(ModerationRecognitionPendingFailure.StoreUnavailable, Now.AddHours(1));
        await Assert.That(pending.State).IsEqualTo(ModerationRecognitionPendingState.LawfulDispositionRequired);
        await Assert.That(pending.HasCompleteMaterial).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Deadline_observation_advances_unsettled_requests_even_with_complete_material(bool computed)
    {
        var pending = Pending();
        if (computed)
        {
            var (decision, sanction) = Final(pending.SubjectId);
            pending.Capture([Item(pending, decision, sanction, Guid.CreateVersion7(), false)], Now);
        }
        var receipt = pending.MaterialReceipt;
        pending.AdvanceDeadline(pending.EscalateAt);
        await Assert.That(pending.State).IsEqualTo(ModerationRecognitionPendingState.EscalationRequired);
        await Assert.That(pending.Failure).IsEqualTo(ModerationRecognitionPendingFailure.None);
        pending.AdvanceDeadline(pending.SettleBy);
        await Assert.That(pending.State).IsEqualTo(ModerationRecognitionPendingState.LawfulDispositionRequired);
        await Assert.That(pending.MaterialReceipt).IsEqualTo(receipt);
        await Assert.That(pending.AuthoritySequence).IsNull();
        await Assert.That(pending.HasCompleteMaterial).IsEqualTo(computed);
    }

    [Test]
    public async Task Retry_or_earlier_clock_cannot_downgrade_or_extend_a_due_obligation()
    {
        var pending = Pending();
        pending.AdvanceDeadline(pending.SettleBy);
        var revision = pending.Revision;
        pending.AdvanceDeadline(pending.SettleBy);
        await Assert.That(pending.Revision).IsEqualTo(revision);
        pending.AdvanceDeadline(Now);
        await Assert.That(pending.Revision).IsEqualTo(revision);
        pending.ObserveFailure(ModerationRecognitionPendingFailure.KeyUnavailable, Now);
        await Assert.That(pending.State).IsEqualTo(ModerationRecognitionPendingState.LawfulDispositionRequired);
        await Assert.That(pending.SettleBy).IsEqualTo(Now.AddHours(1));
    }

    [Test]
    public async Task Capture_after_escalation_preserves_the_unsettled_owner_obligation()
    {
        var pending = Pending();
        var (decision, sanction) = Final(pending.SubjectId);
        pending.ObserveFailure(ModerationRecognitionPendingFailure.KeyUnavailable, pending.EscalateAt);
        pending.Capture([Item(pending, decision, sanction, Guid.CreateVersion7(), false,
            capturedAt: pending.EscalateAt)], pending.EscalateAt);
        await Assert.That(pending.HasCompleteMaterial).IsTrue();
        await Assert.That(pending.State).IsEqualTo(ModerationRecognitionPendingState.EscalationRequired);
        await Assert.That(pending.AuthoritySequence).IsNull();
        await Assert.That(pending.SettleBy).IsEqualTo(Now.AddHours(1));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Receipt_status_observes_elapsed_obligations_without_replay_or_a_write(bool computed)
    {
        var pending = Pending();
        if (computed)
        {
            var (decision, sanction) = Final(pending.SubjectId);
            pending.Capture([Item(pending, decision, sanction, Guid.CreateVersion7(), false)], Now);
        }
        var state = pending.State;
        var revision = pending.Revision;
        var receipt = pending.MaterialReceipt;
        await Assert.That(pending.StateAt(pending.EscalateAt)).IsEqualTo(ModerationRecognitionPendingState.EscalationRequired);
        await Assert.That(pending.StateAt(pending.SettleBy)).IsEqualTo(ModerationRecognitionPendingState.LawfulDispositionRequired);
        await Assert.That(pending.State).IsEqualTo(state);
        await Assert.That(pending.Revision).IsEqualTo(revision);
        await Assert.That(pending.MaterialReceipt).IsEqualTo(receipt);
        await Assert.That(pending.AuthoritySequence).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Settled_authority_cannot_regress_to_an_unsettled_failure(bool noLongerRequired)
    {
        var pending = Pending();
        if (noLongerRequired) pending.BindNoLongerRequired(Fact(pending), Now);
        else
        {
            var (decision, sanction) = Final(pending.SubjectId);
            pending.Capture([Item(pending, decision, sanction, Guid.CreateVersion7(), false)], Now);
            pending.BindAuthority(Fact(pending), Now);
        }
        var state = pending.State;
        var revision = pending.Revision;
        pending.AdvanceDeadline(pending.SettleBy);
        await Assert.That(pending.State).IsEqualTo(state);
        await Assert.That(pending.Revision).IsEqualTo(revision);
        await Assert.That(() => pending.ObserveFailure(
            ModerationRecognitionPendingFailure.StoreUnavailable, pending.SettleBy)).Throws<InvalidOperationException>();
        await Assert.That(pending.AuthoritySequence).IsEqualTo(1L);
        await Assert.That(pending.Revision).IsEqualTo(revision);
    }

    [Test]
    public async Task Original_scope_interval_revisions_and_two_purposes_survive_capture()
    {
        var pending = Pending();
        var (decision, sanction) = Final(pending.SubjectId);
        var source = Guid.CreateVersion7();
        var tag = Convert.ToHexStringLower(Hash());
        var items = new[] { Item(pending, decision, sanction, source, false, tag), Item(pending, decision, sanction, source, true, tag) };
        pending.Capture(items, Now);
        await Assert.That(pending.Material.Count).IsEqualTo(2);
        await Assert.That(pending.HasCompleteMaterial).IsTrue();
        await Assert.That(items[0].Scope).IsEqualTo(sanction.Scope);
        await Assert.That(items[0].EndsAt).IsEqualTo(Now.AddDays(2));
        await Assert.That(items[0].SanctionRevision).IsEqualTo(sanction.Revision);
        await Assert.That(items[0].DecisionRevision).IsEqualTo(decision.Revision);
        await Assert.That(items[0].Purpose).IsNotEqualTo(items[1].Purpose);
        await Assert.That(items[0].MatchesCurrent(decision, sanction, Now)).IsTrue();
        pending.BindAuthority(Fact(pending), Now);
        await Assert.That(pending.AuthoritySequence).IsEqualTo(1L);
        await Assert.That(pending.State).IsEqualTo(ModerationRecognitionPendingState.AuthorityBound);
    }

    [Test]
    [Arguments("empty")]
    [Arguments("duplicate")]
    [Arguments("subject")]
    [Arguments("operation")]
    public async Task Incomplete_or_transplanted_material_never_receipts_a_request(string defect)
    {
        var pending = Pending();
        var (decision, sanction) = Final(defect == "subject" ? Guid.CreateVersion7() : pending.SubjectId);
        var item = Item(defect == "operation" ? Pending() : pending, decision, sanction, Guid.CreateVersion7(), false);
        await Assert.That(() => pending.Capture(
            defect == "empty" ? [] : defect == "duplicate" ? [item, item] : [item], Now))
            .Throws<ArgumentException>();
        await Assert.That(pending.HasCompleteMaterial).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Reversal_or_exact_expiry_drops_only_original_eligibility(bool reversed)
    {
        var pending = Pending();
        var (decision, sanction) = Final(pending.SubjectId);
        var item = Item(pending, decision, sanction, Guid.CreateVersion7(), false);
        pending.Capture([item], Now);
        var observedAt = reversed ? Now.AddMinutes(1) : Now.AddDays(2);
        if (reversed) sanction.Revoke(sanction.Revision, observedAt);
        await Assert.That(item.MatchesCurrent(decision, sanction, observedAt)).IsFalse();
        item.Drop(observedAt);
        var receipt = pending.MaterialReceipt;
        item.Drop(observedAt.AddTicks(1));
        await Assert.That(item.DroppedAt).IsEqualTo(observedAt);
        await Assert.That(pending.MaterialReceipt).IsEqualTo(receipt);
        await Assert.That(pending.HasCompleteMaterial).IsTrue();
        await Assert.That(item.EndsAt).IsEqualTo(Now.AddDays(2));
    }

    [Test]
    [Arguments("subject")]
    [Arguments("intent")]
    [Arguments("policy")]
    public async Task Authority_binding_rejects_a_different_request(string defect)
    {
        var pending = Pending();
        var (decision, sanction) = Final(pending.SubjectId);
        pending.Capture([Item(pending, decision, sanction, Guid.CreateVersion7(), false)], Now);
        var fact = PrivacyErasureIntent.Record(
            defect == "intent" ? Guid.CreateVersion7() : pending.IntentId, 1, PrivacyErasureSubjectKind.User,
            defect == "subject" ? Guid.CreateVersion7() : pending.SubjectId,
            pending.ReasonCode, defect == "policy" ? 2 : 1,
            Now.UtcDateTime, Now.UtcDateTime, Now.AddDays(30).UtcDateTime);
        await Assert.That(() => pending.BindAuthority(fact, Now)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Distinct_contributions_cannot_share_one_persisted_item_identity()
    {
        var pending = Pending();
        var (decision, sanction) = Final(pending.SubjectId);
        var source = Guid.CreateVersion7();
        var tag = Convert.ToHexStringLower(Hash());
        var action = Item(pending, decision, sanction, source, false, tag);
        var enrollment = Item(pending, decision, sanction, source, true, tag);
        typeof(ModerationRecognitionCaptureItem).GetProperty(nameof(enrollment.Id))!.SetValue(enrollment, action.Id);

        await Assert.That(() => pending.Capture([action, enrollment], Now)).Throws<ArgumentException>();
        await Assert.That(pending.Material.Count).IsEqualTo(0);
        await Assert.That(pending.MaterialReceipt).IsNull();
        await Assert.That(pending.Revision).IsEqualTo(1L);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Authority_cannot_bind_a_fact_not_yet_recorded_at_the_observation(bool empty)
    {
        var pending = Pending();
        if (!empty)
        {
            var (decision, sanction) = Final(pending.SubjectId);
            pending.Capture([Item(pending, decision, sanction, Guid.CreateVersion7(), false)], Now);
        }
        var state = pending.State;
        var revision = pending.Revision;
        var fact = PrivacyErasureIntent.Record(pending.IntentId, 1, PrivacyErasureSubjectKind.User,
            pending.SubjectId, pending.ReasonCode, pending.PolicyVersion,
            Now.UtcDateTime, Now.AddTicks(1).UtcDateTime, Now.AddDays(30).UtcDateTime);

        await Assert.That(() =>
        {
            if (empty) pending.BindNoLongerRequired(fact, Now);
            else pending.BindAuthority(fact, Now);
        }).Throws<InvalidOperationException>();
        await Assert.That(pending.AuthoritySequence).IsNull();
        await Assert.That(pending.State).IsEqualTo(state);
        await Assert.That(pending.Revision).IsEqualTo(revision);
    }

    [Test]
    public async Task Empty_disposition_cannot_bind_authority_recorded_before_the_received_request()
    {
        var pending = Pending();
        var fact = PrivacyErasureIntent.Record(pending.IntentId, 1, PrivacyErasureSubjectKind.User,
            pending.SubjectId, pending.ReasonCode, pending.PolicyVersion,
            Now.AddTicks(-1).UtcDateTime, Now.AddTicks(-1).UtcDateTime, Now.AddDays(30).UtcDateTime);
        await Assert.That(() => pending.BindNoLongerRequired(fact, Now)).Throws<InvalidOperationException>();
        await Assert.That(pending.AuthoritySequence).IsNull();
        await Assert.That(pending.Revision).IsEqualTo(1L);
    }

    [Test]
    public async Task Repeating_the_same_empty_settlement_preserves_the_terminal_revision()
    {
        var pending = Pending();
        var fact = Fact(pending);
        pending.BindNoLongerRequired(fact, Now);
        var revision = pending.Revision;
        pending.BindNoLongerRequired(fact, Now.AddMinutes(1));
        pending.ValidatePersistedState();
        await Assert.That(pending.State).IsEqualTo(ModerationRecognitionPendingState.NoLongerRequired);
        await Assert.That(pending.Revision).IsEqualTo(revision);
    }

    [Test]
    [Arguments("capture")]
    [Arguments("authority")]
    [Arguments("empty")]
    [Arguments("failure")]
    [Arguments("deadline")]
    public async Task Exhausted_revision_rejects_transitions_without_mutating_the_receipt(string transition)
    {
        var pending = Pending();
        var (decision, sanction) = Final(pending.SubjectId);
        var item = Item(pending, decision, sanction, Guid.CreateVersion7(), false);
        if (transition == "authority") pending.Capture([item], Now);
        typeof(ModerationRecognitionPending).GetProperty(nameof(pending.Revision))!
            .SetValue(pending, long.MaxValue - 1);
        var state = pending.State;
        var receipt = pending.MaterialReceipt;
        var count = pending.MaterialCount;
        var computedAt = pending.ComputedAt;
        pending.ValidatePersistedState();

        await Assert.That(() =>
        {
            switch (transition)
            {
                case "capture": pending.Capture([item], Now); break;
                case "authority": pending.BindAuthority(Fact(pending), Now); break;
                case "empty": pending.BindNoLongerRequired(Fact(pending), Now); break;
                case "failure": pending.ObserveFailure(ModerationRecognitionPendingFailure.StoreUnavailable, Now); break;
                case "deadline": pending.AdvanceDeadline(pending.SettleBy); break;
            }
        }).Throws<InvalidOperationException>();
        await Assert.That(pending.Revision).IsEqualTo(long.MaxValue - 1);
        await Assert.That(pending.State).IsEqualTo(state);
        await Assert.That(pending.MaterialReceipt).IsEqualTo(receipt);
        await Assert.That(pending.MaterialCount).IsEqualTo(count);
        await Assert.That(pending.Material.Count).IsEqualTo(count);
        await Assert.That(pending.ComputedAt).IsEqualTo(computedAt);
        await Assert.That(pending.AuthoritySequence).IsNull();
        await Assert.That(pending.Failure).IsEqualTo(ModerationRecognitionPendingFailure.None);
    }

    [Test]
    [Arguments(ModerationRecognitionPendingState.Computed)]
    [Arguments(ModerationRecognitionPendingState.AuthorityBound)]
    [Arguments(ModerationRecognitionPendingState.NoLongerRequired)]
    public async Task Persisted_state_cannot_claim_missing_material_or_authority(ModerationRecognitionPendingState state)
    {
        var pending = Pending();
        typeof(ModerationRecognitionPending).GetProperty(nameof(pending.State))!.SetValue(pending, state);
        await Assert.That(() => pending.ValidatePersistedState()).Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments("purpose")]
    [Arguments("partial-purpose")]
    [Arguments("protected-capability")]
    [Arguments("start-offset")]
    [Arguments("end-offset")]
    [Arguments("review")]
    [Arguments("drop-infinite")]
    public async Task Persisted_items_preserve_factory_purpose_and_finite_original_window(string defect)
    {
        var pending = Pending();
        var (decision, sanction) = Final(pending.SubjectId);
        var item = Item(pending, decision, sanction, Guid.CreateVersion7(), false);
        switch (defect)
        {
            case "purpose":
                typeof(ModerationRecognitionCaptureItem).GetProperty(nameof(item.PurposeCapabilityMask))!
                    .SetValue(item, 1 << (int)ModerationCapability.Admission);
                break;
            case "partial-purpose":
                typeof(ModerationRecognitionCaptureItem).GetProperty(nameof(item.OriginalCapabilityMask))!
                    .SetValue(item, item.OriginalCapabilityMask | (1 << (int)ModerationCapability.Upload));
                break;
            case "protected-capability":
                typeof(ModerationRecognitionCaptureItem).GetProperty(nameof(item.OriginalCapabilityMask))!
                    .SetValue(item, item.OriginalCapabilityMask | (1 << (int)ModerationCapability.PrivacyErasure));
                break;
            case "start-offset":
                typeof(ModerationRecognitionCaptureItem).GetProperty(nameof(item.StartsAt))!
                    .SetValue(item, item.StartsAt.ToOffset(TimeSpan.FromHours(1)));
                break;
            case "end-offset":
                typeof(ModerationRecognitionCaptureItem).GetProperty(nameof(item.EndsAt))!
                    .SetValue(item, item.EndsAt!.Value.ToOffset(TimeSpan.FromHours(1)));
                break;
            case "review":
                typeof(ModerationRecognitionCaptureItem).GetProperty(nameof(item.ReviewAt))!.SetValue(item, item.StartsAt);
                break;
            case "drop-infinite":
                typeof(ModerationRecognitionCaptureItem).GetProperty(nameof(item.DroppedAt))!
                    .SetValue(item, DateTimeOffset.MaxValue);
                break;
        }
        await Assert.That(() => item.ValidatePersistedState()).Throws<ArgumentException>();
    }

    [Test]
    [Arguments("before-capture")]
    [Arguments("offset")]
    [Arguments("infinite")]
    public async Task Rejected_drop_preserves_original_eligibility_and_material_receipt(string defect)
    {
        var pending = Pending();
        var (decision, sanction) = Final(pending.SubjectId);
        var item = Item(pending, decision, sanction, Guid.CreateVersion7(), false);
        pending.Capture([item], Now);
        var receipt = pending.MaterialReceipt;
        var observation = defect == "before-capture" ? Now.AddTicks(-1)
            : defect == "offset" ? Now.ToOffset(TimeSpan.FromHours(1)) : DateTimeOffset.MaxValue;
        await Assert.That(() => item.Drop(observation)).Throws<ArgumentException>();
        await Assert.That(item.DroppedAt).IsNull();
        await Assert.That(item.MatchesCurrent(decision, sanction, Now)).IsTrue();
        await Assert.That(pending.MaterialReceipt).IsEqualTo(receipt);
        await Assert.That(pending.HasCompleteMaterial).IsTrue();
    }

    private static ModerationRecognitionPending Pending() => ModerationRecognitionPending.Receive(
        Guid.CreateVersion7(), Guid.CreateVersion7(), 1, PrivacyErasureReasonCode.AccountDeletion,
        Guid.CreateVersion7(), Guid.CreateVersion7(), Now, Now.AddHours(1), Now.AddMinutes(5), Hash(), Now.AddDays(1));

    private static PrivacyErasureIntent Fact(ModerationRecognitionPending pending) => PrivacyErasureIntent.Record(
        pending.IntentId, 1, PrivacyErasureSubjectKind.User, pending.SubjectId, pending.ReasonCode,
        pending.PolicyVersion, Now.UtcDateTime, Now.UtcDateTime, Now.AddDays(30).UtcDateTime);

    private static byte[] Hash() => System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);

    private static ModerationRecognitionCaptureItem Item(ModerationRecognitionPending pending,
        ModerationDecision decision, ModerationSanction sanction, Guid source, bool enrollment, string? tag = null,
        DateTimeOffset? capturedAt = null)
    {
        var purpose = enrollment ? ModerationReferenceRecognition.SeparatelyQualifiedFinalEnrollment
            : ModerationReferenceRecognition.SeparatelyQualifiedFinalAction;
        var token = new ModerationRecognitionToken(sanction.Scope.TrustDomainId, AuthenticationProviderKind.Keycloak,
            purpose, "original", 1, 1, Convert.ToHexStringLower(Hash()));
        var material = new ModerationRecognitionCapturedMaterial(token, tag ?? Convert.ToHexStringLower(Hash()),
            ModerationRecognitionContribution.Capture(decision, sanction, purpose, capturedAt ?? Now, Now.AddDays(3)));
        return ModerationRecognitionCaptureItem.Capture(Guid.CreateVersion7(), pending.IntentId,
            decision, material, source, 1, 7, Guid.CreateVersion7(), 1, 8, 1);
    }

    private static (ModerationDecision Decision, ModerationSanction Sanction) Final(Guid subject, bool eventScope = false)
    {
        var instance = AuthorityScope.Instance(AuthorityScope.Solution(
            AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7());
        var tenant = AuthorityScope.Tenant(instance, Guid.CreateVersion7());
        var scope = eventScope ? AuthorityScope.Event(tenant, Guid.CreateVersion7()) : tenant;
        var decision = ModerationDecision.Propose(Guid.CreateVersion7(), Guid.CreateVersion7(),
            SubjectReference.Create(ModerationSubjectKind.Human, subject, instance),
            SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), instance),
            instance, scope, ModerationRuleReference.Create(scope.Id, "recognition.capture", 1),
            [ModerationCapability.Publish, ModerationCapability.Admission], Now, Now.AddDays(2), null, 1, Now);
        decision.RecordResponse(1, Now);
        decision.AcceptFinding(Guid.CreateVersion7(), 2, Now);
        decision.Approve(SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), instance),
            instance, 3, Now);
        return (decision, decision.Activate(Guid.CreateVersion7(), 4, Now));
    }
}
