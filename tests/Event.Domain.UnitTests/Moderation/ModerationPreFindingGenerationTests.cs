using ISLAMU.Moderation.Domain;

namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationPreFindingGenerationTests
{
    private static readonly DateTimeOffset Now = ModerationPreFindingGenerationFixture.Now;

    [Test]
    public async Task Current_generation_two_pins_new_evidence_without_rewriting_or_refreshing_history()
    {
        var fixture = new ModerationPreFindingGenerationFixture();
        var oldCustody = fixture.Propose(fixture.First, Now);
        oldCustody.Accept(oldCustody.PrivacyOwnerHumanId, 1, Now);
        var oldNotice = fixture.Issue(fixture.First, oldCustody, Now);
        oldNotice.Respond(Guid.CreateVersion7(), fixture.Recipient.HumanId, 1, [9], Now.AddSeconds(1));
        var oldDeadline = oldNotice.RetainUntil;
        var oldCustodyDeadline = oldCustody.ValidUntil;
        var next = fixture.Next();
        var custody = fixture.Propose(next, Now.AddMinutes(1));
        custody.Accept(custody.PrivacyOwnerHumanId, 1, Now.AddMinutes(1));
        var notice = fixture.Issue(next, custody, Now.AddMinutes(1));

        await Assert.That(fixture.Owner.AttributionSourceId).IsEqualTo(fixture.First.Id);
        await Assert.That(fixture.Owner.OccurrenceEvidenceRevision).IsEqualTo((long?)1);
        await Assert.That(custody.AttributionSourceId).IsEqualTo(next.Id);
        await Assert.That(custody.AttributionGeneration).IsEqualTo(2L);
        await Assert.That(custody.EvidenceRevision).IsEqualTo(2L);
        await Assert.That(notice.AttributionSourceId).IsEqualTo(next.Id);
        await Assert.That(notice.AttributionGeneration).IsEqualTo(2L);
        await Assert.That(notice.EvidenceRevision).IsEqualTo(2L);
        await Assert.That(notice.CaseRevision).IsEqualTo(1L);
        await Assert.That(oldNotice.AttributionGeneration).IsEqualTo(1L);
        await Assert.That(oldNotice.EvidenceRevision).IsEqualTo(1L);
        await Assert.That(oldCustody.AttributionSourceId).IsEqualTo(fixture.First.Id);
        await Assert.That(oldCustody.AttributionGeneration).IsEqualTo(1L);
        await Assert.That(oldCustody.EvidenceRevision).IsEqualTo(1L);
        await Assert.That(fixture.First.ValidUntil).IsEqualTo(Now.AddHours(1));
        await Assert.That(oldNotice.RetainUntil).IsEqualTo(oldDeadline);
        await Assert.That(oldCustody.ValidUntil).IsEqualTo(oldCustodyDeadline);
        await Assert.That(oldNotice.ProtectedNotice.ToArray()).IsEquivalentTo(new byte[] { 1, 2, 3 });
        await Assert.That(oldNotice.ProtectedResponse.ToArray()).IsEquivalentTo(new byte[] { 9 });
        oldNotice.Purge(2, Now.AddMinutes(1), withdrawn: true);
        await Assert.That(oldNotice.ProtectedNotice.IsEmpty).IsTrue();
        await Assert.That(oldNotice.ProtectedResponse.IsEmpty).IsTrue();
        await Assert.That(notice.ProtectedNotice.ToArray()).IsEquivalentTo(new byte[] { 1, 2, 3 });
    }

    [Test]
    [Arguments(nameof(ModerationCaseAttributionSource.CaseId))]
    [Arguments(nameof(ModerationCaseAttributionSource.OccurrenceCaseId))]
    [Arguments(nameof(ModerationCaseAttributionSource.OccurrenceId))]
    [Arguments(nameof(ModerationCaseAttributionSource.SubjectId))]
    [Arguments(nameof(ModerationCaseAttributionSource.TenantId))]
    [Arguments(nameof(ModerationCaseAttributionSource.TrustDomainId))]
    [Arguments(nameof(ModerationCaseAttributionSource.HostBindingId))]
    public async Task Another_source_ancestry_cannot_propose_or_issue(string property)
    {
        var fixture = new ModerationPreFindingGenerationFixture();
        var custody = fixture.Propose(fixture.First, Now);
        custody.Accept(custody.PrivacyOwnerHumanId, 1, Now);
        Set(fixture.First, property, Guid.CreateVersion7());
        await Assert.That(() => fixture.Propose(fixture.First, Now)).Throws<ArgumentException>();
        await Assert.That(() => fixture.Issue(fixture.First, custody, Now)).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(nameof(ModerationCaseAttributionSource.Id))]
    [Arguments(nameof(ModerationCaseAttributionSource.Generation))]
    [Arguments(nameof(ModerationCaseAttributionSource.EvidenceCaseRevision))]
    public async Task Notice_refuses_a_different_source_generation_or_evidence_pin(string property)
    {
        var fixture = new ModerationPreFindingGenerationFixture();
        var next = fixture.Next();
        var custody = fixture.Propose(next, Now.AddMinutes(1));
        custody.Accept(custody.PrivacyOwnerHumanId, 1, Now.AddMinutes(1));
        Set(next, property, property == nameof(ModerationCaseAttributionSource.Id)
            ? (object)Guid.CreateVersion7() : 1L);
        await Assert.That(() => fixture.Issue(next, custody, Now.AddMinutes(1))).Throws<ArgumentException>();
        await Assert.That(() => fixture.Issue(fixture.First, custody, Now.AddMinutes(1))).Throws<ArgumentException>();
    }

    [Test]
    [Arguments("withdrawn")]
    [Arguments("expired")]
    [Arguments("future")]
    public async Task Historical_or_not_yet_current_source_cannot_propose_or_issue(string state)
    {
        var fixture = new ModerationPreFindingGenerationFixture();
        var custody = fixture.Propose(fixture.First, Now);
        custody.Accept(custody.PrivacyOwnerHumanId, 1, Now);
        var at = Now;
        if (state == "withdrawn")
            fixture.First.Withdraw(Guid.CreateVersion7(), 1, Guid.CreateVersion7(), Now);
        else if (state == "expired")
            at = fixture.First.ValidUntil;
        else
            Set(fixture.First, nameof(ModerationCaseAttributionSource.QualifiedAt), Now.AddSeconds(1));
        await Assert.That(() => fixture.Propose(fixture.First, at)).Throws<ArgumentException>();
        await Assert.That(() => fixture.Issue(fixture.First, custody, at)).Throws<ArgumentException>();
    }

    [Test]
    public async Task Source_evidence_cannot_regress_below_the_original_opening_evidence()
    {
        var fixture = new ModerationPreFindingGenerationFixture();
        var next = fixture.Next();
        var owner = ModerationCase.OpenAttributed(fixture.Owner.Id, fixture.Owner.Subject!, fixture.Root,
            2, next.Id, Now.AddMinutes(1));
        await Assert.That(() => ModerationPreFindingCustody.Propose(Guid.CreateVersion7(), fixture.Host.Id,
            owner, fixture.First, Guid.CreateVersion7(), Guid.CreateVersion7(),
            ModerationPreFindingGenerationFixture.Terms(), Now.AddMinutes(1), Now.AddMinutes(3)))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Notice_requires_the_exact_open_owner_revision()
    {
        var fixture = new ModerationPreFindingGenerationFixture();
        var custody = fixture.Propose(fixture.First, Now);
        custody.Accept(custody.PrivacyOwnerHumanId, 1, Now);
        Set(custody, nameof(ModerationPreFindingCustody.CaseRevision), 2L);
        await Assert.That(() => fixture.Issue(fixture.First, custody, Now)).Throws<ArgumentException>();
        fixture.Owner.Close(1, Now);
        await Assert.That(() => fixture.Propose(fixture.First, Now)).Throws<ArgumentException>();
        await Assert.That(() => fixture.Issue(fixture.First, custody, Now)).Throws<ArgumentException>();
    }

    [Test]
    public async Task Custody_and_notice_deadlines_cannot_outlive_the_qualified_source()
    {
        var fixture = new ModerationPreFindingGenerationFixture();
        await Assert.That(() => ModerationPreFindingCustody.Propose(Guid.CreateVersion7(), fixture.Host.Id,
            fixture.Owner, fixture.First, Guid.CreateVersion7(), Guid.CreateVersion7(),
            ModerationPreFindingGenerationFixture.Terms(), Now, fixture.First.ValidUntil.AddTicks(1)))
            .Throws<ArgumentException>();
        var custody = fixture.Propose(fixture.First, Now);
        custody.Accept(custody.PrivacyOwnerHumanId, 1, Now);
        Set(custody, nameof(ModerationPreFindingCustody.ValidUntil), fixture.First.ValidUntil.AddMinutes(1));
        await Assert.That(() => ModerationPreFindingNotice.Issue(Guid.CreateVersion7(), fixture.Owner,
            fixture.First, custody, Guid.CreateVersion7(), Guid.CreateVersion7(), 1, fixture.Recipient, [1],
            Now, fixture.First.ValidUntil.AddTicks(1))).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Withdrawn_or_expired_custody_cannot_issue_against_a_current_source(bool withdrawn)
    {
        var fixture = new ModerationPreFindingGenerationFixture();
        var custody = fixture.Propose(fixture.First, Now, Now.AddMinutes(2));
        custody.Accept(custody.PrivacyOwnerHumanId, 1, Now);
        if (withdrawn)
            custody.Withdraw(custody.PrivacyOwnerHumanId, 2, Now);
        await Assert.That(() => fixture.Issue(fixture.First, custody, withdrawn ? Now : Now.AddMinutes(2)))
            .Throws<ArgumentException>();
    }

    [Test]
    [Arguments("now")]
    [Arguments("until")]
    [Arguments("before-owner")]
    public async Task Proposal_requires_a_current_UTC_interval(string invalid)
    {
        var fixture = new ModerationPreFindingGenerationFixture();
        var at = invalid == "now" ? Now.ToOffset(TimeSpan.FromHours(1))
            : invalid == "before-owner" ? Now.AddTicks(-1) : Now;
        var until = invalid == "until" ? Now.AddMinutes(3).ToOffset(TimeSpan.FromHours(1))
            : Now.AddMinutes(3);
        await Assert.That(() => fixture.Propose(fixture.First, at, until)).Throws<ArgumentException>();
    }

    [Test]
    [Arguments("custody", "AttributionGeneration", 0L)]
    [Arguments("custody", "AttributionGeneration", -1L)]
    [Arguments("custody", "EvidenceRevision", 0L)]
    [Arguments("custody", "EvidenceRevision", -1L)]
    [Arguments("notice", "AttributionGeneration", 0L)]
    [Arguments("notice", "AttributionGeneration", -1L)]
    [Arguments("notice", "EvidenceRevision", 0L)]
    [Arguments("notice", "EvidenceRevision", -1L)]
    public async Task Persisted_generation_and_evidence_pins_must_be_positive(string kind, string property, long value)
    {
        var fixture = new ModerationPreFindingGenerationFixture();
        var custody = fixture.Propose(fixture.First, Now);
        custody.Accept(custody.PrivacyOwnerHumanId, 1, Now);
        if (kind == "custody")
        {
            Set(custody, property, value);
            await Assert.That(custody.ValidatePersistedState).Throws<ArgumentException>();
        }
        else
        {
            var notice = fixture.Issue(fixture.First, custody, Now);
            Set(notice, property, value);
            await Assert.That(notice.ValidatePersistedState).Throws<ArgumentException>();
        }
    }

    private static void Set(object row, string property, object value) =>
        row.GetType().GetProperty(property)!.SetValue(row, value);
}

internal sealed class ModerationPreFindingGenerationFixture
{
    internal static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
    internal ModerationHostBinding Host { get; }
    internal ModerationCase Root { get; }
    internal ModerationCase Owner { get; }
    internal ModerationCaseAttributionSource First { get; }
    internal ModerationNoticeContentRepresentative Recipient { get; } =
        new(Guid.CreateVersion7(), 1, Guid.CreateVersion7());
    private readonly ModerationCaseTargetSource material;
    private readonly ModerationQualificationEpoch epoch;
    private readonly ModerationCaseTargetPolicy policy;
    private readonly SubjectReference subject;
    private readonly Guid caseId = Guid.CreateVersion7();
    private readonly Guid human = Guid.CreateVersion7();
    private readonly Guid issuer = Guid.CreateVersion7();

    internal ModerationPreFindingGenerationFixture()
    {
        Host = ModerationHostBinding.Establish(Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var scope = AuthorityScope.Tenant(AuthorityScope.Instance(AuthorityScope.Solution(
            AuthorityScope.TrustDomain(Host.TrustDomainId), Host.SolutionId), Host.InstanceId), Guid.CreateVersion7());
        material = ModerationCaseTargetSource.Record(Guid.CreateVersion7(), Guid.CreateVersion7(),
            scope.Id, scope.TrustDomainId, Guid.CreateVersion7(), Guid.CreateVersion7(),
            ModerationCaseTargetKind.EventVersion, ModerationCaseTargetKind.EventVersion,
            Guid.CreateVersion7(), Guid.CreateVersion7(), new string('a', 64), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), new string('b', 64), new string('c', 64), Now);
        Root = ModerationCase.OpenAllegation(Guid.CreateVersion7(), scope, material.Evidence(scope), Now);
        epoch = ModerationQualificationEpoch.Initialize(Guid.CreateVersion7(), Host, new Tenant
        {
            Id = scope.Id, FullName = "Notice jurisdiction", Slug = "notice-jurisdiction",
            TenantStatus = new TenantStatus { Id = 1, MasterCode = "Active", FullName = "Active" }
        });
        policy = ModerationCaseTargetPolicy.Adopt(Guid.CreateVersion7(), scope.Id, scope.TrustDomainId,
            material.Kind, TimeSpan.FromHours(2), TimeSpan.FromHours(2), issuer);
        subject = SubjectReference.Create(ModerationSubjectKind.Content, material.NativeTargetId, scope);
        First = Qualify(material, 1, 1, Now);
        Owner = ModerationCase.OpenAttributed(caseId, subject, Root, 1, First.Id, Now);
    }

    internal ModerationCaseAttributionSource Next()
    {
        var at = Now.AddMinutes(1);
        var nextMaterial = ModerationCaseTargetSource.Record(Guid.CreateVersion7(), Guid.CreateVersion7(),
            material.TenantId, material.TrustDomainId, material.EventId, material.EventVersion,
            material.Kind, material.NativeKind, material.NativeTargetId, material.NativeTargetVersion,
            material.NativeFactsDigest, material.OccurrenceId, material.OccurrenceVersion, Guid.CreateVersion7(),
            material.OccurrenceDigest, new string('e', 64), at);
        Root.RecordEvidence(nextMaterial.Evidence(Root.Scope), 1, at);
        return Qualify(nextMaterial, 2, 2, at);
    }

    private ModerationCaseAttributionSource Qualify(
        ModerationCaseTargetSource source, long generation, long evidenceRevision, DateTimeOffset at)
    {
        var grant = ModerationCaseReadGrant.Grant(Guid.CreateVersion7(), Root.Scope.Id, Root.Scope.TrustDomainId,
            Root.Id, Root.Revision, source.Id, human, issuer, Guid.CreateVersion7(), false, at, at.AddHours(2));
        return ModerationCaseAttributionSource.Qualify(Guid.CreateVersion7(), caseId, generation, Root,
            evidenceRevision, source, subject, ModerationResponsibilityBasis.EventContent, Host, epoch,
            grant, policy, Guid.CreateVersion7(), false, new string('a', 64), new string('d', 64),
            at, at.AddHours(1));
    }

    internal static ModerationPreFindingCustodyTerms Terms() =>
        new(TimeSpan.FromHours(1), 8192, "Private pre-finding notice and voluntary response.",
            "Independently accepted private access and finite deletion obligation.");

    internal ModerationPreFindingCustody Propose(
        ModerationCaseAttributionSource source, DateTimeOffset at, DateTimeOffset? until = null) =>
        ModerationPreFindingCustody.Propose(Guid.CreateVersion7(), Host.Id, Owner, source,
            Guid.CreateVersion7(), Guid.CreateVersion7(), Terms(), at, until ?? source.ValidUntil);

    internal ModerationPreFindingNotice Issue(
        ModerationCaseAttributionSource source, ModerationPreFindingCustody custody, DateTimeOffset at) =>
        ModerationPreFindingNotice.Issue(Guid.CreateVersion7(), Owner, source, custody, Guid.CreateVersion7(),
            Guid.CreateVersion7(), 1, Recipient, [1, 2, 3], at, at.AddMinutes(30));
}
