using Explore.Application.Features.Events.Moderation;
using Explore.Application.Features.Events.Requests.Commands;

namespace Event.Application.UnitTests.Features.Events.Moderation;

public sealed class EventModerationReasonCodePolicyTests
{
    [Test]
    public async Task TryNormalizeLight_WhenReasonMissing_UsesDefaultReasonCode()
    {
        var result = EventModerationReasonCodePolicy.TryNormalizeLight(
            reasonCode: null,
            correlationId: null,
            out var metadata,
            out var failureCode,
            out var error);

        await Assert.That(result).IsTrue();
        await Assert.That(metadata.ReasonCode).IsEqualTo(ModerateEventCommand.DefaultReasonCode);
        await Assert.That(metadata.CorrelationId).IsNull();
        await Assert.That(failureCode).IsNull();
        await Assert.That(error).IsNull();
    }

    [Test]
    public async Task TryNormalizeHeavy_WhenReasonAndCorrelationHaveWhitespace_TrimsMetadata()
    {
        var result = EventModerationReasonCodePolicy.TryNormalizeHeavy(
            reasonCode: " illegal_image ",
            correlationId: " case-123 ",
            out var metadata,
            out var failureCode,
            out var error);

        await Assert.That(result).IsTrue();
        await Assert.That(metadata.ReasonCode).IsEqualTo("illegal_image");
        await Assert.That(metadata.CorrelationId).IsEqualTo("case-123");
        await Assert.That(failureCode).IsNull();
        await Assert.That(error).IsNull();
    }

    [Test]
    public async Task TryNormalizeUnmoderation_WhenReasonContainsUnsafeShape_ReturnsFailure()
    {
        var result = EventModerationReasonCodePolicy.TryNormalizeUnmoderation(
            reasonCode: "Illegal words from event title",
            correlationId: null,
            out _,
            out var failureCode,
            out var error);

        await Assert.That(result).IsFalse();
        await Assert.That(failureCode).IsEqualTo(EventModerationReasonCodePolicy.InvalidReasonCodeFailureCode);
        await Assert.That(error).Contains("ReasonCode");
    }

    [Test]
    public async Task TryNormalizeLight_WhenCorrelationIdTooLong_ReturnsFailure()
    {
        var result = EventModerationReasonCodePolicy.TryNormalizeLight(
            reasonCode: "policy_review",
            correlationId: new string('x', EventModerationReasonCodePolicy.MaxCorrelationIdLength + 1),
            out _,
            out var failureCode,
            out var error);

        await Assert.That(result).IsFalse();
        await Assert.That(failureCode).IsEqualTo(EventModerationReasonCodePolicy.InvalidCorrelationIdFailureCode);
        await Assert.That(error).Contains("CorrelationId");
    }
}
