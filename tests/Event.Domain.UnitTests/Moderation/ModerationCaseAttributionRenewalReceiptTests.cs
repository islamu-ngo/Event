using ISLAMU.Moderation.Domain;

namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationCaseAttributionRenewalReceiptTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task RecordingCapturesExpectedTupleWithoutAdvancingOrRewritingHistory()
    {
        var fixture = new Fixture();
        var next = fixture.Qualify(fixture.First.ValidUntil, 2);
        var receipt = ModerationCaseAttributionRenewalReceipt.Record(
            fixture.Owner, fixture.Head, next, fixture.Host, next.QualifiedAt);
        receipt.ValidatePersistedState();
        await Assert.That(receipt.Id).IsEqualTo(next.Id);
        await Assert.That(receipt.CaseId).IsEqualTo(fixture.Owner.Id);
        await Assert.That(receipt.HeadId).IsEqualTo(fixture.Head.Id);
        await Assert.That(receipt.TenantId).IsEqualTo(fixture.Root.Scope.Id);
        await Assert.That(receipt.TrustDomainId).IsEqualTo(fixture.Host.TrustDomainId);
        await Assert.That(receipt.ExpectedCaseRevision).IsEqualTo(1L);
        await Assert.That(receipt.ExpectedSourceId).IsEqualTo(fixture.First.Id);
        await Assert.That(receipt.ExpectedGeneration).IsEqualTo(1L);
        await Assert.That(receipt.ExpectedHeadRevision).IsEqualTo(1L);
        await Assert.That(receipt.QualifiedAt).IsEqualTo(next.QualifiedAt);
        await Assert.That(fixture.Head.SourceId).IsEqualTo(fixture.First.Id);
        await Assert.That(fixture.Head.Generation).IsEqualTo(1L);
        await Assert.That(fixture.Head.Revision).IsEqualTo(1L);
        await Assert.That(fixture.Owner.AttributionSourceId).IsEqualTo(fixture.First.Id);
        await Assert.That(fixture.Owner.OccurrenceEvidenceRevision).IsEqualTo(1L);
        await Assert.That(fixture.Owner.Revision).IsEqualTo(1L);
        await Assert.That(fixture.First.ValidUntil).IsEqualTo(Now.AddMinutes(1));
        await Assert.That(fixture.First.Revision).IsEqualTo(1L);
        await Assert.That(fixture.First.WithdrawnAt).IsNull();
        await Assert.That(next.Generation).IsEqualTo(2L);
        await Assert.That(next.Revision).IsEqualTo(1L);
        await Assert.That(next.WithdrawnAt).IsNull();
        await Assert.That(fixture.Root.Evidence.Count).IsEqualTo(1);
        await Assert.That(fixture.Root.Revision).IsEqualTo(1L);
        await Assert.That(receipt.ToString().Contains(fixture.Owner.SubjectId!.Value.ToString(),
            StringComparison.Ordinal)).IsFalse();
        await Assert.That(receipt.ToString().Contains(fixture.First.NativeResponsibilityDigest,
            StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task LaterReceiptCapturesCurrentHeadRatherThanOriginalOpeningSource()
    {
        var fixture = new Fixture();
        var second = fixture.Qualify(fixture.First.ValidUntil, 2);
        fixture.Head.Advance(fixture.First.Id, 1, 1, fixture.Owner,
            fixture.First, second, fixture.Host, second.QualifiedAt);
        var third = fixture.Qualify(second.ValidUntil, 3);
        var receipt = ModerationCaseAttributionRenewalReceipt.Record(
            fixture.Owner, fixture.Head, third, fixture.Host, third.QualifiedAt);
        await Assert.That(receipt.Id).IsEqualTo(third.Id);
        await Assert.That(receipt.ExpectedSourceId).IsEqualTo(second.Id);
        await Assert.That(receipt.ExpectedGeneration).IsEqualTo(2L);
        await Assert.That(receipt.ExpectedHeadRevision).IsEqualTo(2L);
        await Assert.That(receipt.ExpectedCaseRevision).IsEqualTo(1L);
        await Assert.That(fixture.Head.SourceId).IsEqualTo(second.Id);
        await Assert.That(fixture.Head.OpeningSourceId).IsEqualTo(fixture.First.Id);
        await Assert.That(fixture.Owner.AttributionSourceId).IsEqualTo(fixture.First.Id);
        await Assert.That(second.ValidUntil).IsEqualTo(Now.AddMinutes(2));
    }

    [Test]
    [Arguments("Identity")]
    [Arguments("OpeningSource")]
    [Arguments("Subject")]
    [Arguments("Occurrence")]
    [Arguments("Unattributed")]
    public async Task RecordingRejectsSubstitutedOwner(string change)
    {
        var fixture = new Fixture();
        var next = fixture.Qualify(fixture.First.ValidUntil, 2);
        var subject = change == "Subject"
            ? SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), fixture.Root.Scope.Parent!)
            : fixture.Owner.Subject!;
        var root = change == "Occurrence" ? new Fixture(fixture.Host, fixture.Root.Scope).Root : fixture.Root;
        var owner = change == "Unattributed"
            ? ModerationCase.Open(fixture.Owner.Id, subject, root.Scope, root.OccurrenceId, Now)
            : ModerationCase.OpenAttributed(
                change == "Identity" ? Guid.CreateVersion7() : fixture.Owner.Id,
                subject, root, 1, change == "OpeningSource" ? next.Id : fixture.First.Id, Now);
        await Assert.That(() => ModerationCaseAttributionRenewalReceipt.Record(
            owner, fixture.Head, next, fixture.Host, next.QualifiedAt)).Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments("Tenant")]
    [Arguments("TrustDomain")]
    [Arguments("Solution")]
    [Arguments("Instance")]
    public async Task RecordingRejectsForeignOwnerEnvironment(string change)
    {
        var fixture = new Fixture();
        var host = ModerationHostBinding.Establish(Guid.CreateVersion7(),
            change == "TrustDomain" ? Guid.CreateVersion7() : fixture.Host.TrustDomainId,
            change == "Solution" ? Guid.CreateVersion7() : fixture.Host.SolutionId,
            change == "Instance" ? Guid.CreateVersion7() : fixture.Host.InstanceId,
            Guid.CreateVersion7());
        var scope = AuthorityScope.Tenant(AuthorityScope.Instance(AuthorityScope.Solution(
            AuthorityScope.TrustDomain(host.TrustDomainId), host.SolutionId), host.InstanceId),
            change == "Tenant" ? Guid.CreateVersion7() : fixture.Root.Scope.Id);
        var foreign = new Fixture(host, scope);
        var owner = ModerationCase.OpenAttributed(fixture.Owner.Id, foreign.Owner.Subject!,
            foreign.Root, 1, fixture.First.Id, Now);
        var next = fixture.Qualify(fixture.First.ValidUntil, 2);
        await Assert.That(() => ModerationCaseAttributionRenewalReceipt.Record(
            owner, fixture.Head, next, fixture.Host, next.QualifiedAt)).Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments("Case")]
    [Arguments("Subject")]
    [Arguments("Host")]
    [Arguments("Occurrence")]
    [Arguments("Environment")]
    public async Task RecordingRejectsForeignSource(string change)
    {
        var fixture = new Fixture();
        var now = fixture.First.ValidUntil;
        var other = new Fixture(fixture.Host, fixture.Root.Scope);
        var source = change switch
        {
            "Case" => fixture.Qualify(now, 2, caseId: Guid.CreateVersion7()),
            "Subject" => fixture.Qualify(now, 2, subject: SubjectReference.Create(
                ModerationSubjectKind.Human, Guid.CreateVersion7(), fixture.Root.Scope.Parent!)),
            "Host" => fixture.Qualify(now, 2, host: ModerationHostBinding.Establish(
                Guid.CreateVersion7(), fixture.Host.TrustDomainId, fixture.Host.SolutionId,
                fixture.Host.InstanceId, Guid.CreateVersion7())),
            "Occurrence" => other.Qualify(now, 2, caseId: fixture.Owner.Id, subject: fixture.Owner.Subject),
            _ => new Fixture().Qualify(now, 2, caseId: fixture.Owner.Id)
        };
        await Assert.That(() => ModerationCaseAttributionRenewalReceipt.Record(
            fixture.Owner, fixture.Head, source, fixture.Host, now)).Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments("Identity")]
    [Arguments("TrustDomain")]
    [Arguments("Solution")]
    [Arguments("Instance")]
    public async Task RecordingRejectsForeignHostAncestry(string change)
    {
        var fixture = new Fixture();
        var host = ModerationHostBinding.Establish(
            change == "Identity" ? Guid.CreateVersion7() : fixture.Host.Id,
            change == "TrustDomain" ? Guid.CreateVersion7() : fixture.Host.TrustDomainId,
            change == "Solution" ? Guid.CreateVersion7() : fixture.Host.SolutionId,
            change == "Instance" ? Guid.CreateVersion7() : fixture.Host.InstanceId,
            fixture.Host.MaintenanceServicePrincipalId);
        var next = fixture.Qualify(fixture.First.ValidUntil, 2);
        await Assert.That(() => ModerationCaseAttributionRenewalReceipt.Record(
            fixture.Owner, fixture.Head, next, host, next.QualifiedAt)).Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments(1L)]
    [Arguments(3L)]
    public async Task RecordingRejectsStaleOrSkippedGeneration(long generation)
    {
        var fixture = new Fixture();
        var next = fixture.Qualify(fixture.First.ValidUntil, generation);
        await Assert.That(() => ModerationCaseAttributionRenewalReceipt.Record(
            fixture.Owner, fixture.Head, next, fixture.Host, next.QualifiedAt)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task RecordingRejectsCurrentOrOriginalOpeningSourceIdentity()
    {
        var fixture = new Fixture();
        var second = fixture.Qualify(fixture.First.ValidUntil, 2);
        fixture.Head.Advance(fixture.First.Id, 1, 1, fixture.Owner,
            fixture.First, second, fixture.Host, second.QualifiedAt);
        var currentIdentity = fixture.Qualify(second.ValidUntil, 3, id: second.Id);
        var openingIdentity = fixture.Qualify(second.ValidUntil, 3, id: fixture.First.Id);
        await Assert.That(() => Record(currentIdentity)).Throws<InvalidOperationException>();
        await Assert.That(() => Record(openingIdentity)).Throws<InvalidOperationException>();

        ModerationCaseAttributionRenewalReceipt Record(ModerationCaseAttributionSource source) =>
            ModerationCaseAttributionRenewalReceipt.Record(
                fixture.Owner, fixture.Head, source, fixture.Host, source.QualifiedAt);
    }

    [Test]
    public async Task RecordingRejectsClosedCaseWithoutReopeningIt()
    {
        var fixture = new Fixture();
        fixture.Owner.Close(1, Now.AddSeconds(1));
        var next = fixture.Qualify(fixture.First.ValidUntil, 2);
        await Assert.That(() => ModerationCaseAttributionRenewalReceipt.Record(
            fixture.Owner, fixture.Head, next, fixture.Host, next.QualifiedAt)).Throws<InvalidOperationException>();
        await Assert.That(fixture.Owner.State).IsEqualTo(ModerationCaseState.Closed);
        await Assert.That(fixture.Head.Revision).IsEqualTo(1L);
    }

    [Test]
    [Arguments("NonUtc")]
    [Arguments("Default")]
    [Arguments("Infinite")]
    [Arguments("Stale")]
    [Arguments("Expired")]
    [Arguments("Withdrawn")]
    public async Task RecordingRequiresExplicitUtcFreshCurrentQualification(string change)
    {
        var fixture = new Fixture();
        var next = fixture.Qualify(fixture.First.ValidUntil, 2);
        var now = change switch
        {
            "NonUtc" => next.QualifiedAt.ToOffset(TimeSpan.FromHours(1)),
            "Default" => default,
            "Infinite" => DateTimeOffset.MaxValue,
            "Stale" => next.QualifiedAt.AddSeconds(1),
            "Expired" => next.ValidUntil,
            _ => next.QualifiedAt
        };
        if (change == "Withdrawn")
            next.Withdraw(Guid.CreateVersion7(), 1, Guid.CreateVersion7(), now);
        if (change is "NonUtc" or "Default" or "Infinite")
            await Assert.That(() => Record()).Throws<ArgumentException>();
        else
            await Assert.That(() => Record()).Throws<InvalidOperationException>();

        ModerationCaseAttributionRenewalReceipt Record() =>
            ModerationCaseAttributionRenewalReceipt.Record(fixture.Owner, fixture.Head, next, fixture.Host, now);
    }

    [Test]
    [Arguments("Id")]
    [Arguments("CaseId")]
    [Arguments("HeadId")]
    [Arguments("TenantId")]
    [Arguments("TrustDomainId")]
    [Arguments("ExpectedSourceId")]
    public async Task PersistedReceiptRejectsNonUuidV7Identity(string property)
    {
        var fixture = new Fixture();
        var next = fixture.Qualify(fixture.First.ValidUntil, 2);
        var receipt = ModerationCaseAttributionRenewalReceipt.Record(
            fixture.Owner, fixture.Head, next, fixture.Host, next.QualifiedAt);
        Set(receipt, property, Guid.NewGuid());
        await Assert.That(() => receipt.ValidatePersistedState()).Throws<ArgumentException>();
    }

    [Test]
    [Arguments("ExpectedCaseRevision", 0L)]
    [Arguments("ExpectedCaseRevision", -1L)]
    [Arguments("ExpectedGeneration", 0L)]
    [Arguments("ExpectedGeneration", -1L)]
    [Arguments("ExpectedHeadRevision", 0L)]
    [Arguments("ExpectedHeadRevision", -1L)]
    [Arguments("ExpectedHeadRevision", 2L)]
    public async Task PersistedReceiptRejectsInvalidExpectedTuple(string property, long value)
    {
        var fixture = new Fixture();
        var next = fixture.Qualify(fixture.First.ValidUntil, 2);
        var receipt = ModerationCaseAttributionRenewalReceipt.Record(
            fixture.Owner, fixture.Head, next, fixture.Host, next.QualifiedAt);
        Set(receipt, property, value);
        await Assert.That(() => receipt.ValidatePersistedState()).Throws<ArgumentException>();
    }

    [Test]
    [Arguments("ReusedSource")]
    [Arguments("NonUtc")]
    [Arguments("Default")]
    [Arguments("Infinite")]
    public async Task PersistedReceiptRejectsReusedSourceOrInvalidTime(string change)
    {
        var fixture = new Fixture();
        var next = fixture.Qualify(fixture.First.ValidUntil, 2);
        var receipt = ModerationCaseAttributionRenewalReceipt.Record(
            fixture.Owner, fixture.Head, next, fixture.Host, next.QualifiedAt);
        if (change == "ReusedSource")
            Set(receipt, nameof(receipt.Id), receipt.ExpectedSourceId);
        else
            Set(receipt, nameof(receipt.QualifiedAt), change switch
            {
                "Default" => default(DateTimeOffset),
                "Infinite" => DateTimeOffset.MaxValue,
                _ => next.QualifiedAt.ToOffset(TimeSpan.FromHours(1))
            });
        await Assert.That(() => receipt.ValidatePersistedState()).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(0L)]
    [Arguments(-1L)]
    public async Task RecordingRejectsCorruptPersistedSourceGeneration(long generation)
    {
        var fixture = new Fixture();
        var next = fixture.Qualify(fixture.First.ValidUntil, 2);
        Set(next, nameof(next.Generation), generation);
        await Assert.That(() => ModerationCaseAttributionRenewalReceipt.Record(
            fixture.Owner, fixture.Head, next, fixture.Host, next.QualifiedAt)).Throws<ArgumentException>();
    }

    private static void Set(object row, string property, object value) =>
        row.GetType().GetProperty(property)!.SetValue(row, value);

    private sealed class Fixture
    {
        public ModerationHostBinding Host { get; }
        public ModerationCase Root { get; }
        public ModerationCase Owner { get; }
        public ModerationCaseAttributionSource First { get; }
        public ModerationCaseAttributionHead Head { get; }
        private readonly ModerationCaseTargetSource _material;
        private readonly ModerationCaseTargetPolicy _policy;
        private readonly SubjectReference _subject;
        private readonly Guid _caseId = Guid.CreateVersion7();
        private readonly Guid _human = Guid.CreateVersion7();
        private readonly Guid _issuer = Guid.CreateVersion7();

        public Fixture(ModerationHostBinding? host = null, AuthorityScope? scope = null)
        {
            Host = host ?? ModerationHostBinding.Establish(Guid.CreateVersion7(), Guid.CreateVersion7(),
                Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
            scope ??= AuthorityScope.Tenant(AuthorityScope.Instance(AuthorityScope.Solution(
                AuthorityScope.TrustDomain(Host.TrustDomainId), Host.SolutionId), Host.InstanceId), Guid.CreateVersion7());
            _material = ModerationCaseTargetSource.Record(Guid.CreateVersion7(), Guid.CreateVersion7(),
                scope.Id, scope.TrustDomainId, Guid.CreateVersion7(), Guid.CreateVersion7(),
                ModerationCaseTargetKind.EventVersion, ModerationCaseTargetKind.EventVersion,
                Guid.CreateVersion7(), Guid.CreateVersion7(), new string('a', 64), Guid.CreateVersion7(),
                Guid.CreateVersion7(), Guid.CreateVersion7(), new string('b', 64), new string('c', 64), Now);
            Root = ModerationCase.OpenAllegation(Guid.CreateVersion7(), scope, _material.Evidence(scope), Now);
            _policy = ModerationCaseTargetPolicy.Adopt(Guid.CreateVersion7(), scope.Id, scope.TrustDomainId,
                _material.Kind, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5), _issuer);
            _subject = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), scope.Parent!);
            First = Qualify(Now, 1);
            Owner = ModerationCase.OpenAttributed(_caseId, _subject, Root, 1, First.Id, Now);
            Head = ModerationCaseAttributionHead.Open(Guid.CreateVersion7(), Owner, First, Host, Now);
        }

        public ModerationCaseAttributionSource Qualify(
            DateTimeOffset now, long generation, Guid? caseId = null, SubjectReference? subject = null,
            ModerationHostBinding? host = null, Guid? id = null)
        {
            host ??= Host;
            var epoch = ModerationQualificationEpoch.Initialize(Guid.CreateVersion7(), host, new Tenant
            {
                Id = Root.Scope.Id, FullName = "Renewal jurisdiction", Slug = "renewal-jurisdiction",
                TenantStatus = new TenantStatus { Id = 1, MasterCode = "Active", FullName = "Active" }
            });
            var grant = ModerationCaseReadGrant.Grant(Guid.CreateVersion7(), Root.Scope.Id, Root.Scope.TrustDomainId,
                Root.Id, Root.Revision, _material.Id, _human, _issuer, Guid.CreateVersion7(), false,
                now, now.AddMinutes(2));
            return ModerationCaseAttributionSource.Qualify(id ?? Guid.CreateVersion7(), caseId ?? _caseId,
                generation, Root, 1, _material, subject ?? _subject, ModerationResponsibilityBasis.EventSubmission,
                host, epoch, grant, _policy, Guid.CreateVersion7(), false,
                new string('a', 64), new string('d', 64), now, now.AddMinutes(1));
        }
    }
}
