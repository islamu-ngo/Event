using Explore.Application.Features.AiAssistant.Actions;
using Explore.Application.Features.AiAssistant.Prompting;
using Explore.Domain.Ai;

namespace Event.Application.UnitTests.Features.AiAssistant.Actions;

public sealed class DeleteEventAiActionMapperTests
{
    [Test]
    public async Task Map_WhenPayloadIsConfirmed_ReturnsDeletionContext()
    {
        var eventId = Guid.CreateVersion7();
        var concurrencyStamp = Guid.CreateVersion7();

        var result = new DeleteEventAiActionMapper().Map(
            $$"""
              {
                "eventId": "{{eventId}}",
                "expectedConcurrencyStamp": "{{concurrencyStamp}}",
                "managementContextHasDelete": true,
                "destructiveSummary": "  Remove duplicate draft  ",
                "confirmationPhrase": "DELETE_EVENT",
                "acknowledgedConsequences": true
              }
              """);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.EventId).IsEqualTo(eventId);
        await Assert.That(result.DestructiveContext).IsNotNull();
        await Assert.That(result.DestructiveContext!.ExpectedConcurrencyStamp).IsEqualTo(concurrencyStamp);
        await Assert.That(result.DestructiveContext.ManagementContextHasDelete).IsTrue();
        await Assert.That(result.DestructiveContext.DestructiveSummary).IsEqualTo("Remove duplicate draft");
        await Assert.That(result.DestructiveContext.ConfirmationPhrase).IsEqualTo("DELETE_EVENT");
    }

    [Test]
    public async Task Map_WhenParsedActionKindDiffers_ReturnsUnsupportedActionKindFailure()
    {
        var action = new AiParsedProposedAction(
            AiProposedActionKind.PublishEvent,
            "{\"eventId\":\"00000000-0000-0000-0000-000000000001\"}",
            "Wrong kind");

        var result = new DeleteEventAiActionMapper().Map(action);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("unsupported_action_kind");
    }

    [Test]
    public async Task Map_WhenDeleteAffordanceContextIsMissing_ReturnsFailure()
    {
        var result = new DeleteEventAiActionMapper().Map(
            $$"""
              {
                "eventId": "{{Guid.CreateVersion7()}}",
                "expectedConcurrencyStamp": "{{Guid.CreateVersion7()}}",
                "managementContextHasDelete": false,
                "destructiveSummary": "Delete event",
                "confirmationPhrase": "DELETE_EVENT",
                "acknowledgedConsequences": true
              }
              """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("missing_delete_affordance_context");
    }

    [Test]
    public async Task Map_WhenConfirmationPhraseIsMissing_ReturnsFailure()
    {
        var result = new DeleteEventAiActionMapper().Map(
            $$"""
              {
                "eventId": "{{Guid.CreateVersion7()}}",
                "expectedConcurrencyStamp": "{{Guid.CreateVersion7()}}",
                "managementContextHasDelete": true,
                "destructiveSummary": "Delete event",
                "confirmationPhrase": "delete event",
                "acknowledgedConsequences": true
              }
              """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("missing_destructive_confirmation");
    }

    [Test]
    [Arguments("tenantId")]
    [Arguments("actorId")]
    [Arguments("eventStatusId")]
    [Arguments("sessions")]
    [Arguments("concurrencyStamp")]
    public async Task Map_WhenPayloadContainsPrivilegedField_ReturnsUnsupportedPayloadFieldFailure(string fieldName)
    {
        var result = new DeleteEventAiActionMapper().Map(
            $$"""
              {
                "eventId": "{{Guid.CreateVersion7()}}",
                "expectedConcurrencyStamp": "{{Guid.CreateVersion7()}}",
                "managementContextHasDelete": true,
                "destructiveSummary": "Delete event",
                "confirmationPhrase": "DELETE_EVENT",
                "acknowledgedConsequences": true,
                "{{fieldName}}": "not allowed"
              }
              """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("unsupported_payload_field");
    }
}
