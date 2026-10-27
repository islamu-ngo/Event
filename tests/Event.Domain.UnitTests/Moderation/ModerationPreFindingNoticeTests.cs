using ISLAMU.Moderation.Domain;

namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationPreFindingNoticeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Capture_preserves_child_ownership_without_creating_a_finding()
    {
        var (child, source, custody, recipient) = Setup();
        var notice = Issue(child, source, custody, recipient);
        await Assert.That(notice.CaseId).IsEqualTo(child.Id);
        await Assert.That(notice.OccurrenceCaseId).IsEqualTo(child.OccurrenceCaseId!.Value);
        await Assert.That(notice.EvidenceRevision).IsEqualTo(1L);
        await Assert.That(notice.AttributionSourceId).IsEqualTo(child.AttributionSourceId!.Value);
        await Assert.That(notice.HasResponseAt(Now)).IsFalse();
        await Assert.That(child.State).IsEqualTo(ModerationCaseState.Open);
        await Assert.That(child.SubjectKind).IsEqualTo(ModerationSubjectKind.Content);
    }

    [Test]
    public async Task Receipt_and_elapsed_time_cannot_substitute_for_response()
    {
        var (child, source, custody, recipient) = Setup();
        var notice = Issue(child, source, custody, recipient);
        notice.Acknowledge(recipient.HumanId, 1, Now.AddMinutes(1));
        await Assert.That(notice.HasResponseAt(Now.AddMinutes(20))).IsFalse();
        await Assert.That(notice.HasResponseAt(notice.RetainUntil)).IsFalse();
    }

    [Test]
    public async Task Response_establishes_receipt_without_renewing_original_custody()
    {
        var (child, source, custody, recipient) = Setup();
        var notice = Issue(child, source, custody, recipient);
        var until = notice.RetainUntil;
        notice.Respond(Guid.CreateVersion7(), recipient.HumanId, 1, [4, 5, 6], Now.AddMinutes(2));
        await Assert.That(notice.HasResponseAt(Now.AddMinutes(3))).IsTrue();
        await Assert.That(notice.AcknowledgedAt).IsEqualTo(Now.AddMinutes(2));
        await Assert.That(notice.RetainUntil).IsEqualTo(until);
        await Assert.That(notice.HasResponseAt(until)).IsFalse();
    }

    [Test]
    public async Task Another_manager_or_reviewer_cannot_acknowledge_or_answer()
    {
        var (child, source, custody, recipient) = Setup();
        var notice = Issue(child, source, custody, recipient);
        await Assert.That(() => notice.Acknowledge(Guid.CreateVersion7(), 1, Now))
            .Throws<InvalidOperationException>();
        await Assert.That(() => notice.Respond(Guid.CreateVersion7(), notice.IssuedByHumanId, 1, [1], Now))
            .Throws<InvalidOperationException>();
        await Assert.That(notice.Revision).IsEqualTo(1L);
    }

    [Test]
    public async Task Custody_requires_separate_named_privacy_owner_acceptance()
    {
        var (child, source, _, recipient) = Setup();
        var recorder = Guid.CreateVersion7();
        var custody = ModerationPreFindingCustody.Propose(Guid.CreateVersion7(), source.HostBindingId,
            child, source, recorder, Guid.CreateVersion7(), Terms(), Now, Now.AddHours(1));
        await Assert.That(() => custody.Accept(recorder, 1, Now)).Throws<InvalidOperationException>();
        await Assert.That(() => Issue(child, source, custody, recipient)).Throws<ArgumentException>();
    }

    [Test]
    public async Task Approval_cannot_extend_source_or_notice_deadline()
    {
        var (child, source, custody, recipient) = Setup();
        await Assert.That(() => ModerationPreFindingNotice.Issue(Guid.CreateVersion7(), child, source, custody,
            Guid.CreateVersion7(), Guid.CreateVersion7(), 1, recipient, [1], Now,
            custody.ValidUntil.AddTicks(1))).Throws<ArgumentException>();
        await Assert.That(() => custody.Accept(custody.PrivacyOwnerHumanId, 2, Now))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Confidential_root_and_unattributed_case_cannot_receive_notice()
    {
        var (child, source, custody, recipient) = Setup();
        var unlinked = ModerationCase.Open(Guid.CreateVersion7(), child.Subject!, child.Scope,
            Guid.CreateVersion7(), Now);
        await Assert.That(() => Issue(unlinked, source, custody, recipient)).Throws<ArgumentException>();
    }

    [Test]
    public async Task Withdrawal_erases_payloads_without_turning_silence_into_permission()
    {
        var (child, source, custody, recipient) = Setup();
        var notice = Issue(child, source, custody, recipient);
        notice.Respond(Guid.CreateVersion7(), recipient.HumanId, 1, [7], Now.AddMinutes(1));
        notice.Purge(2, Now.AddMinutes(2), withdrawn: true);
        await Assert.That(notice.ProtectedNotice.IsEmpty).IsTrue();
        await Assert.That(notice.ProtectedResponse.IsEmpty).IsTrue();
        await Assert.That(notice.HasResponseAt(Now.AddMinutes(2))).IsFalse();
        await Assert.That(notice.EvidenceRevision).IsEqualTo(1L);
    }

    [Test]
    public async Task Deadline_purge_is_half_open_and_cannot_run_early()
    {
        var (child, source, custody, recipient) = Setup();
        var notice = Issue(child, source, custody, recipient);
        await Assert.That(() => notice.Purge(1, notice.RetainUntil.AddTicks(-1), withdrawn: false))
            .Throws<InvalidOperationException>();
        notice.Purge(1, notice.RetainUntil, withdrawn: false);
        await Assert.That(notice.ProtectedNotice.IsEmpty).IsTrue();
        await Assert.That(notice.PurgedAt).IsEqualTo(notice.RetainUntil);
    }

    [Test]
    public async Task Stale_or_repeated_response_cannot_replace_original_statement()
    {
        var (child, source, custody, recipient) = Setup();
        var notice = Issue(child, source, custody, recipient);
        notice.Respond(Guid.CreateVersion7(), recipient.HumanId, 1, [9], Now.AddMinutes(1));
        await Assert.That(() => notice.Respond(Guid.CreateVersion7(), recipient.HumanId, 2, [8], Now.AddMinutes(2)))
            .Throws<InvalidOperationException>();
        await Assert.That(notice.ProtectedResponse.ToArray()).IsEquivalentTo(new byte[] { 9 });
    }

    [Test]
    public async Task Content_recipient_assignment_requires_uuid_v7()
    {
        var (child, source, custody, recipient) = Setup();
        await Assert.That(() => Issue(child, source, custody,
            recipient with { AssignmentId = Guid.NewGuid() })).Throws<ArgumentException>();
        await Assert.That(child.Revision).IsEqualTo(1L);
        await Assert.That(Issue(child, source, custody, recipient).RecipientAssignmentId)
            .IsEqualTo(recipient.AssignmentId);
    }

    [Test]
    public async Task Hydrated_response_identity_must_match_creation_uuid_contract()
    {
        var (child, source, custody, recipient) = Setup();
        var notice = Issue(child, source, custody, recipient);
        notice.Respond(Guid.CreateVersion7(), recipient.HumanId, 1, [9], Now);
        Set(notice, nameof(notice.ResponseId), Guid.NewGuid());
        await Assert.That(notice.ValidatePersistedState).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Explicit_and_implicit_acknowledgement_preserve_valid_revision_history(bool explicitAcknowledgement)
    {
        var (child, source, custody, recipient) = Setup();
        var notice = Issue(child, source, custody, recipient);
        if (explicitAcknowledgement)
            notice.Acknowledge(recipient.HumanId, 1, Now);
        notice.Respond(Guid.CreateVersion7(), recipient.HumanId,
            explicitAcknowledgement ? 2 : 1, [9], Now);
        notice.ValidatePersistedState();
        await Assert.That(notice.Revision).IsEqualTo(explicitAcknowledgement ? 3L : 2L);
        notice.Purge(notice.Revision, notice.RetainUntil, withdrawn: false);
        notice.ValidatePersistedState();
        await Assert.That(notice.ProtectedResponse.IsEmpty).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Custody_transitions_reject_non_UTC_before_mutation(bool withdraw)
    {
        var fixture = new ModerationPreFindingGenerationFixture();
        var custody = fixture.Propose(fixture.First, Now);
        var at = Now.ToOffset(TimeSpan.FromHours(1));
        if (withdraw)
            await Assert.That(() => custody.Withdraw(custody.PrivacyOwnerHumanId, 1, at))
                .Throws<InvalidOperationException>();
        else
            await Assert.That(() => custody.Accept(custody.PrivacyOwnerHumanId, 1, at))
                .Throws<InvalidOperationException>();
        custody.ValidatePersistedState();
        await Assert.That(custody.Revision).IsEqualTo(1L);
        await Assert.That(custody.AcceptedAt).IsNull();
        await Assert.That(custody.WithdrawnAt).IsNull();
    }

    [Test]
    public async Task Custody_withdrawal_rejects_infinite_time_before_mutation()
    {
        var (child, source, custody, recipient) = Setup();
        await Assert.That(() => custody.Withdraw(custody.PrivacyOwnerHumanId, 2, DateTimeOffset.MaxValue))
            .Throws<InvalidOperationException>();
        await Assert.That(custody.Revision).IsEqualTo(2L);
        await Assert.That(custody.WithdrawnAt).IsNull();
        await Assert.That(Issue(child, source, custody, recipient).Revision).IsEqualTo(1L);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Recipient_transitions_reject_non_UTC_before_mutation(bool response)
    {
        var (child, source, custody, recipient) = Setup();
        var notice = Issue(child, source, custody, recipient);
        var at = Now.ToOffset(TimeSpan.FromHours(1));
        if (response)
            await Assert.That(() => notice.Respond(Guid.CreateVersion7(), recipient.HumanId, 1, [9], at))
                .Throws<InvalidOperationException>();
        else
            await Assert.That(() => notice.Acknowledge(recipient.HumanId, 1, at))
                .Throws<InvalidOperationException>();
        notice.ValidatePersistedState();
        await Assert.That(notice.Revision).IsEqualTo(1L);
        await Assert.That(notice.AcknowledgedAt).IsNull();
        await Assert.That(notice.ResponseId).IsNull();
        await Assert.That(notice.ProtectedResponse.IsEmpty).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Purge_rejects_infinite_or_non_UTC_time_before_clearing_payload(bool infinite)
    {
        var (child, source, custody, recipient) = Setup();
        var notice = Issue(child, source, custody, recipient);
        var at = infinite ? DateTimeOffset.MaxValue : Now.ToOffset(TimeSpan.FromHours(1));
        await Assert.That(() => notice.Purge(1, at, withdrawn: true)).Throws<InvalidOperationException>();
        notice.ValidatePersistedState();
        await Assert.That(notice.PurgedAt).IsNull();
        await Assert.That(notice.ProtectedNotice.ToArray()).IsEquivalentTo(new byte[] { 1, 2, 3 });
        await Assert.That(notice.Revision).IsEqualTo(1L);
    }

    [Test]
    [Arguments("AcceptedAt")]
    [Arguments("WithdrawnAt")]
    public async Task Hydrated_custody_transition_times_require_UTC(string property)
    {
        var (_, _, custody, _) = Setup();
        custody.Withdraw(custody.PrivacyOwnerHumanId, 2, Now);
        Set(custody, property, Now.ToOffset(TimeSpan.FromHours(1)));
        await Assert.That(custody.ValidatePersistedState).Throws<ArgumentException>();
    }

    [Test]
    [Arguments("AcknowledgedAt")]
    [Arguments("RespondedAt")]
    [Arguments("PurgedAt")]
    public async Task Hydrated_notice_transition_times_require_UTC(string property)
    {
        var (child, source, custody, recipient) = Setup();
        var notice = Issue(child, source, custody, recipient);
        notice.Respond(Guid.CreateVersion7(), recipient.HumanId, 1, [9], Now);
        notice.Purge(2, Now, withdrawn: true);
        Set(notice, property, Now.ToOffset(TimeSpan.FromHours(1)));
        await Assert.That(notice.ValidatePersistedState).Throws<ArgumentException>();
    }

    [Test]
    public async Task Corrupt_revision_is_rejected_before_a_transition_can_mutate()
    {
        var (child, source, custody, recipient) = Setup();
        var notice = Issue(child, source, custody, recipient);
        Set(notice, nameof(notice.Revision), long.MaxValue);
        await Assert.That(() => notice.Acknowledge(recipient.HumanId, long.MaxValue, Now))
            .Throws<ArgumentException>();
        await Assert.That(notice.AcknowledgedAt).IsNull();
    }

    [Test]
    public async Task Hydrated_early_purge_requires_the_original_withdrawal_marker()
    {
        var (child, source, custody, recipient) = Setup();
        var notice = Issue(child, source, custody, recipient);
        notice.Purge(1, Now, withdrawn: true);
        Set(notice, nameof(notice.Withdrawn), false);
        await Assert.That(notice.ValidatePersistedState).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Hydrated_withdrawal_or_purge_cannot_retain_an_infinite_timestamp(bool purge)
    {
        var (child, source, custody, recipient) = Setup();
        if (purge)
        {
            var notice = Issue(child, source, custody, recipient);
            notice.Purge(1, Now, withdrawn: true);
            Set(notice, nameof(notice.PurgedAt), DateTimeOffset.MaxValue);
            await Assert.That(notice.ValidatePersistedState).Throws<ArgumentException>();
        }
        else
        {
            custody.Withdraw(custody.PrivacyOwnerHumanId, 2, Now);
            Set(custody, nameof(custody.WithdrawnAt), DateTimeOffset.MaxValue);
            await Assert.That(custody.ValidatePersistedState).Throws<ArgumentException>();
        }
    }

    private static void Set(object row, string property, object value) =>
        row.GetType().GetProperty(property)!.SetValue(row, value);

    private static ModerationPreFindingNotice Issue(ModerationCase child, ModerationCaseAttributionSource source,
        ModerationPreFindingCustody custody, ModerationNoticeContentRepresentative recipient) =>
        ModerationPreFindingNotice.Issue(Guid.CreateVersion7(), child, source, custody, Guid.CreateVersion7(),
            Guid.CreateVersion7(), 1, recipient, [1, 2, 3], Now, Now.AddMinutes(30));

    private static ModerationPreFindingCustodyTerms Terms() =>
        new(TimeSpan.FromHours(1), 8192,
            "Protected pre-finding notice and voluntary response for this exact evidence revision.",
            "Independently accepted purpose, private access and finite deletion obligation.");

    private static (ModerationCase, ModerationCaseAttributionSource,
        ModerationPreFindingCustody, ModerationNoticeContentRepresentative) Setup()
    {
        var fixture = new ModerationPreFindingGenerationFixture();
        var custody = fixture.Propose(fixture.First, Now);
        custody.Accept(custody.PrivacyOwnerHumanId, 1, Now);
        return (fixture.Owner, fixture.First, custody, fixture.Recipient);
    }
}
