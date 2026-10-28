using Explore.Application.Features.Moderation.Handlers.Commands;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Domain;
using ISLAMU.Moderation.Domain;

namespace Event.Application.UnitTests.Moderation;

public sealed class ModerationCaseAttributionCommandTests
{
    [Test]
    public async Task AttributionIntentAcceptsBoundedCoordinatesWithoutClaimingAuthority()
    {
        var result = await new AttributeModerationCaseCommandValidator().ValidateAsync(Command());
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(7)]
    [Arguments(8)]
    [Arguments(9)]
    [Arguments(10)]
    public async Task AttributionIntentRejectsInvalidIdentityRevisionPurposeOrDeadline(int change)
    {
        var command = Command();
        command = change switch
        {
            0 => command with { OperationId = Guid.Empty },
            1 => command with { OccurrenceCaseId = Guid.NewGuid() },
            2 => command with { ReadGrantId = Guid.Empty },
            3 => command with { SubjectId = Guid.Empty },
            4 => command with { ExpectedCaseRevision = 0 },
            5 => command with { ExpectedCaseRevision = long.MaxValue },
            6 => command with { EvidenceCaseRevision = 0 },
            7 => command with { EvidenceCaseRevision = 2 },
            8 => command with { Basis = default },
            9 => command with { SubjectKind = default },
            _ => command with { ValidUntil = DateTimeOffset.MaxValue }
        };
        var result = await new AttributeModerationCaseCommandValidator().ValidateAsync(command);
        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async Task WithdrawalIntentAcceptsAnExactOriginalSourceRevision()
    {
        var command = new WithdrawModerationCaseAttributionCommand
        {
            SourceId = Guid.CreateVersion7(),
            OperationId = Guid.CreateVersion7(),
            ExpectedSourceRevision = 1
        };
        var result = await new WithdrawModerationCaseAttributionCommandValidator().ValidateAsync(command);
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task WithdrawalIntentRequiresExactTerminalSourceRevision(int change)
    {
        var command = new WithdrawModerationCaseAttributionCommand
        {
            SourceId = Guid.CreateVersion7(),
            OperationId = Guid.CreateVersion7(),
            ExpectedSourceRevision = 1
        };
        command = change switch
        {
            0 => command with { SourceId = Guid.Empty },
            1 => command with { OperationId = Guid.NewGuid() },
            _ => command with { ExpectedSourceRevision = 2 }
        };
        var result = await new WithdrawModerationCaseAttributionCommandValidator().ValidateAsync(command);
        await Assert.That(result.IsValid).IsFalse();
    }

    private static AttributeModerationCaseCommand Command() => new()
    {
        OperationId = Guid.CreateVersion7(),
        OccurrenceCaseId = Guid.CreateVersion7(),
        ExpectedCaseRevision = 1,
        EvidenceCaseRevision = 1,
        ReadGrantId = Guid.CreateVersion7(),
        Basis = ModerationResponsibilityBasis.EventContent,
        SubjectKind = ModerationSubjectKind.Content,
        SubjectId = Guid.CreateVersion7(),
        ValidUntil = new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero)
    };
}
