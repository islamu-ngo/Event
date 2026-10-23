using Explore.Application.Features.Moderation.Handlers.Commands;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Application.Services.Moderation;
using ISLAMU.Moderation.Domain;

namespace Event.Application.UnitTests.Moderation;

public sealed class ModerationProfileSubjectConsentCommandTests
{
    [Test]
    public async Task OriginalAssessmentAndConsentSlotRemainDistinctNominations()
    {
        var grant = Grant();
        await Assert.That((await new ModerationProfileSubjectConsentCommandValidator<GrantModerationProfileSubjectConsentCommand>()
            .ValidateAsync(grant)).IsValid).IsTrue();
        var request = grant.ToGrant();
        await Assert.That(request.Assessment.Original).IsEqualTo(grant.Assessment.Original.ToOriginal());
        await Assert.That(request.Assessment.ExpectedSourceGeneration).IsEqualTo(9L);
        await Assert.That(request.Assessment.ExpectedAdoptionGeneration).IsEqualTo(10L);
        await Assert.That(request.ExpectedGeneration).IsEqualTo(3L);
        await Assert.That(request.ConsentId).IsEqualTo(grant.ConsentId);
        await Assert.That(request.ValidUntil).IsEqualTo(grant.ValidUntil);
    }

    [Test]
    public async Task MalformedGrantCannotEnterNativeIdentityTransaction()
    {
        var grant = Grant();
        var assessment = grant.Assessment;
        GrantModerationProfileSubjectConsentCommand[] invalid =
        [
            grant with { Assessment = null! },
            grant with { Assessment = assessment with { Original = null! } },
            grant with { Assessment = assessment with { SourceId = Guid.Empty } },
            grant with { Assessment = assessment with { AdoptionId = assessment.SourceId } },
            grant with { Assessment = assessment with { SourceId = assessment.Original.ObservationId } },
            grant with { Assessment = assessment with { AdoptionId = assessment.Original.ProcedureAdoptionId } },
            grant with { Assessment = assessment with { ExpectedSourceRevision = 2 } },
            grant with { Assessment = assessment with { ExpectedAdoptionRevision = 2 } },
            grant with { Assessment = assessment with { ExpectedSourceGeneration = 0 } },
            grant with { Assessment = assessment with { ExpectedSourceGeneration = long.MaxValue - 1 } },
            grant with { Assessment = assessment with { ExpectedAdoptionGeneration = assessment.ExpectedSourceGeneration } },
            grant with { Assessment = assessment with { ExpectedAdoptionGeneration = long.MaxValue } },
            grant with { ConsentId = Guid.Empty },
            grant with { ConsentId = assessment.SourceId },
            grant with { ConsentId = assessment.AdoptionId },
            grant with { ConsentId = assessment.Original.ObservationId },
            grant with { ExpectedGeneration = -1 },
            grant with { ExpectedGeneration = long.MaxValue - 1 },
            grant with { ValidUntil = DateTimeOffset.MaxValue },
            grant with { ValidUntil = grant.ValidUntil.ToOffset(TimeSpan.FromHours(1)) }
        ];
        foreach (var command in invalid)
        {
            var response = await new GrantModerationProfileSubjectConsentCommandHandler(null!).ExecuteAsync(command);
            await Assert.That(response.IsSuccess).IsFalse();
            await Assert.That(response.Errors).IsNotNull();
        }
    }

    [Test]
    public async Task InvalidOriginalCoordinatesCannotBecomeConsentAuthority()
    {
        var grant = Grant();
        var original = grant.Assessment.Original;
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
            var response = await new GrantModerationProfileSubjectConsentCommandHandler(null!)
                .ExecuteAsync(grant with { Assessment = grant.Assessment with { Original = intent } });
            await Assert.That(response.IsSuccess).IsFalse();
            await Assert.That(response.Errors).IsNotNull();
        }
    }

    [Test]
    public async Task HistoricalConsentGenerationDoesNotReplaceCurrentSlotCas()
    {
        var command = Withdraw() with { ExpectedConsentGeneration = 3, ExpectedGeneration = 5 };
        await Assert.That((await new ModerationProfileSubjectConsentCommandValidator<WithdrawModerationProfileSubjectConsentCommand>()
            .ValidateAsync(command)).IsValid).IsTrue();
        var request = command.ToWithdrawal();
        await Assert.That(request.ExpectedConsentGeneration).IsEqualTo(3L);
        await Assert.That(request.ExpectedGeneration).IsEqualTo(5L);
        await Assert.That(request.ConsentId).IsEqualTo(command.ConsentId);
        await Assert.That(request.ExpectedConsentRevision).IsEqualTo(1L);
    }

    [Test]
    public async Task MalformedHistoricalCorrectionCannotEnterNativeIdentityTransaction()
    {
        var withdrawal = Withdraw();
        WithdrawModerationProfileSubjectConsentCommand[] invalid =
        [
            withdrawal with { ConsentId = Guid.Empty },
            withdrawal with { ExpectedConsentRevision = 0 },
            withdrawal with { ExpectedConsentRevision = 2 },
            withdrawal with { ExpectedConsentGeneration = 0 },
            withdrawal with { ExpectedConsentGeneration = long.MaxValue },
            withdrawal with { ExpectedGeneration = withdrawal.ExpectedConsentGeneration - 1 },
            withdrawal with { ExpectedGeneration = long.MaxValue }
        ];
        foreach (var command in invalid)
        {
            var response = await new WithdrawModerationProfileSubjectConsentCommandHandler(null!).ExecuteAsync(command);
            await Assert.That(response.IsSuccess).IsFalse();
            await Assert.That(response.Errors).IsNotNull();
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task EntryCancellationCannotStartConsentWriting(bool withdraw)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.That(async () =>
        {
            if (withdraw)
                await new WithdrawModerationProfileSubjectConsentCommandHandler(null!).ExecuteAsync(Withdraw(), cancellation.Token);
            else
                await new GrantModerationProfileSubjectConsentCommandHandler(null!).ExecuteAsync(Grant(), cancellation.Token);
        }).Throws<OperationCanceledException>();
    }

    private static GrantModerationProfileSubjectConsentCommand Grant() => new()
    {
        Assessment = new()
        {
            Original = new()
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
            },
            SourceId = Id(7),
            ExpectedSourceRevision = 1,
            ExpectedSourceGeneration = 9,
            AdoptionId = Id(8),
            ExpectedAdoptionRevision = 1,
            ExpectedAdoptionGeneration = 10
        },
        ConsentId = Id(9),
        ExpectedGeneration = 3,
        ValidUntil = new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero)
    };

    private static WithdrawModerationProfileSubjectConsentCommand Withdraw() => new()
    {
        ConsentId = Id(9),
        ExpectedConsentRevision = 1,
        ExpectedConsentGeneration = 4,
        ExpectedGeneration = 4
    };

    private static Guid Id(int value) => Guid.Parse($"019a0000-0000-7000-8000-{value:x12}");
}
