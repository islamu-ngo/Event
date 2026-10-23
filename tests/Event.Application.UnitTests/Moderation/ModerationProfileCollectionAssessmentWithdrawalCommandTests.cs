using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Handlers.Commands;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Application.Services.Moderation;
using ISLAMU.Moderation.Domain;

namespace Event.Application.UnitTests.Moderation;

public sealed class ModerationProfileCollectionAssessmentWithdrawalCommandTests
{
    private static readonly AuthorityScope Instance = AuthorityScope.Instance(
        AuthorityScope.Solution(AuthorityScope.TrustDomain(Id(30)), Id(31)), Id(32));

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OriginalIntentRetainsHumanScopeAndIndependentAssessmentGenerations(bool tenant)
    {
        var command = Create() with { Jurisdiction = tenant ? AuthorityScope.Tenant(Instance, Id(33)) : Instance };
        await Assert.That((await new WithdrawModerationProfileCollectionAssessmentCommandValidator()
            .ValidateAsync(command)).IsValid).IsTrue();
        var expected = new WithdrawModerationProfileCollectionAssessment(
            command.Jurisdiction, command.SubjectHumanId, command.PolicyId, 4,
            command.PublicationId, command.ProfileId, command.Mode, command.SourceId, 2,
            command.AdoptionId, 3, 1);
        await Assert.That(command.ToWithdrawal()).IsEqualTo(expected);
        await Assert.That(command.SubjectHumanId.Version).IsEqualTo(4);
    }

    [Test]
    public async Task MalformedIntentCannotReachNativeCorrection()
    {
        var command = Create();
        var malformedVariant = Guid.Parse("019a0000-0000-7000-7000-000000000001");
        WithdrawModerationProfileCollectionAssessmentCommand[] invalid =
        [
            command with { Jurisdiction = null! },
            command with { Jurisdiction = AuthorityScope.TrustDomain(Id(30)) },
            command with { Jurisdiction = AuthorityScope.Resource(AuthorityScope.Tenant(Instance, Id(33)), Id(34)) },
            command with { SubjectHumanId = Guid.Empty },
            command with { PolicyId = Guid.Empty },
            command with { PublicationId = Guid.Parse("01234567-89ab-4cde-8fab-0123456789ab") },
            command with { ProfileId = malformedVariant },
            command with { SourceId = Guid.Empty },
            command with { AdoptionId = malformedVariant },
            command with { AdoptionId = command.SourceId },
            command with { SourceId = command.SubjectHumanId },
            command with { PolicyRevision = 0 },
            command with { PolicyRevision = long.MaxValue },
            command with { ExpectedSourceGeneration = 0 },
            command with { ExpectedSourceGeneration = long.MaxValue },
            command with { ExpectedAdoptionGeneration = 2 },
            command with { ExpectedAdoptionGeneration = long.MaxValue },
            command with { ExpectedAdoptionRevision = 2 },
            command with { Mode = (ModerationNativeMode)999 }
        ];
        foreach (var request in invalid)
        {
            var response = await new WithdrawModerationProfileCollectionAssessmentCommandHandler(null!)
                .ExecuteAsync(request);
            await Assert.That(response.IsSuccess).IsFalse();
            await Assert.That(response.Errors).IsNotNull();
            await Assert.That(response.FailureCode).IsNull();
        }
    }

    [Test]
    public async Task CancellationCannotBecomeAValidationResponse()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.That(async () => await new WithdrawModerationProfileCollectionAssessmentCommandHandler(null!)
            .ExecuteAsync(Create() with { SubjectHumanId = Guid.Empty }, cancellation.Token))
            .Throws<OperationCanceledException>();
    }

    private static WithdrawModerationProfileCollectionAssessmentCommand Create() => new()
    {
        Jurisdiction = Instance,
        SubjectHumanId = Guid.Parse("01234567-89ab-4cde-8fab-0123456789ab"),
        PolicyId = Id(1),
        PolicyRevision = 4,
        PublicationId = Id(2),
        ProfileId = Id(3),
        Mode = ModerationNativeMode.Embedded,
        SourceId = Id(4),
        ExpectedSourceGeneration = 2,
        AdoptionId = Id(5),
        ExpectedAdoptionGeneration = 3,
        ExpectedAdoptionRevision = 1
    };

    private static Guid Id(int value) => Guid.Parse($"019a0000-0000-7000-8000-{value:x12}");
}
