using Explore.Application.Features.AiAssistant.Actions;
using Explore.Domain.Ai;

namespace Event.Application.UnitTests.Features.AiAssistant.Actions;

public sealed class EventSubResourceAiActionMapperTests
{
    [Test]
    public async Task Map_WhenSessionCreatePayloadIsValid_ReturnsTargetEventContext()
    {
        var eventId = Guid.CreateVersion7();
        var result = new EventSubResourceAiActionMapper().Map(
            AiProposedActionKind.CreateEventSession,
            $$"""
              {
                "eventId": "{{eventId}}",
                "expectedConcurrencyStamp": "{{Guid.CreateVersion7()}}",
                "managementContextHasAddSession": true,
                "title": "Opening session",
                "startTime": "2026-07-01T09:00:00Z",
                "endTime": "2026-07-01T10:00:00Z"
              }
              """);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Kind).IsEqualTo(AiProposedActionKind.CreateEventSession);
        await Assert.That(result.EventId).IsEqualTo(eventId);
        await Assert.That(result.TargetId).IsNull();
    }

    [Test]
    public async Task Map_WhenDeletePayloadIsValid_RequiresDestructiveConfirmation()
    {
        var sessionId = Guid.CreateVersion7();
        var eventId = Guid.CreateVersion7();

        var result = new EventSubResourceAiActionMapper().Map(
            AiProposedActionKind.DeleteEventSession,
            $$"""
              {
                "sessionId": "{{sessionId}}",
                "eventId": "{{eventId}}",
                "expectedConcurrencyStamp": "{{Guid.CreateVersion7()}}",
                "managementContextHasDelete": true,
                "destructiveSummary": "Remove duplicate session.",
                "confirmationPhrase": "DELETE_EVENT_SESSION",
                "acknowledgedConsequences": true
              }
              """);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.EventId).IsEqualTo(eventId);
        await Assert.That(result.TargetId).IsEqualTo(sessionId);
        await Assert.That(result.Destructive).IsTrue();
    }

    [Test]
    public async Task Map_WhenForbiddenServerOwnedFieldIsPresent_FailsClosed()
    {
        var result = new EventSubResourceAiActionMapper().Map(
            AiProposedActionKind.CreateEventSession,
            $$"""
              {
                "eventId": "{{Guid.CreateVersion7()}}",
                "expectedConcurrencyStamp": "{{Guid.CreateVersion7()}}",
                "managementContextHasAddSession": true,
                "title": "Opening session",
                "startTime": "2026-07-01T09:00:00Z",
                "endTime": "2026-07-01T10:00:00Z",
                "tenantId": "{{Guid.CreateVersion7()}}"
              }
              """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("forbidden_tool_argument");
    }

    [Test]
    public async Task Map_WhenTemplateSyncPlanIsValid_ReturnsEventContext()
    {
        var eventId = Guid.CreateVersion7();
        var result = new EventSubResourceAiActionMapper().Map(
            AiProposedActionKind.ApplyEventTemplateSync,
            $$"""
              {
                "eventId": "{{eventId}}",
                "expectedConcurrencyStamp": "{{Guid.CreateVersion7()}}",
                "managementContextHasEdit": true,
                "baseProvenanceVersion": 2,
                "plan": {
                  "targetTemplateVersion": 3,
                  "baseProvenanceVersion": 2,
                  "addedDefinitionKeys": ["sessions.track"],
                  "modifiedDefinitionKeys": [],
                  "retiredDefinitionKeys": [],
                  "addedOptionKeys": [],
                  "modifiedOptionKeys": [],
                  "retiredOptionKeys": []
                },
                "destructiveSummary": "Apply reviewed template sync changes.",
                "confirmationPhrase": "APPLY_EVENT_TEMPLATE_SYNC",
                "acknowledgedConsequences": true
              }
              """);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.EventId).IsEqualTo(eventId);
        await Assert.That(result.Destructive).IsTrue();
    }
}
