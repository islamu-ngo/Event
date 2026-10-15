using ISLAMU.Moderation.Domain;

namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationCaseAttributionSourceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments(ModerationResponsibilityBasis.EventSubmission, ModerationSubjectKind.Human)]
    [Arguments(ModerationResponsibilityBasis.EventContent, ModerationSubjectKind.Content)]
    [Arguments(ModerationResponsibilityBasis.OrganizationalActor, ModerationSubjectKind.Organization)]
    [Arguments(ModerationResponsibilityBasis.ServiceIdentity, ModerationSubjectKind.Service)]
    public async Task QualificationRetainsSeparateSubjectAndImmutableMaterial(
        ModerationResponsibilityBasis basis, ModerationSubjectKind kind)
    {
        var f = new Fixture();
        var subject = SubjectReference.Create(kind, Guid.CreateVersion7(),
            kind == ModerationSubjectKind.Content ? f.Root.Scope : f.Root.Scope.Parent!);
        var source = f.Qualify(subject, basis);

        source.ValidatePersistedState();
        await Assert.That(source.SubjectId).IsEqualTo(subject.Id);
        await Assert.That(source.SubjectKind).IsEqualTo(kind);
        await Assert.That(source.Basis).IsEqualTo(basis);
        await Assert.That(source.OccurrenceCaseId).IsEqualTo(f.Root.Id);
        await Assert.That(source.MaterialFactId).IsEqualTo(f.Material.MaterialFactId);
        await Assert.That(source.EvidenceSourceVersion).IsEqualTo(f.Material.Version);
        await Assert.That(source.QualificationEpochId).IsEqualTo(f.Epoch.Id);
        await Assert.That(source.IsCurrentAt(Now)).IsTrue();
        await Assert.That(source.IsCurrentAt(source.ValidUntil)).IsFalse();
        await Assert.That(f.Root.Subject).IsNull();
        await Assert.That(f.Root.Revision).IsEqualTo(1L);
    }

    [Test]
    [Arguments(nameof(ModerationHostBinding.SolutionId))]
    [Arguments(nameof(ModerationHostBinding.InstanceId))]
    public async Task GenericQualificationRejectsForeignHostAncestry(string coordinate)
    {
        var f = new Fixture();
        var original = f.Host.GetType().GetProperty(coordinate)!.GetValue(f.Host);
        Set(f.Host, coordinate, Guid.CreateVersion7());
        await Assert.That(() => f.Qualify()).Throws<ArgumentException>();

        Set(f.Host, coordinate, original);
        await Assert.That(f.Qualify().IsCurrentAt(Now)).IsTrue();
    }

    [Test]
    [Arguments(nameof(ModerationQualificationEpoch.HostBindingId))]
    [Arguments(nameof(ModerationQualificationEpoch.TenantId))]
    public async Task QualificationRejectsForeignEpochOwner(string coordinate)
    {
        var f = new Fixture();
        Set(f.Epoch, coordinate, Guid.CreateVersion7());
        await Assert.That(() => f.Qualify()).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(nameof(ModerationCaseReadGrant.CaseId))]
    [Arguments(nameof(ModerationCaseReadGrant.SourceId))]
    [Arguments(nameof(ModerationCaseReadGrant.TenantId))]
    [Arguments(nameof(ModerationCaseReadGrant.TrustDomainId))]
    public async Task QualificationCannotBorrowAnotherReadPurpose(string coordinate)
    {
        var f = new Fixture();
        Set(f.Grant, coordinate, Guid.CreateVersion7());
        await Assert.That(() => f.Qualify()).Throws<ArgumentException>();
    }

    [Test]
    [Arguments("caller")]
    [Arguments("issuer")]
    [Arguments("expired")]
    [Arguments("longer-than-read")]
    [Arguments("longer-than-policy")]
    public async Task QualificationRequiresIndependentHumanAndFiniteOriginalLifetime(string defect)
    {
        var f = new Fixture();
        switch (defect)
        {
            case "caller": f.Subject = SubjectReference.Create(
                ModerationSubjectKind.Human, f.Grant.CallerUserId, f.Root.Scope.Parent!); break;
            case "issuer": f.Subject = SubjectReference.Create(
                ModerationSubjectKind.Human, f.Grant.IssuerUserId, f.Root.Scope.Parent!); break;
            case "expired": f.At = f.Grant.ExpiresAt; break;
            case "longer-than-read": f.Until = f.Grant.ExpiresAt.AddTicks(1); break;
            case "longer-than-policy":
                f.At = Now.AddMinutes(-1);
                Set(f.Material, nameof(f.Material.RecordedAt), f.At);
                break;
        }
        await Assert.That(() => f.Qualify()).Throws<ArgumentException>();
    }

    [Test]
    public async Task WithdrawalPreservesOpeningFactsAndCannotBeRepeated()
    {
        var f = new Fixture();
        var source = f.Qualify();
        var operation = Guid.CreateVersion7();
        var human = Guid.CreateVersion7();
        var until = source.ValidUntil;
        var digest = source.NativeResponsibilityDigest;

        source.Withdraw(operation, 1, human, Now.AddMinutes(1));
        source.ValidatePersistedState();
        await Assert.That(source.Revision).IsEqualTo(2L);
        await Assert.That(source.WithdrawalOperationId).IsEqualTo((Guid?)operation);
        await Assert.That(source.WithdrawnByHumanId).IsEqualTo((Guid?)human);
        await Assert.That(source.ValidUntil).IsEqualTo(until);
        await Assert.That(source.NativeResponsibilityDigest).IsEqualTo(digest);
        await Assert.That(source.MaterialFactId).IsEqualTo(f.Material.MaterialFactId);
        await Assert.That(source.IsCurrentAt(Now.AddMinutes(1))).IsFalse();
        await Assert.That(() => source.Withdraw(Guid.CreateVersion7(), 2, human, Now.AddMinutes(2)))
            .Throws<InvalidOperationException>();
        await Assert.That(source.WithdrawalOperationId).IsEqualTo((Guid?)operation);
    }

    [Test]
    [Arguments("operation")]
    [Arguments("human")]
    [Arguments("infinite")]
    [Arguments("offset")]
    [Arguments("past")]
    public async Task MalformedWithdrawalFailsBeforeMutation(string defect)
    {
        var source = new Fixture().Qualify();
        var operation = defect == "operation" ? Guid.NewGuid() : Guid.CreateVersion7();
        var human = defect == "human" ? Guid.NewGuid() : Guid.CreateVersion7();
        var at = defect switch
        {
            "infinite" => DateTimeOffset.MaxValue,
            "offset" => Now.ToOffset(TimeSpan.FromHours(1)),
            "past" => Now.AddTicks(-1),
            _ => Now
        };

        await Assert.That(() => source.Withdraw(operation, 1, human, at)).Throws<ArgumentException>();
        source.ValidatePersistedState();
        await Assert.That(source.Revision).IsEqualTo(1L);
        await Assert.That(source.WithdrawnAt).IsNull();
        await Assert.That(source.WithdrawalOperationId).IsNull();
        await Assert.That(source.WithdrawnByHumanId).IsNull();
    }

    [Test]
    [Arguments("operation")]
    [Arguments("human")]
    [Arguments("infinite")]
    [Arguments("offset")]
    [Arguments("partial")]
    public async Task HydratedWithdrawalMustRetainClosedFiniteUuidShape(string defect)
    {
        var source = new Fixture().Qualify();
        source.Withdraw(Guid.CreateVersion7(), 1, Guid.CreateVersion7(), Now);
        switch (defect)
        {
            case "operation": Set(source, nameof(source.WithdrawalOperationId), Guid.NewGuid()); break;
            case "human": Set(source, nameof(source.WithdrawnByHumanId), Guid.NewGuid()); break;
            case "infinite": Set(source, nameof(source.WithdrawnAt), DateTimeOffset.MaxValue); break;
            case "offset": Set(source, nameof(source.WithdrawnAt), Now.ToOffset(TimeSpan.FromHours(1))); break;
            case "partial": Set(source, nameof(source.WithdrawalOperationId), null); break;
        }
        await Assert.That(() => source.ValidatePersistedState()).Throws<ArgumentException>();
    }

    private static void Set(object value, string property, object? replacement) =>
        value.GetType().GetProperty(property)!.SetValue(value, replacement);

    private sealed class Fixture
    {
        public ModerationHostBinding Host { get; }
        public ModerationQualificationEpoch Epoch { get; }
        public ModerationCase Root { get; }
        public ModerationCaseTargetSource Material { get; }
        public ModerationCaseReadGrant Grant { get; }
        public SubjectReference Subject;
        public DateTimeOffset At = Now;
        public DateTimeOffset Until = Now.AddMinutes(10);
        private readonly ModerationCaseTargetPolicy policy;

        public Fixture()
        {
            Host = ModerationHostBinding.Establish(Guid.CreateVersion7(), Guid.CreateVersion7(),
                Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
            var scope = AuthorityScope.Tenant(AuthorityScope.Instance(AuthorityScope.Solution(
                AuthorityScope.TrustDomain(Host.TrustDomainId), Host.SolutionId), Host.InstanceId), Guid.CreateVersion7());
            Epoch = ModerationQualificationEpoch.Initialize(Guid.CreateVersion7(), Host,
                new Tenant
                {
                    Id = scope.Id, FullName = "Attribution tenant", Slug = "attribution",
                    TenantStatus = new TenantStatus { Id = 1, FullName = "Active", MasterCode = "Active" }
                });
            Material = ModerationCaseTargetSource.Record(Guid.CreateVersion7(), Guid.NewGuid(), scope.Id,
                Host.TrustDomainId, Guid.CreateVersion7(), Guid.NewGuid(), ModerationCaseTargetKind.EventVersion,
                ModerationCaseTargetKind.EventVersion, Guid.CreateVersion7(), Guid.NewGuid(),
                new string('a', 64), Guid.CreateVersion7(), Guid.NewGuid(), Guid.CreateVersion7(),
                new string('b', 64), new string('c', 64), Now);
            Root = ModerationCase.OpenAllegation(Guid.CreateVersion7(), scope, Material.Evidence(scope), Now);
            Subject = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), scope.Parent!);
            policy = ModerationCaseTargetPolicy.Adopt(Guid.CreateVersion7(), scope.Id, Host.TrustDomainId,
                Material.Kind, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10), Guid.CreateVersion7());
            Grant = ModerationCaseReadGrant.Grant(Guid.CreateVersion7(), scope.Id, Host.TrustDomainId,
                Root.Id, Root.Revision, Material.Id, Guid.CreateVersion7(), Guid.CreateVersion7(),
                Guid.CreateVersion7(), false, Now.AddMinutes(-1), Now.AddMinutes(10));
        }

        public ModerationCaseAttributionSource Qualify(SubjectReference? subject = null,
            ModerationResponsibilityBasis basis = ModerationResponsibilityBasis.EventSubmission) =>
            ModerationCaseAttributionSource.Qualify(Guid.CreateVersion7(), Guid.CreateVersion7(), 1, Root, 1,
                Material, subject ?? Subject, basis, Host, Epoch, Grant, policy, Guid.CreateVersion7(), false,
                new string('a', 64), new string('d', 64), At, Until);
    }
}
