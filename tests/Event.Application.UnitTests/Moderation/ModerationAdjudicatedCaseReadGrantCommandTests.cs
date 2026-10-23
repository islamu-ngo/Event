using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Handlers.Commands;
using Explore.Application.Features.Moderation.Requests.Commands;

namespace Event.Application.UnitTests.Moderation;

public sealed class ModerationAdjudicatedCaseReadGrantCommandTests
{
    [Test]
    [Arguments(1L)]
    [Arguments(long.MaxValue - 1)]
    public async Task GrantAcceptsExactOriginalCoordinatesAndFiniteMicrosecondUtcExpiry(long revision)
    {
        var command = Command() with { ExpectedCaseRevision = revision };
        var result = await new GrantModerationAdjudicatedCaseReadPurposeCommandValidator().ValidateAsync(command);
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    [Arguments(0, "OperationId")]
    [Arguments(1, "OperationId")]
    [Arguments(2, "OperationId")]
    [Arguments(3, "CaseId")]
    [Arguments(4, "CaseId")]
    [Arguments(5, "CaseId")]
    [Arguments(6, "GranteeUserId")]
    [Arguments(7, "GranteeUserId")]
    [Arguments(8, "GranteeUserId")]
    [Arguments(9, "ExpectedCaseRevision")]
    [Arguments(10, "ExpectedCaseRevision")]
    [Arguments(11, "ExpectedCaseRevision")]
    [Arguments(12, "Original")]
    [Arguments(13, "Original.EffectId")]
    [Arguments(14, "Original.EffectId")]
    [Arguments(15, "Original.EffectId")]
    [Arguments(16, "Original.SnapshotHash")]
    [Arguments(17, "Original.SnapshotHash")]
    [Arguments(18, "Original.SnapshotHash")]
    [Arguments(19, "Original.SnapshotHash")]
    [Arguments(20, "Original.SnapshotHash")]
    [Arguments(21, "Original.SnapshotHash")]
    [Arguments(22, "Original.EffectBindingHash")]
    [Arguments(23, "Original.EffectBindingHash")]
    [Arguments(24, "Original.EffectBindingHash")]
    [Arguments(25, "Original.EffectBindingHash")]
    [Arguments(26, "Original.EffectBindingHash")]
    [Arguments(27, "Original.EffectBindingHash")]
    [Arguments(28, "ValidUntil")]
    [Arguments(29, "ValidUntil")]
    [Arguments(30, "ValidUntil")]
    [Arguments(31, "ValidUntil")]
    public async Task GrantRejectsMalformedOriginalCoordinatesAndUnboundedOrImpreciseIntent(int change, string property)
    {
        var command = Command();
        command = change switch
        {
            0 => command with { OperationId = Guid.Empty },
            1 => command with { OperationId = Guid.NewGuid() },
            2 => command with { OperationId = NonRfcVersion7 },
            3 => command with { CaseId = Guid.Empty },
            4 => command with { CaseId = Guid.NewGuid() },
            5 => command with { CaseId = NonRfcVersion7 },
            6 => command with { GranteeUserId = Guid.Empty },
            7 => command with { GranteeUserId = Guid.NewGuid() },
            8 => command with { GranteeUserId = NonRfcVersion7 },
            9 => command with { ExpectedCaseRevision = 0 },
            10 => command with { ExpectedCaseRevision = -1 },
            11 => command with { ExpectedCaseRevision = long.MaxValue },
            12 => command with { Original = null! },
            13 => command with { Original = command.Original with { EffectId = Guid.Empty } },
            14 => command with { Original = command.Original with { EffectId = Guid.NewGuid() } },
            15 => command with { Original = command.Original with { EffectId = NonRfcVersion7 } },
            16 => command with { Original = command.Original with { SnapshotHash = null! } },
            17 => command with { Original = command.Original with { SnapshotHash = string.Empty } },
            18 => command with { Original = command.Original with { SnapshotHash = new('a', 63) } },
            19 => command with { Original = command.Original with { SnapshotHash = new('a', 65) } },
            20 => command with { Original = command.Original with { SnapshotHash = new('A', 64) } },
            21 => command with { Original = command.Original with { SnapshotHash = new('g', 64) } },
            22 => command with { Original = command.Original with { EffectBindingHash = null! } },
            23 => command with { Original = command.Original with { EffectBindingHash = string.Empty } },
            24 => command with { Original = command.Original with { EffectBindingHash = new('b', 63) } },
            25 => command with { Original = command.Original with { EffectBindingHash = new('b', 65) } },
            26 => command with { Original = command.Original with { EffectBindingHash = new('B', 64) } },
            27 => command with { Original = command.Original with { EffectBindingHash = new('g', 64) } },
            28 => command with { ValidUntil = DateTimeOffset.MinValue },
            29 => command with { ValidUntil = DateTimeOffset.MaxValue },
            30 => command with { ValidUntil = command.ValidUntil.ToOffset(TimeSpan.FromHours(1)) },
            _ => command with { ValidUntil = command.ValidUntil.AddTicks(1) }
        };

        var result = await new GrantModerationAdjudicatedCaseReadPurposeCommandValidator().ValidateAsync(command);
        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Any(error => error.PropertyName == property)).IsTrue();
    }

    [Test]
    public async Task RevocationAcceptsOnlyTheExactLiveGrantRevision()
    {
        var result = await new RevokeModerationAdjudicatedCaseReadPurposeCommandValidator().ValidateAsync(Revocation());
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    [Arguments(0, "OperationId")]
    [Arguments(1, "OperationId")]
    [Arguments(2, "OperationId")]
    [Arguments(3, "GrantId")]
    [Arguments(4, "GrantId")]
    [Arguments(5, "GrantId")]
    [Arguments(6, "ExpectedGrantRevision")]
    [Arguments(7, "ExpectedGrantRevision")]
    [Arguments(8, "ExpectedGrantRevision")]
    [Arguments(9, "ExpectedGrantRevision")]
    public async Task RevocationRejectsMalformedOperationOwnershipAndNonLiveRevisions(int change, string property)
    {
        var command = Revocation();
        command = change switch
        {
            0 => command with { OperationId = Guid.Empty },
            1 => command with { OperationId = Guid.NewGuid() },
            2 => command with { OperationId = NonRfcVersion7 },
            3 => command with { GrantId = Guid.Empty },
            4 => command with { GrantId = Guid.NewGuid() },
            5 => command with { GrantId = NonRfcVersion7 },
            6 => command with { ExpectedGrantRevision = 0 },
            7 => command with { ExpectedGrantRevision = -1 },
            8 => command with { ExpectedGrantRevision = 2 },
            _ => command with { ExpectedGrantRevision = long.MaxValue }
        };

        var result = await new RevokeModerationAdjudicatedCaseReadPurposeCommandValidator().ValidateAsync(command);
        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Any(error => error.PropertyName == property)).IsTrue();
    }

    [Test]
    public async Task CommandDiagnosticsDiscloseNamesOnlyRatherThanOriginalOrUserCoordinates()
    {
        await Assert.That(Command().ToString()).IsEqualTo(nameof(GrantModerationAdjudicatedCaseReadPurposeCommand));
        await Assert.That(Revocation().ToString()).IsEqualTo(nameof(RevokeModerationAdjudicatedCaseReadPurposeCommand));
    }

    private static readonly Guid NonRfcVersion7 = new("019a0000-0000-7000-0000-000000000001");

    private static GrantModerationAdjudicatedCaseReadPurposeCommand Command() => new()
    {
        OperationId = Guid.CreateVersion7(), CaseId = Guid.CreateVersion7(), ExpectedCaseRevision = 1,
        GranteeUserId = Guid.CreateVersion7(),
        Original = new ModerationOriginalEffectAnchor(Guid.CreateVersion7(),
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            "fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210"),
        ValidUntil = new DateTimeOffset(2026, 10, 22, 16, 0, 0, TimeSpan.Zero).AddTicks(10)
    };

    private static RevokeModerationAdjudicatedCaseReadPurposeCommand Revocation() => new()
    {
        OperationId = Guid.CreateVersion7(), GrantId = Guid.CreateVersion7(), ExpectedGrantRevision = 1
    };
}
