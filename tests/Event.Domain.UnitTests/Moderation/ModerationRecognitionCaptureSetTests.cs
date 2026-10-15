using System.Security.Cryptography;
using Explore.Domain.Enums;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;

namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationRecognitionCaptureSetTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Complete_capture_snapshots_both_purposes_without_profile_inputs()
    {
        var (decision, sanction) = FinalContribution();
        var tag = Digest();
        var action = Material(decision, sanction, tag, ModerationReferenceRecognition.SeparatelyQualifiedFinalAction);
        var enrollment = Material(decision, sanction, tag, ModerationReferenceRecognition.SeparatelyQualifiedFinalEnrollment);
        var items = new List<ModerationRecognitionCapturedMaterial> { action, enrollment };
        var set = new ModerationRecognitionCaptureSet(Guid.CreateVersion7(), decision.Scope.TrustDomainId,
            3, Now, Now.AddHours(1), items);
        items.Clear();
        await Assert.That(set.Material.Length).IsEqualTo(2);
        await Assert.That(set.Material[0]).IsEqualTo(action);
        await Assert.That(set.Material[1]).IsEqualTo(enrollment);
        await Assert.That(set.CapturedAt).IsEqualTo(Now);
    }

    [Test]
    [Arguments("empty")]
    [Arguments("duplicate")]
    [Arguments("mixed-material")]
    [Arguments("mixed-key")]
    [Arguments("foreign-trust")]
    [Arguments("capture-time")]
    [Arguments("deadline")]
    [Arguments("infinite")]
    [Arguments("generation")]
    [Arguments("retention")]
    public async Task Partial_ambiguous_or_foreign_capture_cannot_become_a_complete_set(string defect)
    {
        var (decision, sanction) = FinalContribution();
        var tag = Digest();
        var action = Material(decision, sanction, tag, ModerationReferenceRecognition.SeparatelyQualifiedFinalAction);
        var enrollment = Material(decision, sanction, tag, ModerationReferenceRecognition.SeparatelyQualifiedFinalEnrollment);
        var differentTag = (tag[0] == 'a' ? "b" : "a") + tag[1..];
        var items = new List<ModerationRecognitionCapturedMaterial> { action, enrollment };
        if (defect == "empty") items.Clear();
        if (defect == "duplicate") items.Add(action);
        if (defect == "mixed-material")
            items[1] = new(enrollment.Token, differentTag, enrollment.Contribution);
        if (defect == "mixed-key")
            items[1] = new(new(enrollment.Token.TrustDomainId, enrollment.Token.ProviderKind,
                enrollment.Token.Purpose, "other", 2, 1, enrollment.Token.Digest), tag, enrollment.Contribution);
        await Assert.That(() => new ModerationRecognitionCaptureSet(Guid.CreateVersion7(),
            defect == "foreign-trust" ? Guid.CreateVersion7() : decision.Scope.TrustDomainId,
            defect == "generation" ? long.MaxValue : 3,
            defect == "capture-time" ? Now.AddTicks(1) : Now,
            defect == "deadline" ? Now : defect == "infinite" ? DateTimeOffset.MaxValue
                : defect == "retention" ? Now.AddDays(3).AddTicks(1) : Now.AddHours(1),
            items)).Throws<ArgumentException>();
    }

    private static string Digest() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    private static ModerationRecognitionCapturedMaterial Material(ModerationDecision decision, ModerationSanction sanction,
        string tag, ModerationReferenceRecognition purpose)
    {
        var contribution = ModerationRecognitionContribution.Capture(decision, sanction, purpose, Now, Now.AddDays(3));
        return new(new(decision.Scope.TrustDomainId, AuthenticationProviderKind.Keycloak, purpose,
            "original", 1, 1, Digest()), tag, contribution);
    }

    private static (ModerationDecision Decision, ModerationSanction Sanction) FinalContribution()
    {
        var instance = AuthorityScope.Instance(AuthorityScope.Solution(
            AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7());
        var scope = AuthorityScope.Tenant(instance, Guid.CreateVersion7());
        var decision = ModerationDecision.Propose(Guid.CreateVersion7(), Guid.CreateVersion7(),
            SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), instance),
            SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), instance),
            instance, scope, ModerationRuleReference.Create(scope.Id, "recognition.fixture", 1),
            [ModerationCapability.Publish, ModerationCapability.Admission], Now, Now.AddDays(2), null, 1, Now);
        decision.RecordResponse(1, Now);
        decision.AcceptFinding(Guid.CreateVersion7(), 2, Now);
        decision.Approve(SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), instance),
            instance, 3, Now);
        return (decision, decision.Activate(Guid.CreateVersion7(), 4, Now));
    }
}
