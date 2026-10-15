using ISLAMU.Moderation.Domain;

namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationCaseAttributionHeadTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task OpeningRetainsExactSubjectOccurrenceAndOpeningSource()
    {
        var fixture = new Fixture();
        var head = fixture.Open();
        await Assert.That(head.CaseId).IsEqualTo(fixture.Owner.Id);
        await Assert.That(head.OpeningSourceId).IsEqualTo(fixture.First.Id);
        await Assert.That(head.SourceId).IsEqualTo(fixture.First.Id);
        await Assert.That(head.OccurrenceCaseId).IsEqualTo(fixture.Root.Id);
        await Assert.That(head.OccurrenceId).IsEqualTo(fixture.Root.OccurrenceId);
        await Assert.That(head.SubjectId).IsEqualTo(fixture.Owner.SubjectId!.Value);
        await Assert.That(head.Generation).IsEqualTo(1L);
        await Assert.That(head.Revision).IsEqualTo(1L);
    }

    [Test]
    [Arguments("CaseId")]
    [Arguments("TenantId")]
    [Arguments("TrustDomainId")]
    [Arguments("HostBindingId")]
    [Arguments("OccurrenceCaseId")]
    [Arguments("OccurrenceId")]
    [Arguments("SubjectId")]
    public async Task OpeningRejectsForeignOwnership(string property)
    {
        var fixture = new Fixture();
        Set(fixture.First, property, Guid.CreateVersion7());
        await Assert.That(() => fixture.Open()).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task OpeningRejectsWrongEvidenceGenerationOrUnlinkedCase()
    {
        var fixture = new Fixture();
        Set(fixture.First, nameof(ModerationCaseAttributionSource.EvidenceCaseRevision), 2L);
        Set(fixture.First, nameof(ModerationCaseAttributionSource.OccurrenceCaseRevision), 2L);
        await Assert.That(() => fixture.Open()).Throws<InvalidOperationException>();
        Set(fixture.First, nameof(ModerationCaseAttributionSource.EvidenceCaseRevision), 1L);
        Set(fixture.First, nameof(ModerationCaseAttributionSource.Generation), 2L);
        await Assert.That(() => fixture.Open()).Throws<InvalidOperationException>();
        Set(fixture.First, nameof(ModerationCaseAttributionSource.Generation), 1L);
        var unlinked = ModerationCase.Open(Guid.CreateVersion7(), fixture.Owner.Subject!,
            fixture.Root.Scope, fixture.Root.OccurrenceId, Now);
        await Assert.That(() => ModerationCaseAttributionHead.Open(Guid.CreateVersion7(), unlinked,
            fixture.First, fixture.Host, Now)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ExpiredSourceCanAdvanceWithoutRewritingHistoryOrMultiplyingOccurrence()
    {
        var fixture = new Fixture();
        var head = fixture.Open();
        var deadline = fixture.First.ValidUntil;
        var next = fixture.Next(deadline);
        head.Advance(fixture.First.Id, 1, 1, fixture.Owner, fixture.First, next, fixture.Host, deadline);
        await Assert.That(head.Generation).IsEqualTo(2L);
        await Assert.That(head.Revision).IsEqualTo(2L);
        await Assert.That(head.SourceId).IsEqualTo(next.Id);
        await Assert.That(head.OpeningSourceId).IsEqualTo(fixture.First.Id);
        await Assert.That(fixture.First.ValidUntil).IsEqualTo(deadline);
        await Assert.That(fixture.First.Revision).IsEqualTo(1L);
        await Assert.That(fixture.First.WithdrawnAt).IsNull();
        await Assert.That(fixture.Owner.AttributionSourceId).IsEqualTo(fixture.First.Id);
        await Assert.That(fixture.Owner.OccurrenceEvidenceRevision).IsEqualTo(1L);
        await Assert.That(fixture.Owner.Revision).IsEqualTo(1L);
        await Assert.That(fixture.Root.Revision).IsEqualTo(1L);
        await Assert.That(fixture.Root.Subject).IsNull();
        await Assert.That(fixture.Root.Evidence.Count).IsEqualTo(1);
        await Assert.That(head.CaseId).IsEqualTo(fixture.Owner.Id);
        await Assert.That(head.OccurrenceId).IsEqualTo(fixture.Root.OccurrenceId);
    }

    [Test]
    [Arguments("MaterialFactId")]
    [Arguments("EvidenceSourceId")]
    [Arguments("EvidenceSourceVersion")]
    [Arguments("NativeResponsibilityDigest")]
    public async Task IndependentlyQualifiedNewFactsAllowLiveAdvancement(string property)
    {
        var fixture = new Fixture();
        var head = fixture.Open();
        var next = fixture.Next(Now.AddSeconds(1));
        Set(next, property, property == "NativeResponsibilityDigest" ? new string('d', 64) : (object)Guid.CreateVersion7());
        head.Advance(fixture.First.Id, 1, 1, fixture.Owner, fixture.First, next, fixture.Host, Now.AddSeconds(1));
        await Assert.That(head.SourceId).IsEqualTo(next.Id);
        await Assert.That(fixture.First.ValidUntil).IsEqualTo(Now.AddMinutes(1));
    }

    [Test]
    public async Task LiveUnchangedSourceCannotRenewThroughNewGrantOrEpoch()
    {
        var fixture = new Fixture();
        var head = fixture.Open();
        var next = fixture.Next(Now.AddSeconds(1));
        Set(next, nameof(ModerationCaseAttributionSource.QualificationEpochId), Guid.CreateVersion7());
        Set(next, nameof(ModerationCaseAttributionSource.QualifierRoleGrantId), Guid.CreateVersion7());
        await Assert.That(() => head.Advance(fixture.First.Id, 1, 1, fixture.Owner,
            fixture.First, next, fixture.Host, Now.AddSeconds(1))).Throws<InvalidOperationException>();
        await Assert.That(head.Revision).IsEqualTo(1L);
    }

    [Test]
    public async Task ExpectedSourceGenerationAndRevisionMustAllMatch()
    {
        var fixture = new Fixture();
        var head = fixture.Open();
        var next = fixture.Next(fixture.First.ValidUntil);
        await Assert.That(() => Advance(Guid.CreateVersion7(), 1, 1)).Throws<InvalidOperationException>();
        await Assert.That(() => Advance(fixture.First.Id, 2, 1)).Throws<InvalidOperationException>();
        await Assert.That(() => Advance(fixture.First.Id, 1, 2)).Throws<InvalidOperationException>();
        Advance(fixture.First.Id, 1, 1);
        await Assert.That(() => Advance(fixture.First.Id, 1, 1)).Throws<InvalidOperationException>();
        await Assert.That(head.Revision).IsEqualTo(2L);

        void Advance(Guid source, long generation, long revision) =>
            head.Advance(source, generation, revision, fixture.Owner, fixture.First, next, fixture.Host, next.QualifiedAt);
    }

    [Test]
    [Arguments(1L)]
    [Arguments(3L)]
    public async Task AdvancementRejectsNonconsecutiveGeneration(long generation)
    {
        var fixture = new Fixture();
        var head = fixture.Open();
        var next = fixture.Next(fixture.First.ValidUntil);
        Set(next, nameof(ModerationCaseAttributionSource.Generation), generation);
        await Assert.That(() => head.Advance(fixture.First.Id, 1, 1, fixture.Owner,
            fixture.First, next, fixture.Host, next.QualifiedAt)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task AdvancementRejectsReusedSourceIdentity()
    {
        var fixture = new Fixture();
        var head = fixture.Open();
        var next = fixture.Next(fixture.First.ValidUntil);
        Set(next, nameof(ModerationCaseAttributionSource.Id), fixture.First.Id);
        await Assert.That(() => head.Advance(fixture.First.Id, 1, 1, fixture.Owner,
            fixture.First, next, fixture.Host, next.QualifiedAt)).Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments("CaseId")]
    [Arguments("TenantId")]
    [Arguments("TrustDomainId")]
    [Arguments("HostBindingId")]
    [Arguments("OccurrenceCaseId")]
    [Arguments("OccurrenceId")]
    [Arguments("SubjectId")]
    public async Task AdvancementRejectsForeignSourceCoordinates(string property)
    {
        var fixture = new Fixture();
        var head = fixture.Open();
        var next = fixture.Next(fixture.First.ValidUntil);
        Set(next, property, Guid.CreateVersion7());
        await Assert.That(() => head.Advance(fixture.First.Id, 1, 1, fixture.Owner,
            fixture.First, next, fixture.Host, next.QualifiedAt)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ClosedCaseCannotOpenOrAdvanceHead()
    {
        var fixture = new Fixture();
        var head = fixture.Open();
        fixture.Owner.Close(1, Now.AddSeconds(1));
        await Assert.That(() => fixture.Open()).Throws<InvalidOperationException>();
        var next = fixture.Next(fixture.First.ValidUntil);
        await Assert.That(() => head.Advance(fixture.First.Id, 1, 1, fixture.Owner,
            fixture.First, next, fixture.Host, next.QualifiedAt)).Throws<InvalidOperationException>();
        await Assert.That(fixture.Owner.State).IsEqualTo(ModerationCaseState.Closed);
    }

    [Test]
    public async Task QualificationMustBeFreshUnwithdrawnAndEvaluatedAtUtcNow()
    {
        var fixture = new Fixture();
        var head = fixture.Open();
        var next = fixture.Next(fixture.First.ValidUntil);
        await Assert.That(() => head.Advance(fixture.First.Id, 1, 1, fixture.Owner,
            fixture.First, next, fixture.Host, next.QualifiedAt.AddSeconds(1))).Throws<InvalidOperationException>();
        await Assert.That(() => head.Advance(fixture.First.Id, 1, 1, fixture.Owner,
            fixture.First, next, fixture.Host, next.QualifiedAt.ToOffset(TimeSpan.FromHours(1)))).Throws<ArgumentException>();
        await Assert.That(() => head.Advance(fixture.First.Id, 1, 1, fixture.Owner,
            fixture.First, next, fixture.Host, DateTimeOffset.MaxValue)).Throws<ArgumentException>();
        next.Withdraw(Guid.CreateVersion7(), 1, Guid.CreateVersion7(), next.QualifiedAt);
        await Assert.That(() => head.Advance(fixture.First.Id, 1, 1, fixture.Owner,
            fixture.First, next, fixture.Host, next.QualifiedAt)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task WithdrawalHistorySurvivesRenewalButDoesNotByItselfPermitIt()
    {
        var fixture = new Fixture();
        var head = fixture.Open();
        fixture.First.Withdraw(Guid.CreateVersion7(), 1, Guid.CreateVersion7(), Now);
        var early = fixture.Next(Now.AddSeconds(1));
        await Assert.That(() => head.Advance(fixture.First.Id, 1, 1, fixture.Owner,
            fixture.First, early, fixture.Host, early.QualifiedAt)).Throws<InvalidOperationException>();
        var next = fixture.Next(fixture.First.ValidUntil);
        head.Advance(fixture.First.Id, 1, 1, fixture.Owner, fixture.First, next, fixture.Host, next.QualifiedAt);
        await Assert.That(fixture.First.WithdrawnAt).IsEqualTo((DateTimeOffset?)Now);
        await Assert.That(fixture.First.Revision).IsEqualTo(2L);
        await Assert.That(fixture.First.ValidUntil).IsEqualTo(Now.AddMinutes(1));
    }

    private static void Set(object row, string property, object value) =>
        row.GetType().GetProperty(property)!.SetValue(row, value);

    [Test]
    public async Task LaterGenerationCannotReuseOpeningIdentityOrCreateAnotherOccurrence()
    {
        var fixture = new Fixture();
        var head = fixture.Open();
        var id = head.Id;
        var second = fixture.Next(fixture.First.ValidUntil);
        head.Advance(fixture.First.Id, 1, 1, fixture.Owner, fixture.First, second, fixture.Host, second.QualifiedAt);
        var third = fixture.Next(second.ValidUntil);
        Set(third, nameof(ModerationCaseAttributionSource.Generation), 3L);
        var thirdId = third.Id;
        Set(third, nameof(ModerationCaseAttributionSource.Id), fixture.First.Id);
        await Assert.That(() => head.Advance(second.Id, 2, 2, fixture.Owner,
            second, third, fixture.Host, third.QualifiedAt)).Throws<InvalidOperationException>();
        Set(third, nameof(ModerationCaseAttributionSource.Id), thirdId);
        head.Advance(second.Id, 2, 2, fixture.Owner, second, third, fixture.Host, third.QualifiedAt);
        head.ValidatePersistedState();
        await Assert.That(head.Id).IsEqualTo(id);
        await Assert.That(head.Generation).IsEqualTo(3L);
        await Assert.That(head.CaseId).IsEqualTo(fixture.Owner.Id);
        await Assert.That(head.OccurrenceId).IsEqualTo(fixture.Root.OccurrenceId);
        await Assert.That(fixture.Owner.AttributionSourceId).IsEqualTo(fixture.First.Id);
        await Assert.That(fixture.Root.Evidence.Count).IsEqualTo(1);
        await Assert.That(second.ValidUntil).IsEqualTo(Now.AddMinutes(2));
    }

    [Test]
    public async Task ActualPriorAndOriginalOpeningLinkCannotBeSubstituted()
    {
        var fixture = new Fixture();
        var head = fixture.Open();
        var next = fixture.Next(fixture.First.ValidUntil);
        var impostor = fixture.Next(Now);
        Set(impostor, nameof(ModerationCaseAttributionSource.Generation), 1L);
        await Assert.That(() => head.Advance(fixture.First.Id, 1, 1, fixture.Owner,
            impostor, next, fixture.Host, next.QualifiedAt)).Throws<InvalidOperationException>();
        Set(fixture.Owner, nameof(ModerationCase.AttributionSourceId), next.Id);
        await Assert.That(() => head.Advance(fixture.First.Id, 1, 1, fixture.Owner,
            fixture.First, next, fixture.Host, next.QualifiedAt)).Throws<InvalidOperationException>();
        await Assert.That(head.SourceId).IsEqualTo(fixture.First.Id);
        await Assert.That(head.Revision).IsEqualTo(1L);
    }

    [Test]
    [Arguments(0L)]
    [Arguments(-1L)]
    public async Task QualifiedAttributionSourceRequiresPositiveGeneration(long generation)
    {
        var fixture = new Fixture();
        await Assert.That(() => fixture.Qualify(Now, generation)).Throws<ArgumentException>();
    }

    [Test]
    public async Task FreshQualifiedGenerationRetainsOriginalSourceAndOpeningLink()
    {
        var fixture = new Fixture();
        var until = fixture.First.ValidUntil;
        var next = fixture.Next(until);
        await Assert.That(next.Generation).IsEqualTo(2L);
        await Assert.That(next.Id).IsNotEqualTo(fixture.First.Id);
        await Assert.That(next.CaseId).IsEqualTo(fixture.First.CaseId);
        await Assert.That(fixture.First.Generation).IsEqualTo(1L);
        await Assert.That(fixture.First.ValidUntil).IsEqualTo(until);
        await Assert.That(fixture.Owner.AttributionSourceId).IsEqualTo(fixture.First.Id);
    }

    private sealed class Fixture
    {
        public ModerationHostBinding Host { get; }
        public ModerationCase Root { get; }
        public ModerationCase Owner { get; }
        public ModerationCaseAttributionSource First { get; }
        private readonly ModerationCaseTargetSource _material;
        private readonly ModerationQualificationEpoch _epoch;
        private readonly ModerationCaseTargetPolicy _policy;
        private readonly SubjectReference _subject;
        private readonly Guid _caseId = Guid.CreateVersion7();
        private readonly Guid _human = Guid.CreateVersion7();
        private readonly Guid _issuer = Guid.CreateVersion7();

        public Fixture()
        {
            Host = ModerationHostBinding.Establish(Guid.CreateVersion7(), Guid.CreateVersion7(),
                Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
            var scope = AuthorityScope.Tenant(AuthorityScope.Instance(AuthorityScope.Solution(
                AuthorityScope.TrustDomain(Host.TrustDomainId), Host.SolutionId), Host.InstanceId), Guid.CreateVersion7());
            _material = ModerationCaseTargetSource.Record(Guid.CreateVersion7(), Guid.CreateVersion7(),
                scope.Id, scope.TrustDomainId, Guid.CreateVersion7(), Guid.CreateVersion7(),
                ModerationCaseTargetKind.EventVersion, ModerationCaseTargetKind.EventVersion,
                Guid.CreateVersion7(), Guid.CreateVersion7(), new string('a', 64), Guid.CreateVersion7(),
                Guid.CreateVersion7(), Guid.CreateVersion7(), new string('b', 64), new string('c', 64), Now);
            Root = ModerationCase.OpenAllegation(Guid.CreateVersion7(), scope, _material.Evidence(scope), Now);
            _epoch = ModerationQualificationEpoch.Initialize(Guid.CreateVersion7(), Host, new Tenant
            {
                Id = scope.Id, FullName = "Attribution jurisdiction", Slug = "attribution-jurisdiction",
                TenantStatus = new TenantStatus { Id = 1, MasterCode = "Active", FullName = "Active" }
            });
            _policy = ModerationCaseTargetPolicy.Adopt(Guid.CreateVersion7(), scope.Id, scope.TrustDomainId,
                _material.Kind, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5), _issuer);
            _subject = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), scope.Parent!);
            First = Qualify(Now, 1);
            Owner = ModerationCase.OpenAttributed(_caseId, _subject, Root, 1, First.Id, Now);
        }

        public ModerationCaseAttributionHead Open() =>
            ModerationCaseAttributionHead.Open(Guid.CreateVersion7(), Owner, First, Host, Now);

        public ModerationCaseAttributionSource Next(DateTimeOffset now) => Qualify(now, 2);

        public ModerationCaseAttributionSource Qualify(DateTimeOffset now, long generation)
        {
            var grant = ModerationCaseReadGrant.Grant(Guid.CreateVersion7(), Root.Scope.Id, Root.Scope.TrustDomainId,
                Root.Id, Root.Revision, _material.Id, _human, _issuer, Guid.CreateVersion7(), false,
                now, now.AddMinutes(2));
            return ModerationCaseAttributionSource.Qualify(Guid.CreateVersion7(), _caseId, generation, Root, 1, _material,
                _subject, ModerationResponsibilityBasis.EventSubmission, Host, _epoch, grant, _policy,
                Guid.CreateVersion7(), false, new string('a', 64), new string('d', 64), now, now.AddMinutes(1));
        }
    }
}
