using Explore.Domain.Enums;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;

namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationRecognitionContinuityBindingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Eligibility_drop_preserves_immutable_material_commitments_on_binding_and_recovery()
    {
        var (item, decision, sanction) = Original();
        var originalReceipt = item.ComputeMaterialReceipt();
        var binding = ModerationRecognitionContinuityBinding.Bind(Guid.CreateVersion7(),
            Guid.CreateVersion7(), item, decision, sanction, Now);
        var recovery = ModerationRecognitionActionRecovery.Receive(Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), item, SettledReceipt(item), Now);
        item.Drop(Now.AddMinutes(1));
        await Assert.That(item.ComputeMaterialReceipt()).IsEqualTo(originalReceipt);
        await Assert.That(binding.CaptureMaterialReceipt).IsEqualTo(originalReceipt);
        await Assert.That(recovery.CaptureMaterialReceipt).IsEqualTo(originalReceipt);
        await Assert.That(binding.IsCurrent(item, decision, sanction, Now.AddMinutes(1))).IsFalse();
        await Assert.That(recovery.CouldAffect(item, item.Scope, ModerationCapability.Publish, Now.AddMinutes(1))).IsFalse();
    }

    [Test]
    public async Task Deferred_action_uncertainty_preserves_owners_and_original_scope_without_claiming_a_match()
    {
        var (item, _, _) = Original();
        var receipt = SettledReceipt(item);
        var fresh = Guid.CreateVersion7();
        var login = Guid.CreateVersion7();
        var recovery = ModerationRecognitionActionRecovery.Receive(Guid.CreateVersion7(), fresh, login, item, receipt, Now);
        await Assert.That(recovery.FreshSubjectId).IsEqualTo(fresh);
        await Assert.That(recovery.VerifiedBindingId).IsEqualTo(login);
        await Assert.That(recovery.PrivacyOwnerHumanId).IsEqualTo(receipt.PrivacyOwnerHumanId);
        await Assert.That(recovery.RecoveryOwnerHumanId).IsEqualTo(receipt.RecoveryOwnerHumanId);
        await Assert.That(recovery.DispositionBy).IsEqualTo(item.EndsAt!.Value);
        await Assert.That(recovery.CouldAffect(item, item.Scope, ModerationCapability.Publish, Now)).IsTrue();
        await Assert.That(recovery.CouldAffect(item, item.Scope, ModerationCapability.ReadResource, Now)).IsFalse();
        await Assert.That(recovery.CouldAffect(item, item.Scope, ModerationCapability.Publish, recovery.DispositionBy)).IsFalse();
    }

    [Test]
    public async Task Deferred_admission_candidate_cannot_become_an_action_recovery_marker()
    {
        var (item, _, _) = Original(admission: true);
        var receipt = SettledReceipt(item);
        await Assert.That(() => ModerationRecognitionActionRecovery.Receive(Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), item, receipt, Now)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Deferred_recovery_cannot_replace_an_unsettled_capture_promise()
    {
        var (item, _, _) = Original();
        var receipt = ReceivedReceipt(item);
        receipt.Capture([item], Now);
        await Assert.That(() => ModerationRecognitionActionRecovery.Receive(Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), item, receipt, Now)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Completed_matching_resolves_uncertainty_without_extending_original_authorization()
    {
        var (item, _, _) = Original();
        var recovery = ModerationRecognitionActionRecovery.Receive(Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), item, SettledReceipt(item), Now);
        var ceiling = recovery.DispositionBy;
        recovery.Resolve(Now.AddMinutes(1));
        recovery.Resolve(Now.AddMinutes(2));
        await Assert.That(recovery.ResolvedAt).IsEqualTo(Now.AddMinutes(1));
        await Assert.That(recovery.DispositionBy).IsEqualTo(ceiling);
        await Assert.That(recovery.CouldAffect(item, item.Scope, ModerationCapability.Publish, Now.AddMinutes(2))).IsFalse();
    }

    private static ModerationRecognitionPending ReceivedReceipt(ModerationRecognitionCaptureItem item) =>
        ModerationRecognitionPending.Receive(item.IntentId, item.SubjectId, 1, PrivacyErasureReasonCode.AccountDeletion,
            Guid.CreateVersion7(), Guid.CreateVersion7(), Now, Now.AddHours(2), Now.AddHours(1),
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(32), Now.AddDays(7));

    private static ModerationRecognitionPending SettledReceipt(ModerationRecognitionCaptureItem item)
    {
        var receipt = ReceivedReceipt(item);
        receipt.Capture([item], Now);
        receipt.BindAuthority(PrivacyErasureIntent.Record(item.IntentId, 1, PrivacyErasureSubjectKind.User,
            item.SubjectId, PrivacyErasureReasonCode.AccountDeletion, 1,
            Now.UtcDateTime, Now.UtcDateTime, Now.AddDays(30).UtcDateTime), Now);
        return receipt;
    }

    [Test]
    public async Task Action_binding_preserves_original_contribution_without_reassigning_sanction()
    {
        var (item, decision, sanction) = Original();
        var fresh = Guid.CreateVersion7();
        var binding = ModerationRecognitionContinuityBinding.Bind(Guid.CreateVersion7(), fresh, item, decision, sanction, Now);
        await Assert.That(binding.FreshSubjectId).IsEqualTo(fresh);
        await Assert.That(binding.IsCurrent(item, decision, sanction, Now)).IsTrue();
        await Assert.That(sanction.SubjectId).IsEqualTo(item.SubjectId);
        await Assert.That(sanction.Revision).IsEqualTo(1L);
        await Assert.That(binding.CaptureItemId).IsEqualTo(item.Id);
        await Assert.That(binding.RetainUntil).IsEqualTo(item.RetainUntil);
    }

    [Test]
    [Arguments("admission")]
    [Arguments("old-subject")]
    [Arguments("expired")]
    [Arguments("review-due")]
    [Arguments("dropped")]
    [Arguments("reversed")]
    public async Task Action_binding_follows_current_canonical_eligibility(string defect)
    {
        var (item, decision, sanction) = Original(
            admission: defect == "admission", review: defect == "review-due");
        var now = defect == "expired" ? Now.AddDays(2) : defect == "review-due" ? Now.AddHours(1) : Now;
        if (defect == "dropped") item.Drop(now);
        if (defect == "reversed") sanction.Revoke(sanction.Revision, now);
        if (defect == "review-due")
        {
            var binding = ModerationRecognitionContinuityBinding.Bind(Guid.CreateVersion7(),
                Guid.CreateVersion7(), item, decision, sanction, now);
            await Assert.That(sanction.IsEffectiveAt(now)).IsTrue();
            await Assert.That(binding.IsCurrent(item, decision, sanction, now)).IsTrue();
            await Assert.That(item.ReviewAt).IsEqualTo(now);
            await Assert.That(item.EndsAt).IsEqualTo(Now.AddDays(2));
            return;
        }
        await Assert.That(() => ModerationRecognitionContinuityBinding.Bind(Guid.CreateVersion7(),
            defect == "old-subject" ? item.SubjectId : Guid.CreateVersion7(), item, decision, sanction, now))
            .Throws<ArgumentException>();
    }

    [Test]
    [Arguments("expiry")]
    [Arguments("review")]
    [Arguments("retention")]
    [Arguments("reversal")]
    [Arguments("replacement")]
    [Arguments("capture-drop")]
    public async Task Later_current_reads_follow_canonical_changes_without_extending_original_authority(string change)
    {
        var (item, decision, sanction) = Original(review: change == "review",
            shortRetention: change == "retention");
        var binding = ModerationRecognitionContinuityBinding.Bind(Guid.CreateVersion7(),
            Guid.CreateVersion7(), item, decision, sanction, Now);
        var now = change is "review" or "retention" ? Now.AddHours(1) : Now.AddDays(2);
        if (change == "reversal") sanction.Revoke(sanction.Revision, Now.AddMinutes(1));
        if (change == "replacement") sanction.Supersede(Guid.CreateVersion7(), sanction.Revision, Now.AddMinutes(1));
        if (change == "capture-drop") item.Drop(Now.AddMinutes(1));
        await Assert.That(binding.IsCurrent(item, decision, sanction, now)).IsEqualTo(change == "review");
        if (change == "review")
        {
            await Assert.That(sanction.IsEffectiveAt(now)).IsTrue();
            await Assert.That(item.ReviewAt).IsEqualTo(now);
        }
        await Assert.That(sanction.SubjectId).IsEqualTo(item.SubjectId);
        await Assert.That(sanction.EndsAt).IsEqualTo(Now.AddDays(2));
    }

    [Test]
    public async Task Admission_requires_original_purpose_and_exact_covered_ancestry()
    {
        var (item, _, _) = Original(admission: true);
        var otherTenant = AuthorityScope.Tenant(item.Scope.Parent!, Guid.CreateVersion7());
        await Assert.That(ModerationRecognitionContinuityBinding.CoversAdmission(item, item.Scope, Now)).IsTrue();
        await Assert.That(ModerationRecognitionContinuityBinding.CoversAdmission(item, otherTenant, Now)).IsFalse();
        await Assert.That(ModerationRecognitionContinuityBinding.CoversAdmission(item, item.Scope.Parent!, Now)).IsFalse();
        var (action, _, _) = Original();
        await Assert.That(ModerationRecognitionContinuityBinding.CoversAdmission(action, action.Scope, Now)).IsFalse();
    }

    private static (ModerationRecognitionCaptureItem Item, ModerationDecision Decision, ModerationSanction Sanction)
        Original(bool admission = false, bool review = false, bool shortRetention = false)
    {
        var instance = AuthorityScope.Instance(AuthorityScope.Solution(
            AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7());
        var scope = AuthorityScope.Tenant(instance, Guid.CreateVersion7());
        var subject = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), instance);
        var actor = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), instance);
        var decision = ModerationDecision.Propose(Guid.CreateVersion7(), Guid.CreateVersion7(), subject, actor,
            instance, scope, ModerationRuleReference.Create(scope.Id, "continuity.fixture", 1),
            [ModerationCapability.Publish, ModerationCapability.Admission], Now, Now.AddDays(2),
            review ? Now.AddHours(1) : null, 1, Now);
        decision.RecordResponse(1, Now);
        decision.AcceptFinding(Guid.CreateVersion7(), 2, Now);
        decision.Approve(SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), instance), instance, 3, Now);
        var sanction = decision.Activate(Guid.CreateVersion7(), 4, Now);
        var purpose = admission ? ModerationReferenceRecognition.SeparatelyQualifiedFinalEnrollment
            : ModerationReferenceRecognition.SeparatelyQualifiedFinalAction;
        var contribution = ModerationRecognitionContribution.Capture(decision, sanction, purpose, Now,
            shortRetention ? Now.AddHours(1) : Now.AddDays(3));
        var material = new ModerationRecognitionCapturedMaterial(new ModerationRecognitionToken(scope.TrustDomainId,
            AuthenticationProviderKind.Keycloak, purpose, "original", 1, 1, Digest()), Digest(), contribution);
        return (ModerationRecognitionCaptureItem.Capture(Guid.CreateVersion7(), Guid.CreateVersion7(), decision,
            material, Guid.CreateVersion7(), 1, 3, Guid.CreateVersion7(), 1, 4, 1), decision, sanction);
    }

    private static string Digest() => Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
}
