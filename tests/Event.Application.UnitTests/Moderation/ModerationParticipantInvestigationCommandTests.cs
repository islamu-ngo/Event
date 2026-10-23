using Explore.Application;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Handlers.Commands;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Domain;
using ISLAMU.Moderation.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Moderation;

public sealed class ModerationParticipantInvestigationCommandTests
{
    [Test]
    [Arguments("operation")]
    [Arguments("scope")]
    [Arguments("qualification")]
    [Arguments("qualification-overflow")]
    [Arguments("source")]
    [Arguments("occurrence")]
    [Arguments("case-revision")]
    [Arguments("evidence-revision")]
    [Arguments("future-evidence")]
    [Arguments("read-grant")]
    [Arguments("evidence-source")]
    [Arguments("evidence-version")]
    [Arguments("human")]
    [Arguments("session")]
    [Arguments("session-generation")]
    [Arguments("window")]
    [Arguments("offset")]
    [Arguments("precision")]
    [Arguments("validity")]
    [Arguments("purge")]
    [Arguments("identification")]
    [Arguments("conduct")]
    [Arguments("provenance")]
    [Arguments("contradictions")]
    [Arguments("oversize")]
    public async Task InvalidCaptureCannotReachNativeRepositoryOrEchoGrounds(string defect)
    {
        var command = Valid();
        command = defect switch
        {
            "operation" => command with { OperationId = Guid.Empty },
            "scope" => command with { Target = command.Target.Parent! },
            "qualification" => command with { ExpectedQualificationRevision = 0 },
            "qualification-overflow" => command with { ExpectedQualificationRevision = long.MaxValue - 1 },
            "source" => command with { SourceId = Guid.NewGuid() },
            "occurrence" => command with { OccurrenceCaseId = Guid.Empty },
            "case-revision" => command with { ExpectedCaseRevision = long.MaxValue },
            "evidence-revision" => command with { EvidenceCaseRevision = 0 },
            "future-evidence" => command with { EvidenceCaseRevision = command.ExpectedCaseRevision + 1 },
            "read-grant" => command with { ReadGrantId = Guid.Empty },
            "evidence-source" => command with { EvidenceSourceId = Guid.Empty },
            "evidence-version" => command with { EvidenceSourceVersion = Guid.Empty },
            "human" => command with { SubjectHumanId = Guid.Empty },
            "session" => command with { SessionId = Guid.Empty },
            "session-generation" => command with { ExpectedSessionGeneration = Guid.Empty },
            "window" => command with { IncidentWindowUntil = command.IncidentWindowFrom },
            "offset" => command with { IncidentWindowFrom = command.IncidentWindowFrom.ToOffset(TimeSpan.FromHours(1)) },
            "precision" => command with { IncidentWindowFrom = command.IncidentWindowFrom.AddTicks(1) },
            "validity" => command with { ValidUntil = command.IncidentWindowUntil },
            "purge" => command with { PurgeAt = command.ValidUntil.AddTicks(-10) },
            "identification" => command with { IdentificationGrounds = " " },
            "conduct" => command with { AllegedConduct = null! },
            "provenance" => command with { EvidenceProvenance = "" },
            "contradictions" => command with { ContradictionsAndCredentialMisuse = "\t" },
            _ => command with { AllegedConduct = new string('x', 2049) }
        };
        // A missing repository makes accidental execution fail instead of manufacturing authority.
        var response = await new RecordModerationParticipantInvestigationCommandHandler(null!).ExecuteAsync(command);
        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.FailureCode).IsNull();
        await Assert.That(response.Errors).IsNotNull();
        await Assert.That(response.Revision).IsNull();
        await Assert.That(response.QualificationRevision).IsNull();
        await Assert.That(string.Join(" ", response.Errors ?? []).Contains(Valid().IdentificationGrounds)).IsFalse();
        await Assert.That(command.ToString().Contains(Valid().IdentificationGrounds)).IsFalse();
    }

    [Test]
    public async Task ConcreteCapturePreservesEveryNativeCoordinateAndProtectedGround()
    {
        var command = Valid();
        var mutation = (RecordModerationParticipantIncidentResponsibilitySource)command.ToMutation();
        await Assert.That(ModerationParticipantInvestigationQualification.IsValid(mutation)).IsTrue();
        await Assert.That(mutation.OperationId).IsEqualTo(command.OperationId);
        await Assert.That(mutation.Target).IsEqualTo(command.Target);
        await Assert.That(mutation.ExpectedQualificationRevision).IsEqualTo(command.ExpectedQualificationRevision);
        await Assert.That(mutation.SourceId).IsEqualTo(command.SourceId);
        await Assert.That(mutation.OccurrenceCaseId).IsEqualTo(command.OccurrenceCaseId);
        await Assert.That(mutation.ExpectedCaseRevision).IsEqualTo(command.ExpectedCaseRevision);
        await Assert.That(mutation.EvidenceCaseRevision).IsEqualTo(command.EvidenceCaseRevision);
        await Assert.That(mutation.ReadGrantId).IsEqualTo(command.ReadGrantId);
        await Assert.That(mutation.EvidenceSourceId).IsEqualTo(command.EvidenceSourceId);
        await Assert.That(mutation.EvidenceSourceVersion).IsEqualTo(command.EvidenceSourceVersion);
        await Assert.That(mutation.SubjectHumanId).IsEqualTo(command.SubjectHumanId);
        await Assert.That(mutation.SessionId).IsEqualTo(command.SessionId);
        await Assert.That(mutation.ExpectedSessionGeneration).IsEqualTo(command.ExpectedSessionGeneration);
        await Assert.That(mutation.IncidentWindowFrom).IsEqualTo(command.IncidentWindowFrom);
        await Assert.That(mutation.IncidentWindowUntil).IsEqualTo(command.IncidentWindowUntil);
        await Assert.That(mutation.ValidUntil).IsEqualTo(command.ValidUntil);
        await Assert.That(mutation.PurgeAt).IsEqualTo(command.PurgeAt);
        await Assert.That(mutation.Grounds).IsEqualTo(new ModerationParticipantInvestigationGrounds(
            command.IdentificationGrounds, command.AllegedConduct, command.EvidenceProvenance,
            command.ContradictionsAndCredentialMisuse));
        await Assert.That(mutation.ToString().Contains(command.AllegedConduct)).IsFalse();
        await Assert.That(mutation.Grounds.ToString().Contains(command.AllegedConduct)).IsFalse();
    }

    [Test]
    public async Task OpaqueVersionsAndMaximumGroundsRemainValidWithoutInventedCorroboration()
    {
        var command = Valid() with
        {
            IdentificationGrounds = new string('i', 2048), AllegedConduct = new string('c', 2048),
            EvidenceProvenance = new string('p', 2048), ContradictionsAndCredentialMisuse = new string('m', 2048),
            EvidenceCaseRevision = 1, PurgeAt = Valid().ValidUntil
        };
        await Assert.That((await new ModerationParticipantInvestigationCommandValidator().ValidateAsync(command)).IsValid)
            .IsTrue();
    }

    [Test]
    public async Task AutomaticDiscoveryRegistersTheClosedCommandWithItsOwningHandler()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations([typeof(RecordModerationParticipantInvestigationCommand),
            typeof(RecordModerationParticipantInvestigationCommandHandler)]);
        await Assert.That(services.Any(descriptor => descriptor.ServiceType ==
            typeof(Explore.Application.Contracts.Operations.ICommandHandler<
                RecordModerationParticipantInvestigationCommand, ModerationQualificationResponse>))).IsTrue();
        await Assert.That(services.Any(descriptor =>
            descriptor.ServiceType == typeof(RecordModerationParticipantInvestigationCommandHandler)
            && descriptor.Lifetime == ServiceLifetime.Scoped)).IsTrue();
    }

    [Test]
    public async Task RealHandlerPreservesNativeIntentCancellationAndReceiptOnlyResponse()
    {
        var command = Valid();
        using var cancellation = new CancellationTokenSource();
        var repository = new CaptureRepository(command);
        var response = await new RecordModerationParticipantInvestigationCommandHandler(repository)
            .ExecuteAsync(command, cancellation.Token);
        await Assert.That(repository.Captured).IsEqualTo(command.ToMutation());
        await Assert.That(repository.Token).IsEqualTo(cancellation.Token);
        await Assert.That(response.IsSuccess).IsTrue();
        await Assert.That(response.Id).IsEqualTo(command.SourceId);
        await Assert.That(response.Revision).IsEqualTo(1L);
        await Assert.That(response.QualificationRevision).IsEqualTo(5L);
        await Assert.That(response.Message).IsNull();
        await Assert.That(response.Errors).IsNull();
        await Assert.That(response.FailureCode).IsNull();
    }

    // A capture seam exercises transport mapping only; it does not simulate native assessor authority.
    private sealed class CaptureRepository(RecordModerationParticipantInvestigationCommand command)
        : IModerationQualificationRepository
    {
        public ModerationQualificationMutation? Captured { get; private set; }
        public CancellationToken Token { get; private set; }

        public Task<ModerationQualificationWriteResult> ExecuteAsync(
            ModerationQualificationMutation mutation, CancellationToken cancellationToken = default)
        {
            Captured = mutation;
            Token = cancellationToken;
            var receipt = ModerationQualificationMutationReceipt.Complete(command.OperationId, new string('a', 64),
                Guid.CreateVersion7(), command.Target.TrustDomainId, command.Target.Id,
                ModerationQualificationSourceKind.ParticipantIncidentResponsibilitySource,
                command.SourceId, Guid.CreateVersion7(), 1, 5, command.ValidUntil);
            return Task.FromResult(new ModerationQualificationWriteResult(ModerationQualificationWriteOutcome.Known, receipt));
        }
    }

    internal static RecordModerationParticipantInvestigationCommand Valid() => new()
    {
        OperationId = Guid.CreateVersion7(),
        Target = AuthorityScope.Tenant(AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()),
            Guid.CreateVersion7()), Guid.CreateVersion7()),
        ExpectedQualificationRevision = 4, SourceId = Guid.CreateVersion7(), OccurrenceCaseId = Guid.CreateVersion7(),
        ExpectedCaseRevision = 3, EvidenceCaseRevision = 2, ReadGrantId = Guid.CreateVersion7(),
        EvidenceSourceId = Guid.CreateVersion7(), EvidenceSourceVersion = Guid.NewGuid(),
        SubjectHumanId = Guid.CreateVersion7(), SessionId = Guid.CreateVersion7(), ExpectedSessionGeneration = Guid.NewGuid(),
        IncidentWindowFrom = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero),
        IncidentWindowUntil = new(2026, 10, 13, 10, 0, 0, TimeSpan.Zero),
        ValidUntil = new(2026, 10, 13, 11, 0, 0, TimeSpan.Zero),
        PurgeAt = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero),
        IdentificationGrounds = "Protected incident-specific identification assessment.",
        AllegedConduct = "Protected allegation assessed independently, not established guilt.",
        EvidenceProvenance = "Protected original evidence provenance assessment.",
        ContradictionsAndCredentialMisuse = "Protected contradictions and possible credential misuse assessment."
    };
}
