using Explore.Application.Features.AiAssistant.Actions;
using Explore.Application.Features.AiAssistant.Prompting;
using Explore.Domain.Ai;

namespace Event.Application.UnitTests.Features.AiAssistant.Actions;

public sealed class PublishEventAiActionMapperTests
{
    [Test]
    public async Task Map_WhenPayloadIsReady_ReturnsPublishRequest()
    {
        var eventId = Guid.CreateVersion7();
        var concurrencyStamp = Guid.CreateVersion7();

        var result = new PublishEventAiActionMapper().Map(
            $$"""
              {
                "eventId": "{{eventId}}",
                "expectedConcurrencyStamp": "{{concurrencyStamp}}",
                "readinessIsReady": true,
                "readinessErrorCount": 0,
                "readinessCheckedAtUtc": "2026-06-23T12:00:00Z",
                "readinessSummary": "  Ready to publish  "
              }
              """);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.EventId).IsEqualTo(eventId);
        await Assert.That(result.Request).IsNotNull();
        await Assert.That(result.Request!.ExpectedConcurrencyStamp).IsEqualTo(concurrencyStamp);
        await Assert.That(result.ReadinessContext).IsNotNull();
        await Assert.That(result.ReadinessContext!.IsReady).IsTrue();
        await Assert.That(result.ReadinessContext.ErrorCount).IsEqualTo(0);
        await Assert.That(result.ReadinessContext.Summary).IsEqualTo("Ready to publish");
    }

    [Test]
    public async Task Map_WhenParsedActionKindDiffers_ReturnsUnsupportedActionKindFailure()
    {
        var action = new AiParsedProposedAction(
            AiProposedActionKind.CreateEventDraft,
            "{\"eventId\":\"00000000-0000-0000-0000-000000000001\"}",
            "Wrong kind");

        var result = new PublishEventAiActionMapper().Map(action);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("unsupported_action_kind");
    }

    [Test]
    public async Task Map_WhenEventIdIsMissing_ReturnsMissingEventIdFailure()
    {
        var result = new PublishEventAiActionMapper().Map(
            $"{{\"expectedConcurrencyStamp\":\"{Guid.CreateVersion7()}\",\"readinessIsReady\":true,\"readinessErrorCount\":0}}");

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("missing_event_id");
    }

    [Test]
    public async Task Map_WhenExpectedConcurrencyStampIsMissing_ReturnsMissingConcurrencyFailure()
    {
        var result = new PublishEventAiActionMapper().Map(
            $"{{\"eventId\":\"{Guid.CreateVersion7()}\",\"readinessIsReady\":true,\"readinessErrorCount\":0}}");

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("missing_expected_concurrency_stamp");
    }

    [Test]
    public async Task Map_WhenReadinessIsNotReady_ReturnsReadinessFailure()
    {
        var result = new PublishEventAiActionMapper().Map(
            $"{{\"eventId\":\"{Guid.CreateVersion7()}\",\"expectedConcurrencyStamp\":\"{Guid.CreateVersion7()}\",\"readinessIsReady\":false,\"readinessErrorCount\":0}}");

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("publish_readiness_not_ready");
    }

    [Test]
    public async Task Map_WhenReadinessHasErrors_ReturnsReadinessErrorsFailure()
    {
        var result = new PublishEventAiActionMapper().Map(
            $"{{\"eventId\":\"{Guid.CreateVersion7()}\",\"expectedConcurrencyStamp\":\"{Guid.CreateVersion7()}\",\"readinessIsReady\":true,\"readinessErrorCount\":1}}");

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("publish_readiness_has_errors");
    }

    [Test]
    public async Task Map_WhenReadinessSummaryIsTooLong_ReturnsFieldTooLongFailure()
    {
        var result = new PublishEventAiActionMapper().Map(
            $$"""
              {
                "eventId": "{{Guid.CreateVersion7()}}",
                "expectedConcurrencyStamp": "{{Guid.CreateVersion7()}}",
                "readinessIsReady": true,
                "readinessErrorCount": 0,
                "readinessSummary": "{{new string('A', 1001)}}"
              }
              """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("field_too_long");
    }

    [Test]
    [Arguments("tenantId")]
    [Arguments("actorId")]
    [Arguments("title")]
    [Arguments("eventStatusId")]
    [Arguments("isPublished")]
    [Arguments("publishedAt")]
    [Arguments("outboxMessages")]
    [Arguments("concurrencyStamp")]
    public async Task Map_WhenPayloadContainsPrivilegedField_ReturnsUnsupportedPayloadFieldFailure(string fieldName)
    {
        var result = new PublishEventAiActionMapper().Map(
            $$"""
              {
                "eventId": "{{Guid.CreateVersion7()}}",
                "expectedConcurrencyStamp": "{{Guid.CreateVersion7()}}",
                "readinessIsReady": true,
                "readinessErrorCount": 0,
                "{{fieldName}}": "not allowed"
              }
              """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("unsupported_payload_field");
    }

    [Test]
    public async Task Map_WhenPayloadContainsUnknownField_ReturnsUnsupportedPayloadFieldFailure()
    {
        var result = new PublishEventAiActionMapper().Map(
            $$"""
              {
                "eventId": "{{Guid.CreateVersion7()}}",
                "expectedConcurrencyStamp": "{{Guid.CreateVersion7()}}",
                "readinessIsReady": true,
                "readinessErrorCount": 0,
                "unexpected": true
              }
              """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("unsupported_payload_field");
    }
}
