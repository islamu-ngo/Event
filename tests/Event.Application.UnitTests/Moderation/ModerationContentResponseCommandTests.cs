using System.Text;
using Explore.Application.Features.Moderation.Responses;
using Explore.Application.Features.Moderation.Responses.Commands;

namespace Event.Application.UnitTests.Moderation;

public sealed class ModerationContentResponseCommandTests
{
    [Test]
    [Arguments("")]
    [Arguments("   ")]
    public async Task Protected_response_rejects_empty_statements(string statement)
    {
        var result = await new SubmitModerationContentResponseCommandValidator().ValidateAsync(Command(statement));
        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async Task Statement_custody_is_bounded_by_UTF8_bytes_not_character_count()
    {
        var validator = new SubmitModerationContentResponseCommandValidator();
        await Assert.That((await validator.ValidateAsync(Command(new string('a', 8192)))).IsValid).IsTrue();
        var tooLarge = new string('\u00e9', 4097);
        await Assert.That(Encoding.UTF8.GetByteCount(tooLarge)).IsEqualTo(8194);
        await Assert.That((await validator.ValidateAsync(Command(tooLarge))).IsValid).IsFalse();
    }

    [Test]
    public async Task Original_assignment_and_operation_coordinates_are_required_and_diagnostics_exclude_private_text()
    {
        var command = Command("Private response witness.");
        var validator = new SubmitModerationContentResponseCommandValidator();
        await Assert.That((await validator.ValidateAsync(command with { AssignmentRevision = 0 })).IsValid).IsFalse();
        await Assert.That((await validator.ValidateAsync(command with { OperationId = Guid.NewGuid() })).IsValid).IsFalse();
        await Assert.That((await validator.ValidateAsync(command with { SnapshotHash = new string('Z', 64) })).IsValid).IsFalse();
        await Assert.That(command.ToString().Contains(command.Statement, StringComparison.Ordinal)).IsFalse();
    }

    private static SubmitModerationContentResponseCommand Command(string statement) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), new string('a', 64), Guid.CreateVersion7(), 1, statement);
}
