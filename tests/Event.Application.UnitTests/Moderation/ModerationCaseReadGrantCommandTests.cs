using Explore.Application.Features.Moderation.Handlers.Commands;
using Explore.Application.Features.Moderation.Requests.Commands;

namespace Event.Application.UnitTests.Moderation;

public sealed class ModerationCaseReadGrantCommandTests
{
    [Test]
    public async Task ReadGrantIntentAcceptsAnExactFiniteCaseMaterialPurpose()
    {
        var result = await new GrantModerationCaseReadPurposeCommandValidator().ValidateAsync(Command());
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
    [Arguments(11)]
    [Arguments(12)]
    [Arguments(13)]
    public async Task ReadGrantIntentRejectsMissingWrongVersionStaleOrUnboundedCoordinates(int change)
    {
        var command = Command();
        command = change switch
        {
            0 => command with { OperationId = Guid.Empty },
            1 => command with { OperationId = Guid.NewGuid() },
            2 => command with { CaseId = Guid.Empty },
            3 => command with { CaseId = Guid.NewGuid() },
            4 => command with { SourceId = Guid.Empty },
            5 => command with { SourceId = Guid.NewGuid() },
            6 => command with { GranteeUserId = Guid.Empty },
            7 => command with { GranteeUserId = Guid.NewGuid() },
            8 => command with { ExpectedCaseRevision = 0 },
            9 => command with { ExpectedCaseRevision = long.MaxValue },
            10 => command with { ValidUntil = DateTimeOffset.MinValue },
            11 => command with { ValidUntil = DateTimeOffset.MaxValue },
            12 => command with { ValidUntil = command.ValidUntil.ToOffset(TimeSpan.FromHours(1)) },
            _ => command with { ValidUntil = command.ValidUntil.AddTicks(1) }
        };
        var result = await new GrantModerationCaseReadPurposeCommandValidator().ValidateAsync(command);
        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async Task ReadGrantRevocationAcceptsOnlyItsOriginalLiveRevision()
    {
        var command = new RevokeModerationCaseReadPurposeCommand
        {
            OperationId = Guid.CreateVersion7(), GrantId = Guid.CreateVersion7(), ExpectedGrantRevision = 1
        };
        var result = await new RevokeModerationCaseReadPurposeCommandValidator().ValidateAsync(command);
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    public async Task ReadGrantRevocationRejectsUnownedOperationsAndNonOriginalRevisions(int change)
    {
        var command = new RevokeModerationCaseReadPurposeCommand
        {
            OperationId = Guid.CreateVersion7(), GrantId = Guid.CreateVersion7(), ExpectedGrantRevision = 1
        };
        command = change switch
        {
            0 => command with { OperationId = Guid.Empty },
            1 => command with { OperationId = Guid.NewGuid() },
            2 => command with { GrantId = Guid.Empty },
            3 => command with { GrantId = Guid.NewGuid() },
            4 => command with { ExpectedGrantRevision = 0 },
            _ => command with { ExpectedGrantRevision = 2 }
        };
        var result = await new RevokeModerationCaseReadPurposeCommandValidator().ValidateAsync(command);
        await Assert.That(result.IsValid).IsFalse();
    }

    private static GrantModerationCaseReadPurposeCommand Command() => new()
    {
        OperationId = Guid.CreateVersion7(), CaseId = Guid.CreateVersion7(), ExpectedCaseRevision = 1,
        SourceId = Guid.CreateVersion7(), GranteeUserId = Guid.CreateVersion7(),
        ValidUntil = new(2026, 10, 12, 16, 0, 0, TimeSpan.Zero)
    };
}
