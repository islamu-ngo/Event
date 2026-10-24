using Explore.Domain.Enums;
using ISLAMU.Moderation.Domain;
using System.Runtime.InteropServices;

namespace Event.Domain.UnitTests.Entities;

public sealed class ModerationParticipantIncidentResponsibilitySourceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task RecordsExactIncidentAndIndependentCustodyWithoutChangingOccurrence()
    {
        var f = new Fixture();
        var source = f.Record();
        source.ValidatePersistedState();
        await Assert.That(source.OccurrenceCaseId).IsEqualTo(f.Root.Id);
        await Assert.That(source.EvidenceSourceVersion).IsEqualTo(f.Material.Version);
        await Assert.That(source.OriginalNarrativeId).IsEqualTo(f.Narrative.Id);
        await Assert.That(source.ParticipantId).IsEqualTo(f.Participant.Id);
        await Assert.That(source.EventVersion).IsEqualTo(f.Session.Event.ConcurrencyStamp);
        await Assert.That(source.CustodyAdoptionId).IsEqualTo(f.Adoption.Id);
        await Assert.That(source.AssessorHumanId).IsEqualTo(f.ReadGrant.CallerUserId);
        await Assert.That(source.Revision).IsEqualTo(1L);
        await Assert.That(f.Root.Revision).IsEqualTo(1L);
        await Assert.That(f.Root.Subject is null).IsTrue();
    }

    [Test]
    public async Task ProtectedAssessmentSnapshotsInputAndReturnedMemory()
    {
        var f = new Fixture();
        var source = f.Record();
        f.Ciphertext[0] = 99;
        await Assert.That(MemoryMarshal.TryGetArray(source.ProtectedAssessment, out var returned)).IsTrue();
        returned.Array![returned.Offset] = 77;
        await Assert.That(source.ProtectedAssessment.Span[0]).IsEqualTo((byte)1);
        await Assert.That(source.ToString()).DoesNotContain(source.AssessmentDigest);
        await Assert.That(source.ToString()).DoesNotContain(source.SubjectId.ToString());
    }

    [Test]
    [Arguments("subject-assessor")]
    [Arguments("subject-issuer")]
    [Arguments("nonhuman")]
    public async Task HumanAndIndependentReviewCoordinatesAreRequired(string conflict)
    {
        var f = new Fixture();
        if (conflict == "nonhuman")
            f.Subject = SubjectReference.Create(ModerationSubjectKind.Organization, Guid.CreateVersion7(), f.Root.Scope.Parent!);
        else
        {
            var assessor = conflict == "subject-assessor" ? f.Subject.Id : f.ReadGrant.CallerUserId;
            var issuer = conflict == "subject-issuer" ? f.Subject.Id : f.ReadGrant.IssuerUserId;
            f.ReadGrant = f.Grant(assessor, issuer);
        }
        await Assert.That(() => f.Record()).Throws<ArgumentException>();
    }

    [Test]
    public async Task NativeReadIssuerMayBeTheAssessorWithoutBecomingTheAllegedSubject()
    {
        var f = new Fixture();
        var assessor = f.ReadGrant.CallerUserId;
        f.ReadGrant = f.Grant(assessor, assessor);
        var source = f.Record();
        await Assert.That(source.AssessorHumanId).IsEqualTo(source.ReadIssuerHumanId);
        await Assert.That(source.SubjectId).IsNotEqualTo(assessor);
        await Assert.That(source.Revision).IsEqualTo(1L);
    }

    [Test]
    [Arguments("session-event")]
    [Arguments("session-tenant")]
    [Arguments("event-version")]
    [Arguments("order-event")]
    [Arguments("participant-order")]
    [Arguments("participant-tenant")]
    [Arguments("participant-source-id")]
    [Arguments("participant-source-generation")]
    [Arguments("subject-origin")]
    public async Task ForeignAncestryCannotBecomeIncidentResponsibility(string mismatch)
    {
        var f = new Fixture();
        switch (mismatch)
        {
            case "session-event": f.Session.EventId = Guid.CreateVersion7(); break;
            case "session-tenant": f.Session.TenantId = Guid.CreateVersion7(); break;
            case "event-version": f.Session.Event.ConcurrencyStamp = Guid.NewGuid(); break;
            case "order-event": f.Order = f.NewOrder(Guid.CreateVersion7()); break;
            case "participant-order":
                f.Participant = RegistrationParticipant.Create(f.Root.Scope.Id, Guid.CreateVersion7(),
                    null, ParticipantTypeEnum.Adult, null);
                f.Participant.ConcurrencyStamp = Guid.NewGuid();
                break;
            case "participant-tenant":
                f.Participant = RegistrationParticipant.Create(Guid.CreateVersion7(), f.Order.Id,
                    null, ParticipantTypeEnum.Adult, null);
                f.Participant.ConcurrencyStamp = Guid.NewGuid();
                break;
            case "participant-source-id":
                f.Participant = RegistrationParticipant.Create(f.Root.Scope.Id, f.Order.Id,
                    null, ParticipantTypeEnum.Adult, null);
                f.Participant.ConcurrencyStamp = Guid.NewGuid();
                break;
            case "participant-source-generation": f.Participant.ConcurrencyStamp = Guid.NewGuid(); break;
            case "subject-origin":
                f.Subject = SubjectReference.Create(ModerationSubjectKind.Human, f.Subject.Id,
                    AuthorityScope.Instance(f.Root.Scope.Parent!.Parent!, Guid.CreateVersion7()));
                break;
        }
        await Assert.That(() => f.Record()).Throws<ArgumentException>();
    }

    [Test]
    [Arguments("wrong-purpose")]
    [Arguments("another-adoption")]
    [Arguments("withdrawn-adoption")]
    [Arguments("disabled-target-policy")]
    [Arguments("revoked-read")]
    [Arguments("foreign-read-case")]
    [Arguments("foreign-material")]
    [Arguments("foreign-narrative")]
    public async Task ExactOriginalMaterialAndSeparateCurrentAdoptionAreRequired(string mismatch)
    {
        var f = new Fixture();
        switch (mismatch)
        {
            case "wrong-purpose":
                f.Custody = f.CustodyPolicy(ModerationCaseNarrativePurpose.ConfidentialAllegation);
                f.Adoption = f.Adopt(f.Custody);
                break;
            case "another-adoption": f.Adoption = f.Adopt(f.CustodyPolicy()); break;
            case "withdrawn-adoption": f.Adoption.Withdraw(1, Guid.CreateVersion7(), Now); break;
            case "disabled-target-policy":
                f.Policy.Change(1, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10), false, Guid.CreateVersion7());
                break;
            case "revoked-read": f.ReadGrant.Revoke(1, Now); break;
            case "foreign-read-case":
                f.ReadGrant = ModerationCaseReadGrant.Grant(Guid.CreateVersion7(), f.Root.Scope.Id,
                    f.Root.Scope.TrustDomainId, Guid.CreateVersion7(), 1, f.Material.Id,
                    f.ReadGrant.CallerUserId, f.ReadGrant.IssuerUserId, Guid.CreateVersion7(), false, Now, Now.AddMinutes(10));
                break;
            case "foreign-material": f.Material = f.NewMaterial(); break;
            case "foreign-narrative": f.Narrative = new Fixture().Narrative; break;
        }
        await Assert.That(() => f.Record()).Throws<ArgumentException>();
    }

    [Test]
    [Arguments("adoption")]
    [Arguments("retention")]
    [Arguments("read")]
    [Arguments("original")]
    [Arguments("validity")]
    [Arguments("utc")]
    [Arguments("infinite")]
    public async Task OriginalPhysicalDeadlineCannotOutliveAnyCustodyBound(string bound)
    {
        var f = new Fixture();
        switch (bound)
        {
            case "adoption": f.Adoption = f.Adopt(f.Custody, Now.AddMinutes(3)); break;
            case "retention":
                f.Custody = f.CustodyPolicy(retention: TimeSpan.FromMinutes(3));
                f.Adoption = f.Adopt(f.Custody);
                break;
            case "read": f.ReadGrant = f.Grant(expiresAt: Now.AddMinutes(3)); break;
            case "original": f.Narrative = f.Capture(Now.AddMinutes(3)); break;
            case "validity": f.ValidUntil = f.PurgeAt.AddTicks(1); break;
            case "utc": f.PurgeAt = f.PurgeAt.ToOffset(TimeSpan.FromHours(1)); break;
            case "infinite": f.PurgeAt = DateTimeOffset.MaxValue; break;
        }
        await Assert.That(() => f.Record()).Throws<ArgumentException>();
    }

    [Test]
    [Arguments("session")]
    [Arguments("order")]
    [Arguments("participant")]
    [Arguments("source-id")]
    [Arguments("assessment-hash")]
    [Arguments("ciphertext")]
    [Arguments("incident-window")]
    public async Task StorageRejectsMissingGenerationsMalformedPayloadAndInvalidWindow(string field)
    {
        var f = new Fixture();
        switch (field)
        {
            case "session": f.Session.ConcurrencyStamp = Guid.Empty; break;
            case "order": f.Order.ConcurrencyStamp = Guid.Empty; break;
            case "participant": f.Participant.ConcurrencyStamp = Guid.Empty; break;
            case "source-id": f.Id = Guid.NewGuid(); break;
            case "assessment-hash": f.Digest = new string('A', 64); break;
            case "ciphertext": f.Ciphertext = []; break;
            case "incident-window": f.WindowUntil = f.WindowFrom; break;
        }
        await Assert.That(() => f.Record()).Throws<ArgumentException>();
    }

    [Test]
    public async Task FiniteValidityUsesExactExclusiveCutoff()
    {
        var f = new Fixture();
        var row = f.Record();
        await Assert.That(row.IsCurrentAt(Now.AddTicks(-1))).IsFalse();
        await Assert.That(row.IsCurrentAt(Now)).IsTrue();
        await Assert.That(row.IsCurrentAt(f.ValidUntil)).IsFalse();
    }

    [Test]
    public async Task WithdrawalIsOneWayAndPreservesOriginalDeadlineAndEvidence()
    {
        var f = new Fixture();
        var row = f.Record();
        var operation = Guid.CreateVersion7();
        var human = Guid.CreateVersion7();
        row.Withdraw(operation, 1, human, Now.AddMinutes(1));
        row.ValidatePersistedState();
        await Assert.That(row.Revision).IsEqualTo(2L);
        await Assert.That(row.WithdrawalOperationId).IsEqualTo((Guid?)operation);
        await Assert.That(row.WithdrawnByHumanId).IsEqualTo((Guid?)human);
        await Assert.That(row.IsCurrentAt(Now)).IsFalse();
        await Assert.That(row.PurgeAt).IsEqualTo(f.PurgeAt);
        await Assert.That(row.EvidenceSourceId).IsEqualTo(f.Material.Id);
        await Assert.That(() => row.Withdraw(Guid.CreateVersion7(), 2, human, Now.AddMinutes(2)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments("stale")]
    [Arguments("operation")]
    [Arguments("human")]
    [Arguments("time")]
    [Arguments("utc")]
    public async Task FailedWithdrawalDoesNotPartiallyMutateSource(string invalid)
    {
        var f = new Fixture();
        var row = f.Record();
        var operation = invalid == "operation" ? Guid.NewGuid() : Guid.CreateVersion7();
        var human = invalid == "human" ? Guid.Empty : Guid.CreateVersion7();
        var time = invalid == "time" ? Now.AddTicks(-1)
            : invalid == "utc" ? Now.ToOffset(TimeSpan.FromHours(1)) : Now;
        if (invalid == "stale")
            await Assert.That(() => row.Withdraw(operation, 2, human, time)).Throws<InvalidOperationException>();
        else
            await Assert.That(() => row.Withdraw(operation, 1, human, time)).Throws<ArgumentException>();
        row.ValidatePersistedState();
        await Assert.That(row.Revision).IsEqualTo(1L);
        await Assert.That(row.WithdrawnAt is null).IsTrue();
    }

    [Test]
    [Arguments("withdrawal-human")]
    [Arguments("withdrawal-operation")]
    [Arguments("revision")]
    public async Task PersistedWithdrawalMetadataMustBeExclusive(string corruption)
    {
        var row = new Fixture().Record();
        var property = corruption switch
        {
            "withdrawal-human" => nameof(row.WithdrawnByHumanId),
            "withdrawal-operation" => nameof(row.WithdrawalOperationId),
            _ => nameof(row.Revision)
        };
        object value = corruption == "revision" ? 2L : Guid.CreateVersion7();
        typeof(ModerationParticipantIncidentResponsibilitySource).GetProperty(property)!.SetValue(row, value);
        await Assert.That(() => row.ValidatePersistedState()).Throws<ArgumentException>();
    }

    [Test]
    [Arguments("purpose")]
    [Arguments("target")]
    [Arguments("purge")]
    [Arguments("generation")]
    public async Task RehydratedOriginCannotLosePurposeOrOriginalDeadlineBounds(string corruption)
    {
        var f = new Fixture();
        var row = f.Record();
        var property = corruption switch
        {
            "purpose" => nameof(row.Purpose),
            "target" => nameof(row.TargetKind),
            "purge" => nameof(row.PurgeAt),
            _ => nameof(row.CustodyAdoptionWriterGeneration)
        };
        object value = corruption switch
        {
            "purpose" => ModerationCaseNarrativePurpose.ConfidentialAllegation,
            "target" => ModerationCaseTargetKind.EventVersion,
            "purge" => f.Narrative.PurgeAt.AddTicks(1),
            _ => row.CustodySourceWriterGeneration
        };
        typeof(ModerationParticipantIncidentResponsibilitySource).GetProperty(property)!.SetValue(row, value);
        await Assert.That(() => row.ValidatePersistedState()).Throws<ArgumentException>();
    }

    private sealed class Fixture
    {
        public Guid Id = Guid.CreateVersion7();
        public ModerationHostBinding Host { get; }
        public ModerationCase Root { get; }
        public ModerationCaseTargetSource Material;
        public ModerationCaseNarrative Narrative;
        public ModerationCaseNarrativePolicySource Custody;
        public ModerationCaseNarrativeAdoption Adoption;
        public ModerationCaseTargetPolicy Policy { get; }
        public ModerationCaseReadGrant ReadGrant;
        public EventSession Session { get; }
        public RegistrationOrder Order;
        public RegistrationParticipant Participant;
        public SubjectReference Subject;
        public byte[] Ciphertext = Enumerable.Repeat((byte)1, 64).ToArray();
        public string Digest = new('a', 64);
        public DateTimeOffset WindowFrom = Now.AddMinutes(-20);
        public DateTimeOffset WindowUntil = Now.AddMinutes(-10);
        public DateTimeOffset ValidUntil = Now.AddMinutes(2);
        public DateTimeOffset PurgeAt = Now.AddMinutes(5);
        private readonly Guid assessorGrantId = Guid.CreateVersion7();

        public Fixture()
        {
            Host = ModerationHostBinding.Establish(Guid.CreateVersion7(), Guid.CreateVersion7(),
                Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
            var scope = AuthorityScope.Tenant(AuthorityScope.Instance(AuthorityScope.Solution(
                AuthorityScope.TrustDomain(Host.TrustDomainId), Host.SolutionId), Host.InstanceId), Guid.CreateVersion7());
            var tenant = new Tenant
            {
                Id = scope.Id,
                FullName = "Incident tenant",
                Slug = "incident",
                TenantStatus = new TenantStatus { Id = 1, FullName = "Active", MasterCode = "Active" }
            };
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
            Session = new EventSession
            {
                Id = Guid.CreateVersion7(),
                Event = @event,
                EventId = @event.Id,
                Tenant = tenant,
                TenantId = scope.Id,
                ConcurrencyStamp = Guid.NewGuid()
            };
            Order = NewOrder(@event.Id);
            Participant = RegistrationParticipant.Create(scope.Id, Order.Id, null, ParticipantTypeEnum.Adult, null);
            Participant.ConcurrencyStamp = Guid.NewGuid();
            Material = NewMaterial();
            Root = ModerationCase.OpenAllegation(Guid.CreateVersion7(), scope, Material.Evidence(scope), Now);
            Policy = ModerationCaseTargetPolicy.Adopt(Guid.CreateVersion7(), scope.Id, scope.TrustDomainId,
                ModerationCaseTargetKind.ParticipantIncident, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10),
                Guid.CreateVersion7());
            Custody = CustodyPolicy();
            Adoption = Adopt(Custody);
            Subject = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), scope.Parent!);
            ReadGrant = Grant();
            Narrative = Capture(Now.AddMinutes(10));
        }

        public ModerationCaseTargetSource NewMaterial() => ModerationCaseTargetSource.Record(
            Guid.CreateVersion7(), Guid.NewGuid(), Session.TenantId, Host.TrustDomainId,
            Session.Event.Id, Session.Event.ConcurrencyStamp, ModerationCaseTargetKind.ParticipantIncident,
            ModerationCaseTargetKind.ParticipantIncident, Participant.Id, Participant.ConcurrencyStamp,
            new string('a', 64), Guid.CreateVersion7(), Guid.NewGuid(), Guid.CreateVersion7(),
            new string('b', 64), new string('c', 64), Now);

        public ModerationCaseNarrativePolicySource CustodyPolicy(
            ModerationCaseNarrativePurpose purpose = ModerationCaseNarrativePurpose.ParticipantInvestigation,
            TimeSpan? retention = null) => ModerationCaseNarrativePolicySource.Record(Guid.CreateVersion7(),
            Host.Id, Host.TrustDomainId, Session.TenantId, ModerationCaseTargetKind.ParticipantIncident,
            new(purpose, retention ?? TimeSpan.FromMinutes(10), 4096),
            new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
                "Independent canonical controllers.", "Incident-specific finite investigation custody."),
            Guid.CreateVersion7(), 2, Now, Now.AddMinutes(30));

        public ModerationCaseNarrativeAdoption Adopt(ModerationCaseNarrativePolicySource policy,
            DateTimeOffset? until = null) => ModerationCaseNarrativeAdoption.Adopt(Guid.CreateVersion7(),
            policy, policy.PrivacyOwnerHumanId, 3, Now, until ?? Now.AddMinutes(20));

        public ModerationCaseReadGrant Grant(Guid? assessor = null, Guid? issuer = null,
            DateTimeOffset? expiresAt = null) => ModerationCaseReadGrant.Grant(Guid.CreateVersion7(),
            Root.Scope.Id, Host.TrustDomainId, Root.Id, Root.Revision, Material.Id,
            assessor ?? Guid.CreateVersion7(), issuer ?? Guid.CreateVersion7(), Guid.CreateVersion7(), false,
            Now, expiresAt ?? Now.AddMinutes(10));

        public RegistrationOrder NewOrder(Guid eventId)
        {
            var order = RegistrationOrder.Create(Session.TenantId, eventId, Guid.CreateVersion7(), null,
                BookingPartyTypeEnum.Individual, Guid.CreateVersion7(),
                RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(), 4, 3, 2,
                    GuestRecoveryPolicyEnum.VerifiedEmailRequired), null, null, "USD", Now.UtcDateTime, null);
            order.ConcurrencyStamp = Guid.NewGuid();
            return order;
        }

        public ModerationCaseNarrative Capture(DateTimeOffset deadline)
        {
            var policy = CustodyPolicy(ModerationCaseNarrativePurpose.ConfidentialAllegation);
            var adoption = Adopt(policy);
            var receipt = ModerationCaseTargetReceipt.Issue(Guid.CreateVersion7(), Root.Scope,
                Guid.CreateVersion7(), Host.Id, Guid.CreateVersion7(), 3, Guid.CreateVersion7(),
                ModerationCaseRelationshipKind.EventRole, ModerationCaseRelationshipGeneration.FromRevision(1),
                Policy.Id, Policy.Revision, Material.Evidence(Root.Scope), new byte[32], Now, Now.AddMinutes(10));
            return ModerationCaseNarrative.Capture(Guid.CreateVersion7(), Root.Id, receipt,
                policy, adoption, Now, deadline, Ciphertext);
        }

        public ModerationParticipantIncidentResponsibilitySource Record() =>
            ModerationParticipantIncidentResponsibilitySource.Record(Id, Host, Root, 1, Material, Narrative,
                Session, Order, Participant, Subject, assessorGrantId, Policy, Custody, Adoption, ReadGrant,
                WindowFrom, WindowUntil, Now, ValidUntil, PurgeAt, Ciphertext, Digest);
    }
}
