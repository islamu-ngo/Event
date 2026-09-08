using Explore.Application.Features.AiAssistant.Actions;
using Explore.Application.Features.AiAssistant.Prompting;
using Explore.Domain.Ai;

namespace Event.Application.UnitTests.Features.AiAssistant.Actions;

public sealed class EventModerationAiActionMapperTests
{
    [Test]
    public async Task Map_WhenLightModerationPayloadIsValid_ReturnsModerationContext()
    {
        var eventId = Guid.CreateVersion7();
        var concurrencyStamp = Guid.CreateVersion7();

        var result = new EventModerationAiActionMapper().Map(
            AiProposedActionKind.LightModerateEvent,
            $$"""
              {
                "eventId": "{{eventId}}",
                "expectedConcurrencyStamp": "{{concurrencyStamp}}",
                "managementContextHasModerateLight": true,
                "reasonCode": " policy_violation ",
                "correlationId": " report-123 "
              }
              """);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Kind).IsEqualTo(AiProposedActionKind.LightModerateEvent);
        await Assert.That(result.EventId).IsEqualTo(eventId);
        await Assert.That(result.ExpectedConcurrencyStamp).IsEqualTo(concurrencyStamp);
        await Assert.That(result.ReasonCode).IsEqualTo("policy_violation");
        await Assert.That(result.CorrelationId).IsEqualTo("report-123");
        await Assert.That(result.Destructive).IsFalse();
    }

    [Test]
    public async Task Map_WhenHeavyModerationPayloadIsValid_ReturnsDestructiveContext()
    {
        var eventId = Guid.CreateVersion7();
        var concurrencyStamp = Guid.CreateVersion7();

        var result = new EventModerationAiActionMapper().Map(
            AiProposedActionKind.HeavyModerateEvent,
            $$"""
              {
                "eventId": "{{eventId}}",
                "expectedConcurrencyStamp": "{{concurrencyStamp}}",
                "managementContextHasModerateHeavy": true,
                "reasonCode": "severe_policy_violation",
                "destructiveSummary": "  Irreversibly redact public event content and media.  ",
                "confirmationPhrase": "HEAVY_MODERATE_EVENT",
                "acknowledgedConsequences": true
              }
              """);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Kind).IsEqualTo(AiProposedActionKind.HeavyModerateEvent);
        await Assert.That(result.EventId).IsEqualTo(eventId);
        await Assert.That(result.ExpectedConcurrencyStamp).IsEqualTo(concurrencyStamp);
        await Assert.That(result.DestructiveSummary).IsEqualTo("Irreversibly redact public event content and media.");
        await Assert.That(result.Destructive).IsTrue();
    }

    [Test]
    public async Task Map_WhenParsedActionKindDiffers_ReturnsUnsupportedActionKindFailure()
    {
        var action = new AiParsedProposedAction(
            AiProposedActionKind.DeleteEvent,
            "{\"eventId\":\"00000000-0000-0000-0000-000000000001\"}",
            "Wrong kind");

        var result = new EventModerationAiActionMapper().Map(action);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("unsupported_action_kind");
    }

    [Test]
    public async Task Map_WhenLightModerationHalContextIsFalse_ReturnsSchemaFailure()
    {
        var result = new EventModerationAiActionMapper().Map(
            AiProposedActionKind.LightModerateEvent,
            $$"""
              {
                "eventId": "{{Guid.CreateVersion7()}}",
                "expectedConcurrencyStamp": "{{Guid.CreateVersion7()}}",
                "managementContextHasModerateLight": false,
                "reasonCode": "policy_violation"
              }
              """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("invalid_tool_argument_value");
    }

    [Test]
    public async Task Map_WhenHeavyModerationAcknowledgementIsMissing_ReturnsFailure()
    {
        var result = new EventModerationAiActionMapper().Map(
            AiProposedActionKind.HeavyModerateEvent,
            $$"""
              {
                "eventId": "{{Guid.CreateVersion7()}}",
                "expectedConcurrencyStamp": "{{Guid.CreateVersion7()}}",
                "managementContextHasModerateHeavy": true,
                "reasonCode": "severe_policy_violation",
                "destructiveSummary": "Redact public event content and media.",
                "confirmationPhrase": "HEAVY_MODERATE_EVENT",
                "acknowledgedConsequences": false
              }
              """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("invalid_tool_argument_value");
    }

    [Test]
    [Arguments("tenantId")]
    [Arguments("actorId")]
    [Arguments("eventStatusId")]
    [Arguments("moderationRecords")]
    [Arguments("concurrencyStamp")]
    public async Task Map_WhenPayloadContainsPrivilegedField_ReturnsValidationFailure(string fieldName)
    {
        var result = new EventModerationAiActionMapper().Map(
            AiProposedActionKind.UnmoderateEvent,
            $$"""
              {
                "eventId": "{{Guid.CreateVersion7()}}",
                "expectedConcurrencyStamp": "{{Guid.CreateVersion7()}}",
                "managementContextHasUnmoderate": true,
                "reasonCode": "appeal_approved",
                "{{fieldName}}": "not allowed"
              }
              """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("forbidden_tool_argument");
    }
}
