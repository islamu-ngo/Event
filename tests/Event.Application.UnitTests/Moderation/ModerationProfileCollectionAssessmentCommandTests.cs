using Explore.Application.Features.Moderation.Handlers.Commands;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Application.Services.Moderation;
using ISLAMU.Moderation.Domain;

namespace Event.Application.UnitTests.Moderation;

public sealed class ModerationProfileCollectionAssessmentCommandTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExplicitEmptyAndCompleteRequirementsAreValidNominationsNotAuthority(bool complete)
    {
        var command = Record() with
        {
            Required = complete
            ? [ModerationCollectionPrerequisiteKind.AccountEligibility, ModerationCollectionPrerequisiteKind.SubjectConsent,
                ModerationCollectionPrerequisiteKind.GuardianConsent] : []
        };
        await Assert.That((await new ModerationProfileCollectionAssessmentCommandValidator<RecordModerationProfileCollectionAssessmentCommand>()
            .ValidateAsync(command)).IsValid).IsTrue();
        await Assert.That(command.ToRecord().Required.SequenceEqual(command.Required)).IsTrue();
        await Assert.That(command.ToRecord().Original).IsEqualTo(command.Original.ToOriginal());
    }

    [Test]
    public async Task InvalidProposalCannotReachNativeRecording()
    {
        var command = Record();
        RecordModerationProfileCollectionAssessmentCommand[] invalid =
        [
            command with { Original = null! },
            command with { SourceId = Guid.Empty },
            command with { SourceId = command.Original.ObservationId },
            command with { ExpectedGeneration = -1 },
            command with { ExpectedGeneration = long.MaxValue - 1 },
            command with { Required = null! },
            command with { Required = [ModerationCollectionPrerequisiteKind.SubjectConsent,
                ModerationCollectionPrerequisiteKind.SubjectConsent] },
            command with { Required = [(ModerationCollectionPrerequisiteKind)999] },
            command with { Required = [ModerationCollectionPrerequisiteKind.AccountEligibility,
                ModerationCollectionPrerequisiteKind.SubjectConsent, ModerationCollectionPrerequisiteKind.GuardianConsent,
                ModerationCollectionPrerequisiteKind.SubjectConsent] },
            command with { ValidUntil = DateTimeOffset.MaxValue },
            command with { ValidUntil = command.ValidUntil.ToOffset(TimeSpan.FromHours(1)) }
        ];
        foreach (var nomination in invalid)
        {
            var response = await new RecordModerationProfileCollectionAssessmentCommandHandler(null!).ExecuteAsync(nomination);
            await Assert.That(response.IsSuccess).IsFalse();
            await Assert.That(response.Errors).IsNotNull();
        }
    }

    [Test]
    public async Task InvalidOriginalCoordinatesCannotReachEitherNativeWriter()
    {
        var original = Original();
        ModerationProfileCollectionAssessmentOriginalIntent[] invalid =
        [
            original with { ObservationId = Guid.Empty },
            original with { ExpectedObservationRevision = 2 },
            original with { ExpectedObservationGeneration = 0 },
            original with { Jurisdiction = AuthorityScope.TrustDomain(Id(30)) },
            original with { PolicyRevision = 0 },
            original with { Mode = (ModerationNativeMode)999 },
            original with { ExpectedProcedureSourceRevision = 2 },
            original with { ExpectedProcedureAdoptionRevision = 2 },
            original with { ExpectedProcedureAdoptionWriterGeneration = original.ExpectedProcedureSourceWriterGeneration }
        ];
        foreach (var intent in invalid)
        {
            var record = await new RecordModerationProfileCollectionAssessmentCommandHandler(null!)
                .ExecuteAsync(Record() with { Original = intent });
            var adopt = await new AdoptModerationProfileCollectionAssessmentCommandHandler(null!)
                .ExecuteAsync(Adopt() with { Original = intent });
            await Assert.That(record.IsSuccess).IsFalse();
            await Assert.That(adopt.IsSuccess).IsFalse();
            await Assert.That(record.Errors).IsNotNull();
            await Assert.That(adopt.Errors).IsNotNull();
        }
    }

    [Test]
    public async Task InvalidAcceptanceCannotReachNativeAdoption()
    {
        var command = Adopt();
        AdoptModerationProfileCollectionAssessmentCommand[] invalid =
        [
            command with { Original = null! },
            command with { AdoptionId = command.SourceId },
            command with { AdoptionId = command.Original.ObservationId },
            command with { ExpectedSourceRevision = 2 },
            command with { ExpectedSourceGeneration = 0 },
            command with { ExpectedGeneration = command.ExpectedSourceGeneration + 1 },
            command with { ValidUntil = DateTimeOffset.MinValue }
        ];
        foreach (var nomination in invalid)
        {
            var response = await new AdoptModerationProfileCollectionAssessmentCommandHandler(null!).ExecuteAsync(nomination);
            await Assert.That(response.IsSuccess).IsFalse();
            await Assert.That(response.Errors).IsNotNull();
        }
    }

    [Test]
    public async Task RequiredSetIsCopiedAtTheCommandBoundary()
    {
        var required = new[] { ModerationCollectionPrerequisiteKind.SubjectConsent };
        var command = Record() with { Required = required };
        required[0] = ModerationCollectionPrerequisiteKind.GuardianConsent;
        await Assert.That(command.Required.SequenceEqual([ModerationCollectionPrerequisiteKind.SubjectConsent])).IsTrue();
        await Assert.That(command.ToRecord().Required.SequenceEqual(command.Required)).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CancellationBeforeEntryCannotReachNativeWriting(bool adopt)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.That(async () =>
        {
            if (adopt)
                await new AdoptModerationProfileCollectionAssessmentCommandHandler(null!).ExecuteAsync(Adopt(), cancellation.Token);
            else
                await new RecordModerationProfileCollectionAssessmentCommandHandler(null!).ExecuteAsync(Record(), cancellation.Token);
        }).Throws<OperationCanceledException>();
    }

    private static RecordModerationProfileCollectionAssessmentCommand Record() => new()
    {
        Original = Original(),
        SourceId = Id(7),
        ExpectedGeneration = 0,
        Required = [],
        ValidUntil = new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero)
    };

    private static AdoptModerationProfileCollectionAssessmentCommand Adopt() => new()
    {
        Original = Original(),
        SourceId = Id(7),
        ExpectedSourceRevision = 1,
        ExpectedSourceGeneration = 1,
        ExpectedGeneration = 1,
        AdoptionId = Id(8),
        ValidUntil = new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero)
    };

    private static ModerationProfileCollectionAssessmentOriginalIntent Original() => new()
    {
        ObservationId = Id(1),
        ExpectedObservationRevision = 1,
        ExpectedObservationGeneration = 2,
        Jurisdiction = AuthorityScope.Tenant(AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(Id(30)), Id(31)), Id(32)), Id(33)),
        PolicyId = Id(2),
        PolicyRevision = 4,
        PublicationId = Id(3),
        ProfileId = Id(4),
        Mode = ModerationNativeMode.Embedded,
        ProcedureSourceId = Id(5),
        ExpectedProcedureSourceRevision = 1,
        ExpectedProcedureSourceWriterGeneration = 7,
        ProcedureAdoptionId = Id(6),
        ExpectedProcedureAdoptionRevision = 1,
        ExpectedProcedureAdoptionWriterGeneration = 8
    };

    private static Guid Id(int value) => Guid.Parse($"019a0000-0000-7000-8000-{value:x12}");
}
