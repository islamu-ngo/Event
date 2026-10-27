using Explore.Domain;
using Explore.Domain.Enums;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;

namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationRecognitionRecaptureTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Recapture_preserves_original_authority_but_receipts_the_current_erasure_owner()
    {
        var (original, decision, sanction) = Original();
        var fresh = Guid.CreateVersion7();
        var binding = ModerationRecognitionContinuityBinding.Bind(Guid.CreateVersion7(), fresh,
            original, decision, sanction, Now);
        var pending = Pending(fresh, Now.AddHours(1));
        var item = Recapture(original, binding, decision, sanction, pending);
        pending.Capture([item], Now.AddHours(1));
        await Assert.That(pending.HasCompleteMaterial).IsTrue();
        await Assert.That(item.SubjectId).IsEqualTo(decision.SubjectId);
        await Assert.That(item.ErasureSubjectId).IsEqualTo(fresh);
        await Assert.That(item.ParentCaptureItemId).IsEqualTo(original.Id);
        await Assert.That(item.ContinuityBindingId).IsEqualTo(binding.Id);
        await Assert.That(item.FindingId).IsEqualTo(decision.FindingId!.Value);
        await Assert.That(item.SanctionRevision).IsEqualTo(original.SanctionRevision);
        await Assert.That(item.DecisionRevision).IsEqualTo(original.DecisionRevision);
        await Assert.That(item.SourceId).IsEqualTo(original.SourceId);
        await Assert.That(item.AdoptionId).IsEqualTo(original.AdoptionId);
        await Assert.That(item.RetainUntil).IsEqualTo(original.RetainUntil);
        await Assert.That(item.OriginalCapturedAt).IsEqualTo(original.CapturedAt);
        await Assert.That(sanction.SubjectId).IsEqualTo(original.SubjectId);
    }

    [Test]
    public async Task Chained_aliases_cannot_restart_total_retention_or_change_original_coordinates()
    {
        var (original, decision, sanction) = Original();
        var firstOwner = Guid.CreateVersion7();
        var firstBinding = ModerationRecognitionContinuityBinding.Bind(Guid.CreateVersion7(), firstOwner,
            original, decision, sanction, Now);
        var first = Recapture(original, firstBinding, decision, sanction, Pending(firstOwner, Now.AddHours(1)));
        var secondOwner = Guid.CreateVersion7();
        var nextBinding = ModerationRecognitionContinuityBinding.Bind(Guid.CreateVersion7(), secondOwner,
            first, decision, sanction, Now.AddHours(2));
        var second = Recapture(first, nextBinding, decision, sanction, Pending(secondOwner, Now.AddHours(3)));
        await Assert.That(second.PreservesOriginalContribution(first)).IsTrue();
        await Assert.That(second.PreservesOriginalContribution(original)).IsTrue();
        await Assert.That(second.RetainUntil).IsEqualTo(Now.AddDays(3));
        await Assert.That(second.OriginalCapturedAt).IsEqualTo(Now);
        await Assert.That(second.EndsAt).IsEqualTo(Now.AddDays(2));
        await Assert.That(second.ReviewAt).IsEqualTo(Now.AddHours(12));
        await Assert.That(second.ParentCaptureItemId).IsEqualTo(first.Id);
    }

    [Test]
    [Arguments("owner")]
    [Arguments("binding")]
    [Arguments("dropped")]
    [Arguments("reversed")]
    [Arguments("expired")]
    public async Task Recapture_rejects_transplanted_or_no_longer_effective_bound_authority(string defect)
    {
        var (original, decision, sanction) = Original();
        var fresh = Guid.CreateVersion7();
        var binding = ModerationRecognitionContinuityBinding.Bind(Guid.CreateVersion7(), fresh,
            original, decision, sanction, Now);
        if (defect == "dropped") original.Drop(Now);
        if (defect == "reversed") sanction.Revoke(sanction.Revision, Now);
        var pending = Pending(defect == "owner" ? Guid.CreateVersion7() : fresh,
            defect == "expired" ? Now.AddDays(2) : Now.AddHours(1));
        var other = defect == "binding" ? Original().Item : original;
        await Assert.That(() => Recapture(other, binding, decision, sanction, pending)).Throws<ArgumentException>();
    }

    [Test]
    public async Task Duplicate_alias_material_and_wrong_request_owner_cannot_receipt_capture()
    {
        var (original, decision, sanction) = Original();
        var fresh = Guid.CreateVersion7();
        var binding = ModerationRecognitionContinuityBinding.Bind(Guid.CreateVersion7(), fresh,
            original, decision, sanction, Now);
        var pending = Pending(fresh, Now.AddHours(1));
        var first = Recapture(original, binding, decision, sanction, pending);
        var duplicate = ModerationRecognitionCaptureItem.Recapture(Guid.CreateVersion7(), pending.IntentId,
            fresh, original, binding, decision, sanction, first.Token, first.KeyVerificationTag, 2, pending.ReceivedAt);
        await Assert.That(() => pending.Capture([first, duplicate], pending.ReceivedAt)).Throws<ArgumentException>();
        var unrelated = Pending(Guid.CreateVersion7(), pending.ReceivedAt);
        await Assert.That(() => unrelated.Capture([first], unrelated.ReceivedAt)).Throws<ArgumentException>();
        await Assert.That(pending.HasCompleteMaterial).IsFalse();
    }

    [Test]
    public async Task Fixed_retention_ceiling_can_precede_the_new_privacy_request_deadline()
    {
        var (original, decision, sanction) = Original();
        var fresh = Guid.CreateVersion7();
        var binding = ModerationRecognitionContinuityBinding.Bind(Guid.CreateVersion7(), fresh,
            original, decision, sanction, Now);
        var pending = ModerationRecognitionPending.Receive(Guid.CreateVersion7(), fresh, 1,
            PrivacyErasureReasonCode.AccountDeletion, Guid.CreateVersion7(), Guid.CreateVersion7(),
            Now.AddHours(1), Now.AddDays(4), Now.AddHours(2), Hash(), Now.AddDays(1));
        var item = Recapture(original, binding, decision, sanction, pending);
        pending.Capture([item], pending.ReceivedAt);
        await Assert.That(pending.HasCompleteMaterial).IsTrue();
        await Assert.That(item.RetainUntil).IsEqualTo(original.RetainUntil);
        await Assert.That(item.RetainUntil < pending.SettleBy).IsTrue();
    }

    private static ModerationRecognitionCaptureItem Recapture(ModerationRecognitionCaptureItem original,
        ModerationRecognitionContinuityBinding binding, ModerationDecision decision, ModerationSanction sanction,
        ModerationRecognitionPending pending) =>
        ModerationRecognitionCaptureItem.Recapture(Guid.CreateVersion7(), pending.IntentId, pending.SubjectId,
            original, binding, decision, sanction,
            new(original.TrustDomainId, AuthenticationProviderKind.Keycloak, original.Purpose, "next", 2, 1,
                Convert.ToHexStringLower(Hash())), Convert.ToHexStringLower(Hash()), 2, pending.ReceivedAt);

    private static ModerationRecognitionPending Pending(Guid owner, DateTimeOffset at) =>
        ModerationRecognitionPending.Receive(Guid.CreateVersion7(), owner, 1, PrivacyErasureReasonCode.AccountDeletion,
            Guid.CreateVersion7(), Guid.CreateVersion7(), at, at.AddHours(1), at.AddMinutes(5), Hash(), at.AddDays(1));

    private static byte[] Hash() => System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);

    private static (ModerationRecognitionCaptureItem Item, ModerationDecision Decision, ModerationSanction Sanction) Original()
    {
        var instance = AuthorityScope.Instance(AuthorityScope.Solution(
            AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7());
        var scope = AuthorityScope.Tenant(instance, Guid.CreateVersion7());
        var decision = ModerationDecision.Propose(Guid.CreateVersion7(), Guid.CreateVersion7(),
            SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), instance),
            SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), instance), instance, scope,
            ModerationRuleReference.Create(scope.Id, "recognition.recapture", 1), [ModerationCapability.Publish],
            Now, Now.AddDays(2), Now.AddHours(12), 1, Now);
        decision.RecordResponse(1, Now);
        decision.AcceptFinding(Guid.CreateVersion7(), 2, Now);
        decision.Approve(SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), instance), instance, 3, Now);
        var sanction = decision.Activate(Guid.CreateVersion7(), 4, Now);
        var purpose = ModerationReferenceRecognition.SeparatelyQualifiedFinalAction;
        var material = new ModerationRecognitionCapturedMaterial(
            new(scope.TrustDomainId, AuthenticationProviderKind.Keycloak, purpose, "original", 1, 1,
                Convert.ToHexStringLower(Hash())), Convert.ToHexStringLower(Hash()),
            ModerationRecognitionContribution.Capture(decision, sanction, purpose, Now, Now.AddDays(3)));
        return (ModerationRecognitionCaptureItem.Capture(Guid.CreateVersion7(), Guid.CreateVersion7(), decision,
            material, Guid.CreateVersion7(), 1, 3, Guid.CreateVersion7(), 1, 4, 1), decision, sanction);
    }
}
