using Explore.Application.Features.AiAssistant.Actions;
using Explore.Application.Features.AiAssistant.Prompting;
using Explore.Domain.Ai;

namespace Event.Application.UnitTests.Features.AiAssistant.Actions;

public sealed class UpdateEventDraftAiActionMapperTests
{
    [Test]
    public async Task Map_WhenPayloadIsMinimal_ReturnsDraftUpdateRequest()
    {
        var eventId = Guid.CreateVersion7();
        var concurrencyStamp = Guid.CreateVersion7();
        var participationConfigurationConcurrencyStamp = Guid.CreateVersion7();

        var result = new UpdateEventDraftAiActionMapper().Map(
            $$"""
              {
                "eventId": "{{eventId}}",
                "expectedConcurrencyStamp": "{{concurrencyStamp}}",
                "expectedParticipationConfigurationConcurrencyStamp": "{{participationConfigurationConcurrencyStamp}}",
                "participationConfiguration": { "participationHandlingModeId": 1, "advanceRegistrationObligationId": 1 },
                "title": "  Community Iftar Updated  ",
                "description": "Evening meal update",
                "visibilityTypeId": 2,
                "eventFormatId": 1
              }
              """);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.EventId).IsEqualTo(eventId);
        await Assert.That(result.Draft).IsNotNull();
        await Assert.That(result.Draft!.ExpectedConcurrencyStamp).IsEqualTo(concurrencyStamp);
        await Assert.That(result.Draft.Title).IsEqualTo("Community Iftar Updated");
        await Assert.That(result.Draft.Description).IsEqualTo("Evening meal update");
        await Assert.That(result.Draft.VisibilityTypeId).IsEqualTo(2);
        await Assert.That(result.Draft.EventFormatId).IsEqualTo(1);
    }

    [Test]
    public async Task Map_WhenParsedActionKindDiffers_ReturnsUnsupportedActionKindFailure()
    {
        var action = new AiParsedProposedAction(
            AiProposedActionKind.CreateEventDraft,
            "{\"title\":\"Draft\"}",
            "Wrong kind");

        var result = new UpdateEventDraftAiActionMapper().Map(action);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("unsupported_action_kind");
    }

    [Test]
    public async Task Map_WhenEventIdIsMissing_ReturnsMissingEventIdFailure()
    {
        var result = new UpdateEventDraftAiActionMapper().Map(
            $"{{\"expectedConcurrencyStamp\":\"{Guid.CreateVersion7()}\",\"title\":\"Draft\"}}");

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("missing_event_id");
    }

    [Test]
    public async Task Map_WhenExpectedConcurrencyStampIsMissing_ReturnsMissingConcurrencyFailure()
    {
        var result = new UpdateEventDraftAiActionMapper().Map(
            $"{{\"eventId\":\"{Guid.CreateVersion7()}\",\"title\":\"Draft\"}}");

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("missing_expected_concurrency_stamp");
    }

    [Test]
    public async Task Map_WhenTitleIsMissing_ReturnsMissingTitleFailure()
    {
        var result = new UpdateEventDraftAiActionMapper().Map(
            $"{{\"eventId\":\"{Guid.CreateVersion7()}\",\"expectedConcurrencyStamp\":\"{Guid.CreateVersion7()}\",\"expectedParticipationConfigurationConcurrencyStamp\":\"{Guid.CreateVersion7()}\",\"participationConfiguration\":{{\"participationHandlingModeId\":1,\"advanceRegistrationObligationId\":1}}}}");

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("missing_title");
    }

    [Test]
    public async Task Map_WhenTitleIsTooLong_ReturnsFieldTooLongFailure()
    {
        var result = new UpdateEventDraftAiActionMapper().Map(
            $$"""
              {
                "eventId": "{{Guid.CreateVersion7()}}",
                "expectedConcurrencyStamp": "{{Guid.CreateVersion7()}}",
                "expectedParticipationConfigurationConcurrencyStamp": "{{Guid.CreateVersion7()}}",
                "participationConfiguration": { "participationHandlingModeId": 1, "advanceRegistrationObligationId": 1 },
                "title": "{{new string('A', 501)}}"
              }
              """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("field_too_long");
    }

    [Test]
    [Arguments("tenantId")]
    [Arguments("actorId")]
    [Arguments("organizationId")]
    [Arguments("eventStatusId")]
    [Arguments("concurrencyStamp")]
    [Arguments("sessions")]
    [Arguments("firstSessionStartUtc")]
    [Arguments("sourceTemplateVersion")]
    public async Task Map_WhenPayloadContainsPrivilegedField_ReturnsUnsupportedPayloadFieldFailure(string fieldName)
    {
        var result = new UpdateEventDraftAiActionMapper().Map(
            $$"""
              {
                "eventId": "{{Guid.CreateVersion7()}}",
                "expectedConcurrencyStamp": "{{Guid.CreateVersion7()}}",
                "title": "Draft",
                "{{fieldName}}": "not allowed"
              }
              """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("unsupported_payload_field");
    }

    [Test]
    public async Task Map_WhenPayloadContainsUnknownField_ReturnsUnsupportedPayloadFieldFailure()
    {
        var result = new UpdateEventDraftAiActionMapper().Map(
            $$"""
              {
                "eventId": "{{Guid.CreateVersion7()}}",
                "expectedConcurrencyStamp": "{{Guid.CreateVersion7()}}",
                "title": "Draft",
                "unexpected": true
              }
              """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("unsupported_payload_field");
    }

    [Test]
    public async Task Map_WhenRemovedPriceFieldIsPresent_ReturnsUnsupportedPayloadFieldFailure()
    {
        var result = new UpdateEventDraftAiActionMapper().Map(
            $$"""
              {
                "eventId": "{{Guid.CreateVersion7()}}",
                "expectedConcurrencyStamp": "{{Guid.CreateVersion7()}}",
                "expectedParticipationConfigurationConcurrencyStamp": "{{Guid.CreateVersion7()}}",
                "participationConfiguration": { "participationHandlingModeId": 1, "advanceRegistrationObligationId": 1 },
                "title": "Draft",
                "price": -1
              }
              """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("unsupported_payload_field");
    }

    [Test]
    public async Task Map_WhenSeriesOrderIsNegative_ReturnsInvalidNumericValueFailure()
    {
        var result = new UpdateEventDraftAiActionMapper().Map(
            $$"""
              {
                "eventId": "{{Guid.CreateVersion7()}}",
                "expectedConcurrencyStamp": "{{Guid.CreateVersion7()}}",
                "expectedParticipationConfigurationConcurrencyStamp": "{{Guid.CreateVersion7()}}",
                "participationConfiguration": { "participationHandlingModeId": 1, "advanceRegistrationObligationId": 1 },
                "title": "Draft",
                "seriesOrder": -1
              }
              """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("invalid_numeric_value");
    }
}
