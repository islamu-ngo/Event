using Explore.Application.Features.Moderation.Responses;
using Explore.Application.Features.Moderation.Responses.Commands;

namespace Event.Application.UnitTests.Moderation;

public sealed class ModerationPreFindingRecipientCommandTests
{
    [Test]
    public async Task ResponseIntentAcceptsBoundedCoordinatesWithoutClaimingRecipientAuthority()
    {
        var result = await new SubmitModerationPreFindingResponseCommandValidator().ValidateAsync(Response());
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    public async Task ResponseIntentRejectsSubstitutedIdentityUnboundedRevisionAndEmptyMaterial(int change)
    {
        var response = Response();
        response = change switch
        {
            0 => response with { OperationId = Guid.Empty },
            1 => response with { CaseId = Guid.NewGuid() },
            2 => response with { NoticeId = Guid.Empty },
            3 => response with { ExpectedCaseRevision = 0 },
            4 => response with { ExpectedNoticeRevision = long.MaxValue },
            _ => response with { Statement = " " }
        };
        await Assert.That((await new SubmitModerationPreFindingResponseCommandValidator()
            .ValidateAsync(response)).IsValid).IsFalse();
    }

    [Test]
    [Arguments(32768, true)]
    [Arguments(32769, false)]
    public async Task ResponsePayloadCeilingCountsUtf8Bytes(int length, bool accepted)
    {
        var response = Response() with { Statement = new string('\u00e9', length) };
        var result = await new SubmitModerationPreFindingResponseCommandValidator().ValidateAsync(response);
        await Assert.That(result.IsValid).IsEqualTo(accepted);
    }

    [Test]
    public async Task AcknowledgementIntentHasItsOwnExactNoticeRevision()
    {
        var response = Response();
        var command = new AcknowledgeModerationPreFindingNoticeCommand
        {
            OperationId = response.OperationId,
            CaseId = response.CaseId,
            ExpectedCaseRevision = response.ExpectedCaseRevision,
            NoticeId = response.NoticeId,
            ExpectedNoticeRevision = response.ExpectedNoticeRevision
        };
        var validator = new AcknowledgeModerationPreFindingNoticeCommandValidator();
        await Assert.That((await validator.ValidateAsync(command)).IsValid).IsTrue();
        await Assert.That((await validator.ValidateAsync(command with { ExpectedNoticeRevision = 0 })).IsValid).IsFalse();
    }

    private static SubmitModerationPreFindingResponseCommand Response() => new()
    {
        OperationId = Guid.CreateVersion7(),
        CaseId = Guid.CreateVersion7(),
        ExpectedCaseRevision = 1,
        NoticeId = Guid.CreateVersion7(),
        ExpectedNoticeRevision = 1,
        Statement = "Subject-owned response"
    };
}
