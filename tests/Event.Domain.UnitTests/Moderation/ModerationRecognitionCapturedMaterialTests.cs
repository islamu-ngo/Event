using Explore.Domain.Enums;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;

namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationRecognitionCapturedMaterialTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Complete_material_preserves_original_token_and_only_qualified_purpose()
    {
        var (decision, sanction) = FinalContribution();
        var contribution = ModerationRecognitionContribution.Capture(decision, sanction,
            ModerationReferenceRecognition.SeparatelyQualifiedFinalAction, Now, Now.AddDays(3));
        var token = Token(contribution.Scope.TrustDomainId, contribution.Purpose);
        var material = new ModerationRecognitionCapturedMaterial(token, Tag(), contribution);
        await Assert.That(material.Token).IsEqualTo(token);
        await Assert.That(material.Contribution.Capabilities.ToArray())
            .IsEquivalentTo(new[] { ModerationCapability.Publish });
        await Assert.That(material.Contribution.EndsAt).IsEqualTo(Now.AddDays(2));
        await Assert.That(material.Contribution.MatchesCurrent(decision, sanction, Now)).IsTrue();
    }

    [Test]
    [Arguments("trust")]
    [Arguments("purpose")]
    [Arguments("tag-empty")]
    [Arguments("tag-case")]
    [Arguments("tag-length")]
    public async Task Cross_purpose_trust_or_invalid_material_identity_rejects_capture(string defect)
    {
        var (decision, sanction) = FinalContribution();
        var contribution = ModerationRecognitionContribution.Capture(decision, sanction,
            ModerationReferenceRecognition.SeparatelyQualifiedFinalAction, Now, Now.AddDays(3));
        var token = Token(defect == "trust" ? Guid.CreateVersion7() : contribution.Scope.TrustDomainId,
            defect == "purpose" ? ModerationReferenceRecognition.SeparatelyQualifiedFinalEnrollment : contribution.Purpose);
        var tag = defect switch
        {
            "tag-empty" => "",
            "tag-case" => new string('A', 64),
            "tag-length" => new string('a', 63),
            _ => Tag()
        };
        await Assert.That(() => new ModerationRecognitionCapturedMaterial(token, tag, contribution))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Matching_requires_original_material_and_current_contribution_without_widening_rights()
    {
        var (decision, sanction) = FinalContribution();
        var contribution = ModerationRecognitionContribution.Capture(decision, sanction,
            ModerationReferenceRecognition.SeparatelyQualifiedFinalAction, Now, Now.AddDays(3));
        var token = Token(contribution.Scope.TrustDomainId, contribution.Purpose);
        var tag = Tag();
        var material = new ModerationRecognitionCapturedMaterial(token, tag, contribution);
        await Assert.That(material.MatchesCurrent(token, tag, decision, sanction, Now)).IsTrue();
        var differentTag = (tag[0] == 'a' ? "b" : "a") + tag[1..];
        var differentDigest = (token.Digest[0] == 'a' ? "b" : "a") + token.Digest[1..];
        var differentToken = new ModerationRecognitionToken(token.TrustDomainId, token.ProviderKind,
            token.Purpose, token.KeyId, token.KeyGeneration, token.SerializationVersion, differentDigest);
        await Assert.That(material.MatchesCurrent(token, differentTag, decision, sanction, Now)).IsFalse();
        await Assert.That(material.MatchesCurrent(differentToken,
            tag, decision, sanction, Now)).IsFalse();
        await Assert.That(material.MatchesCurrent(token, tag, decision, sanction, Now.AddDays(2))).IsFalse();
        sanction.Revoke(1, Now.AddHours(1));
        await Assert.That(material.MatchesCurrent(token, tag, decision, sanction, Now.AddHours(1))).IsFalse();
    }

    [Test]
    [Arguments(ModerationReferenceRecognition.SeparatelyQualifiedFinalAction)]
    [Arguments(ModerationReferenceRecognition.SeparatelyQualifiedFinalEnrollment)]
    public async Task Exact_retention_expiry_stops_matching_before_the_original_sanction_ends(
        ModerationReferenceRecognition purpose)
    {
        var (decision, sanction) = FinalContribution();
        var retainUntil = Now.AddHours(1);
        var contribution = ModerationRecognitionContribution.Capture(decision, sanction, purpose, Now, retainUntil);
        var token = Token(contribution.Scope.TrustDomainId, purpose);
        var tag = Tag();
        var material = new ModerationRecognitionCapturedMaterial(token, tag, contribution);
        await Assert.That(material.MatchesCurrent(token, tag, decision, sanction, retainUntil.AddTicks(-1))).IsTrue();
        await Assert.That(material.MatchesCurrent(token, tag, decision, sanction, retainUntil)).IsFalse();
        await Assert.That(sanction.IsEffectiveAt(retainUntil)).IsTrue();
        await Assert.That(material.Contribution.RetainUntil).IsEqualTo(retainUntil);
        await Assert.That(material.Contribution.EndsAt).IsEqualTo(Now.AddDays(2));
    }

    private static string Tag() =>
        Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    private static ModerationRecognitionToken Token(Guid trust, ModerationReferenceRecognition purpose) =>
        new(trust, AuthenticationProviderKind.Keycloak, purpose, "original", 1, 1, Tag());

    private static (ModerationDecision Decision, ModerationSanction Sanction) FinalContribution()
    {
        var instance = AuthorityScope.Instance(AuthorityScope.Solution(
            AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7());
        var scope = AuthorityScope.Resource(AuthorityScope.Tenant(instance, Guid.CreateVersion7()), Guid.CreateVersion7());
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
