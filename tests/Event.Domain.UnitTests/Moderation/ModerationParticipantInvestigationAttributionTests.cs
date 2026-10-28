using Explore.Domain.Enums;
using ISLAMU.Moderation.Domain;

namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationParticipantInvestigationAttributionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task ExactAssessmentOpensHumanUnderReviewWithoutChangingOccurrence()
    {
        var f = new Fixture();
        var source = f.Qualify(f.Assessment);
        var owner = ModerationCase.OpenAttributed(source.CaseId, f.Subject, f.Root, 1, source.Id, Now);
        await Assert.That(source.InvestigationSourceId).IsEqualTo((Guid?)f.Assessment.Id);
        await Assert.That(source.InvestigationSourceRevision).IsEqualTo((long?)1);
        await Assert.That(source.ValidUntil).IsEqualTo(f.Assessment.ValidUntil);
        await Assert.That(owner.Subject).IsEqualTo(f.Subject);
        await Assert.That(owner.State).IsEqualTo(ModerationCaseState.Open);
        await Assert.That(f.Root.Subject).IsNull();
        await Assert.That(f.Root.Revision).IsEqualTo(1L);
    }

    [Test]
    public async Task InvestigationRequiresAnAssessmentAndCannotBorrowEventSubmission()
    {
        var f = new Fixture();
        await Assert.That(() => f.Qualify(null)).Throws<ArgumentException>();
        await Assert.That(() => f.Qualify(null, ModerationResponsibilityBasis.EventSubmission)).Throws<ArgumentException>();
        await Assert.That(() => f.Qualify(f.Assessment, ModerationResponsibilityBasis.EventSubmission)).Throws<ArgumentException>();
    }

    [Test]
    [Arguments("HostBindingId")]
    [Arguments("TrustDomainId")]
    [Arguments("TenantId")]
    [Arguments("OccurrenceCaseId")]
    [Arguments("OccurrenceId")]
    [Arguments("EvidenceSourceId")]
    [Arguments("EvidenceSourceVersion")]
    [Arguments("MaterialFactId")]
    [Arguments("SubjectId")]
    public async Task AssessmentCannotBeTransplantedAcrossExactCoordinates(string property)
    {
        var f = new Fixture();
        Set(f.Assessment, property, Guid.CreateVersion7());
        await Assert.That(() => f.Qualify(f.Assessment)).Throws<ArgumentException>();
    }

    [Test]
    [Arguments("OccurrenceCaseRevision")]
    [Arguments("EvidenceCaseRevision")]
    public async Task AssessmentMustMatchExactRootAndEvidenceRevisions(string property)
    {
        var f = new Fixture();
        Set(f.Assessment, property, 2L);
        await Assert.That(() => f.Qualify(f.Assessment)).Throws<ArgumentException>();
    }

    [Test]
    [Arguments("withdrawn")]
    [Arguments("expired")]
    [Arguments("future")]
    [Arguments("deadline")]
    [Arguments("nonhuman")]
    public async Task AssessmentMustBeCurrentHumanAndBoundAttributionLifetime(string defect)
    {
        var f = new Fixture();
        switch (defect)
        {
            case "withdrawn": f.Assessment.Withdraw(Guid.CreateVersion7(), 1, Guid.CreateVersion7(), Now); break;
            case "expired": f.At = f.Assessment.ValidUntil; break;
            case "future": f.At = Now.AddTicks(-1); break;
            case "deadline": f.Until = f.Assessment.ValidUntil.AddTicks(1); break;
            case "nonhuman":
                f.Subject = SubjectReference.Create(ModerationSubjectKind.Organization, f.Subject.Id, f.Root.Scope.Parent!);
                break;
        }
        await Assert.That(() => f.Qualify(f.Assessment)).Throws<ArgumentException>();
    }

    [Test]
    [Arguments("missing-id")]
    [Arguments("missing-revision")]
    [Arguments("wrong-id")]
    [Arguments("wrong-revision")]
    [Arguments("other-basis")]
    [Arguments("unknown-basis")]
    public async Task RehydratedAttributionRequiresClosedInvestigationCoordinateShape(string defect)
    {
        var f = new Fixture();
        var row = f.Qualify(f.Assessment);
        switch (defect)
        {
            case "missing-id": Set(row, nameof(row.InvestigationSourceId), null); break;
            case "missing-revision": Set(row, nameof(row.InvestigationSourceRevision), null); break;
            case "wrong-id": Set(row, nameof(row.InvestigationSourceId), Guid.NewGuid()); break;
            case "wrong-revision": Set(row, nameof(row.InvestigationSourceRevision), 2L); break;
            case "other-basis": Set(row, nameof(row.Basis), ModerationResponsibilityBasis.EventSubmission); break;
            case "unknown-basis": Set(row, nameof(row.Basis), (ModerationResponsibilityBasis)6); break;
        }
        await Assert.That(() => row.ValidatePersistedState()).Throws<ArgumentException>();
    }

    [Test]
    public async Task AssessmentWithdrawalDoesNotRewriteHistoricalAttribution()
    {
        var f = new Fixture();
        var row = f.Qualify(f.Assessment);
        f.Assessment.Withdraw(Guid.CreateVersion7(), 1, Guid.CreateVersion7(), Now);
        row.ValidatePersistedState();
        await Assert.That(row.InvestigationSourceRevision).IsEqualTo((long?)1);
        await Assert.That(row.InvestigationSourceId).IsEqualTo((Guid?)f.Assessment.Id);
        await Assert.That(row.Revision).IsEqualTo(1L);
    }

    private static void Set(object row, string property, object? value) =>
        row.GetType().GetProperty(property)!.SetValue(row, value);

    private sealed class Fixture
    {
        public ModerationHostBinding Host { get; }
        public ModerationCase Root { get; }
        public ModerationCaseTargetSource Material { get; }
        public ModerationParticipantIncidentResponsibilitySource Assessment { get; }
        public SubjectReference Subject;
        public DateTimeOffset At = Now;
        public DateTimeOffset Until = Now.AddMinutes(2);
        private readonly ModerationQualificationEpoch epoch;
        private readonly ModerationCaseTargetPolicy policy;
        private readonly ModerationCaseReadGrant grant;

        public Fixture()
        {
            Host = ModerationHostBinding.Establish(Guid.CreateVersion7(), Guid.CreateVersion7(),
                Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
            var scope = AuthorityScope.Tenant(AuthorityScope.Instance(AuthorityScope.Solution(
                AuthorityScope.TrustDomain(Host.TrustDomainId), Host.SolutionId), Host.InstanceId), Guid.CreateVersion7());
            var tenant = new Tenant
            {
                Id = scope.Id,
                FullName = "Investigation tenant",
                Slug = "investigation",
                TenantStatus = new TenantStatus { Id = 1, FullName = "Active", MasterCode = "Active" }
            };
            epoch = ModerationQualificationEpoch.Initialize(Guid.CreateVersion7(), Host, tenant);
            var @event = new Explore.Domain.Event
            {
                Id = Guid.CreateVersion7(),
                Title = "Incident event",
                TenantId = scope.Id,
                Tenant = tenant,
                Actor = null!,
                VisibilityType = null!,
                EventStatus = null!,
                EventFormat = null!,
                ConcurrencyStamp = Guid.NewGuid()
            };
            var session = new EventSession
            {
                Id = Guid.CreateVersion7(),
                Event = @event,
                EventId = @event.Id,
                Tenant = tenant,
                TenantId = scope.Id,
                ConcurrencyStamp = Guid.NewGuid()
            };
            var order = RegistrationOrder.Create(scope.Id, @event.Id, Guid.CreateVersion7(), null,
                BookingPartyTypeEnum.Individual, Guid.CreateVersion7(),
                RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(), 4, 3, 2,
                    GuestRecoveryPolicyEnum.VerifiedEmailRequired), null, null, "USD", Now.UtcDateTime, null);
            order.ConcurrencyStamp = Guid.NewGuid();
            var participant = RegistrationParticipant.Create(scope.Id, order.Id, null, ParticipantTypeEnum.Adult, null);
            participant.ConcurrencyStamp = Guid.NewGuid();
            Material = ModerationCaseTargetSource.Record(Guid.CreateVersion7(), Guid.NewGuid(), scope.Id,
                Host.TrustDomainId, @event.Id, @event.ConcurrencyStamp, ModerationCaseTargetKind.ParticipantIncident,
                ModerationCaseTargetKind.ParticipantIncident, participant.Id, participant.ConcurrencyStamp,
                new string('a', 64), Guid.CreateVersion7(), Guid.NewGuid(), Guid.CreateVersion7(),
                new string('b', 64), new string('c', 64), Now);
            Root = ModerationCase.OpenAllegation(Guid.CreateVersion7(), scope, Material.Evidence(scope), Now);
            Subject = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), scope.Parent!);
            policy = ModerationCaseTargetPolicy.Adopt(Guid.CreateVersion7(), scope.Id, Host.TrustDomainId,
                Material.Kind, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10), Guid.CreateVersion7());
            grant = ModerationCaseReadGrant.Grant(Guid.CreateVersion7(), scope.Id, Host.TrustDomainId,
                Root.Id, Root.Revision, Material.Id, Guid.CreateVersion7(), Guid.CreateVersion7(),
                Guid.CreateVersion7(), false, Now, Now.AddMinutes(10));
            var originalPolicy = Custody(ModerationCaseNarrativePurpose.ConfidentialAllegation);
            var receipt = ModerationCaseTargetReceipt.Issue(Guid.CreateVersion7(), scope, Guid.CreateVersion7(),
                Host.Id, Guid.CreateVersion7(), 3, Guid.CreateVersion7(), ModerationCaseRelationshipKind.EventRole,
                ModerationCaseRelationshipGeneration.FromRevision(1), policy.Id, policy.Revision,
                Material.Evidence(scope), new byte[32], Now, Now.AddMinutes(10));
            var narrative = ModerationCaseNarrative.Capture(Guid.CreateVersion7(), Root.Id, receipt,
                originalPolicy, Adopt(originalPolicy), Now, Now.AddMinutes(10), new byte[64]);
            var custody = Custody(ModerationCaseNarrativePurpose.ParticipantInvestigation);
            Assessment = ModerationParticipantIncidentResponsibilitySource.Record(Guid.CreateVersion7(), Host,
                Root, 1, Material, narrative, session, order, participant, Subject, Guid.CreateVersion7(),
                policy, custody, Adopt(custody), grant, Now.AddMinutes(-20), Now.AddMinutes(-10),
                Now, Until, Now.AddMinutes(5), new byte[64], new string('a', 64));
        }

        private ModerationCaseNarrativePolicySource Custody(ModerationCaseNarrativePurpose purpose) =>
            ModerationCaseNarrativePolicySource.Record(Guid.CreateVersion7(), Host.Id, Host.TrustDomainId,
                Root.Scope.Id, Material.Kind, new(purpose, TimeSpan.FromMinutes(10), 4096),
                new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
                    "Independent controllers.", "Finite incident custody."),
                Guid.CreateVersion7(), 2, Now, Now.AddMinutes(30));

        private static ModerationCaseNarrativeAdoption Adopt(ModerationCaseNarrativePolicySource source) =>
            ModerationCaseNarrativeAdoption.Adopt(Guid.CreateVersion7(), source, source.PrivacyOwnerHumanId,
                3, Now, Now.AddMinutes(20));

        public ModerationCaseAttributionSource Qualify(ModerationParticipantIncidentResponsibilitySource? investigation,
            ModerationResponsibilityBasis basis = ModerationResponsibilityBasis.ParticipantInvestigation) =>
            ModerationCaseAttributionSource.Qualify(Guid.CreateVersion7(), Guid.CreateVersion7(), 1, Root, 1,
                Material, Subject, basis, Host, epoch, grant, policy, Guid.CreateVersion7(), false,
                new string('a', 64), new string('d', 64), At, Until, investigation);
    }
}
