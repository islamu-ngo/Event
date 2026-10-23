using System.Security.Cryptography;

namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationRecognitionDispositionTests
{
    internal static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Recovery_attestation_is_not_legal_authorization_or_erasure_completion()
    {
        var (pending, source, adoption) = Fixture();
        var disposition = Assess(pending, source, adoption);
        await Assert.That(disposition.Authorizes(pending, source, adoption, Now)).IsFalse();
        await Assert.That(disposition.AuthoritySequence).IsNull();
        await Assert.That(pending.HasCompleteMaterial).IsFalse();
        await Assert.That(() => disposition.Authorize(1, source.RecoveryOwnerHumanId, pending,
            source, adoption, Now, 0)).Throws<InvalidOperationException>();
        disposition.Authorize(1, source.PrivacyOwnerHumanId, pending, source, adoption, Now, 0);
        await Assert.That(disposition.Authorizes(pending, source, adoption, Now)).IsTrue();
        await Assert.That(disposition.AuthoritySequence).IsNull();
    }

    [Test]
    public async Task Changed_request_revision_or_withdrawn_adoption_closes_authorization()
    {
        var (pending, source, adoption) = Fixture();
        var disposition = Assess(pending, source, adoption);
        disposition.Authorize(1, source.PrivacyOwnerHumanId, pending, source, adoption, Now, 0);
        pending.ObserveFailure(ModerationRecognitionPendingFailure.StoreUnavailable, Now);
        await Assert.That(disposition.Authorizes(pending, source, adoption, Now)).IsFalse();
        var fresh = Assess(pending, source, adoption);
        fresh.Authorize(1, source.PrivacyOwnerHumanId, pending, source, adoption, Now, 0);
        adoption.Withdraw(1, source.RecordedByHumanId, Now);
        await Assert.That(fresh.Authorizes(pending, source, adoption, Now)).IsFalse();
    }

    [Test]
    public async Task Exact_deadline_and_receipt_proof_never_become_a_matching_receipt()
    {
        var (pending, source, adoption) = Fixture();
        var disposition = Assess(pending, source, adoption);
        disposition.Authorize(1, source.PrivacyOwnerHumanId, pending, source, adoption, Now, 0);
        var proof = ModerationRecognitionDispositionRemedy.Protect(disposition, pending, source, Now);
        await Assert.That(proof.Authenticates(pending.ReceiptHash, Now)).IsTrue();
        await Assert.That(proof.Authenticates(RandomNumberGenerator.GetBytes(32), Now)).IsFalse();
        await Assert.That(proof.Authenticates(pending.ReceiptHash, proof.RetainUntil)).IsFalse();
        // The independently authorized correction purpose survives ordinary status expiry,
        // but never becomes an account credential or outlives its original calendar remedy.
        await Assert.That(proof.Authenticates(pending.ReceiptHash, pending.ReceiptExpiresAt)).IsTrue();
        await Assert.That(proof.Authenticates(pending.ReceiptHash, proof.RemedyUntil)).IsFalse();
        await Assert.That(proof.RemedyUntil).IsEqualTo(Now.AddMonths(6));
        await Assert.That(disposition.OriginalDeadline).IsEqualTo(pending.SettleBy);
        await Assert.That(disposition.Authorizes(pending, source, adoption, disposition.ValidUntil)).IsFalse();
        await Assert.That(pending.MaterialReceipt).IsNull();
    }

    [Test]
    public async Task Authority_binding_rejects_another_request_and_preserves_exact_replay()
    {
        var (pending, source, adoption) = Fixture();
        var disposition = Assess(pending, source, adoption);
        disposition.Authorize(1, source.PrivacyOwnerHumanId, pending, source, adoption, Now, 0);
        PrivacyErasureIntent Fact(Guid id) => PrivacyErasureIntent.Record(id, 1,
            PrivacyErasureSubjectKind.User, pending.SubjectId, pending.ReasonCode, pending.PolicyVersion,
            Now.UtcDateTime, Now.UtcDateTime, Now.AddDays(30).UtcDateTime);
        await Assert.That(() => disposition.Bind(Fact(Guid.CreateVersion7()), Now))
            .Throws<InvalidOperationException>();
        var fact = Fact(pending.IntentId);
        disposition.Bind(fact, Now);
        disposition.Bind(fact, Now.AddMinutes(1));
        await Assert.That(disposition.MatchesFact(fact)).IsTrue();
        await Assert.That(disposition.Authorizes(pending, source, adoption, Now)).IsFalse();
        await Assert.That(pending.AuthoritySequence).IsNull();
    }

    internal static ModerationRecognitionDisposition Assess(ModerationRecognitionPending pending,
        ModerationRecognitionPrivacySource source, ModerationRecognitionPrivacyAdoption adoption) =>
        ModerationRecognitionDisposition.Assess(Guid.CreateVersion7(), pending, source, adoption,
            source.RecoveryOwnerHumanId, pending.Failure == ModerationRecognitionPendingFailure.StoreUnavailable
                ? ModerationRecognitionDispositionGrounds.OriginalStoreRecoveryUnavailable
                : ModerationRecognitionDispositionGrounds.OriginalKeyRecoveryUnavailable,
            ModerationRecognitionDisposition.ComputeCoverageReceipt([source.Id]), 1, Now, Now.AddMinutes(5));

    [Test]
    public async Task Accepted_owner_receipts_preserve_commit_chronology_without_renewing_authorization()
    {
        var (pending, source, adoption) = Fixture();
        var disposition = Assess(pending, source, adoption);
        var recovery = ModerationRecognitionDispositionAcceptance.Record(disposition,
            ModerationRecognitionDispositionAcceptanceRole.RecoveryOwner, source.RecoveryOwnerHumanId);
        disposition.Authorize(1, source.PrivacyOwnerHumanId, pending, source, adoption, Now, 9);
        var privacy = ModerationRecognitionDispositionAcceptance.Record(disposition,
            ModerationRecognitionDispositionAcceptanceRole.PrivacyOwner, source.PrivacyOwnerHumanId);
        PrivacyErasureIntent Fact(long sequence, DateTimeOffset recorded) => PrivacyErasureIntent.Record(
            pending.IntentId, sequence, PrivacyErasureSubjectKind.User, pending.SubjectId,
            pending.ReasonCode, pending.PolicyVersion, Now.UtcDateTime, recorded.UtcDateTime, Now.AddDays(30).UtcDateTime);
        var committed = Fact(10, Now.AddMinutes(1));
        adoption.Withdraw(1, source.RecordedByHumanId, Now.AddMinutes(2));
        await Assert.That(disposition.Authorizes(pending, source, adoption, Now.AddHours(1))).IsFalse();
        await Assert.That(disposition.MatchesAcceptedSource(source, adoption, committed)).IsTrue();
        await Assert.That(disposition.AcceptsCommittedFact(Fact(9, Now.AddMinutes(1)))).IsFalse();
        await Assert.That(disposition.AcceptsCommittedFact(Fact(10, Now.AddMinutes(5)))).IsFalse();
        await Assert.That(recovery.Matches(disposition)).IsTrue();
        await Assert.That(privacy.Matches(disposition)).IsTrue();
        disposition.Bind(committed, Now.AddDays(1));
        await Assert.That(privacy.Matches(disposition)).IsTrue();
        await Assert.That(disposition.MatchesFact(committed)).IsTrue();
    }

    internal static (ModerationRecognitionPending, ModerationRecognitionPrivacySource,
        ModerationRecognitionPrivacyAdoption) Fixture()
    {
        var privacy = Guid.CreateVersion7();
        var recovery = Guid.CreateVersion7();
        var governor = Guid.CreateVersion7();
        var source = ModerationRecognitionPrivacySource.Record(Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1,
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1, new string('a', 64),
            ModerationRecognitionPrivacyTests.Terms(),
            new(privacy, privacy, recovery, recovery, governor, "Distinct canonical controllers.",
                "Qualified statutory erasure and protected remedy mandate.", "Qualified original-key recovery mandate."),
            ModerationRecognitionPrivacyTests.Retention(), governor, 4, Now.AddHours(-2), Now.AddDays(1));
        var adoption = ModerationRecognitionPrivacyAdoption.Adopt(Guid.CreateVersion7(), source,
            privacy, 5, Now.AddHours(-2), Now.AddHours(2));
        var pending = ModerationRecognitionPending.Receive(Guid.CreateVersion7(), Guid.CreateVersion7(), 1,
            PrivacyErasureReasonCode.AccountDeletion, privacy, recovery, Now.AddHours(-1),
            Now.AddHours(1), Now.AddMinutes(-5), RandomNumberGenerator.GetBytes(32), Now.AddDays(1));
        pending.ObserveFailure(ModerationRecognitionPendingFailure.KeyUnavailable, Now);
        return (pending, source, adoption);
    }
}
