using System.Text.Json;
using Explore.Application.Contracts.Infrastructure.Ai;
using Explore.Application.Features.AiAssistant.Actions;
using Explore.Application.Features.AiAssistant.Prompting;
using Explore.Application.Features.AiAssistant.Tools;
using Explore.Domain.Ai;

namespace Event.Application.UnitTests.Features.AiAssistant.Prompting;

public sealed class AiStructuredActionParserTests
{
    [Test]
    public async Task Parse_WhenPayloadIsJsonObject_ReturnsParsedAction()
    {
        var result = new AiStructuredActionParser().Parse(
            [new AiProposedActionCandidate(AiProposedActionKind.CreateEventDraft, "{\"title\":\"Draft\",\"participationConfiguration\":{\"participationHandlingModeId\":1,\"advanceRegistrationObligationId\":1}}", "Create draft")]);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Actions.Count).IsEqualTo(1);
        await Assert.That(result.Actions[0].Kind).IsEqualTo(AiProposedActionKind.CreateEventDraft);
        await Assert.That(result.Actions[0].Summary).IsEqualTo("Create draft");
    }

    [Test]
    public async Task Parse_WhenCreateEventDraftContainsMalformedOwnerScope_DropsOwnerScope()
    {
        var result = new AiStructuredActionParser().Parse(
        [
            new AiProposedActionCandidate(
                AiProposedActionKind.CreateEventDraft,
                "{\"title\":\"Community Dinner\",\"organizationId\":\"example-org\",\"participationConfiguration\":{\"participationHandlingModeId\":1,\"advanceRegistrationObligationId\":1}}",
                "Create draft")
        ]);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Actions.Count).IsEqualTo(1);
        await Assert.That(result.Actions[0].PayloadJson).Contains("Community Dinner");
        await Assert.That(result.Actions[0].PayloadJson).DoesNotContain("organizationId");
    }

    [Test]
    public async Task Parse_WhenCreateEventDraftContainsValidReferenceIds_PreservesThoseIds()
    {
        var validCategoryId = Guid.CreateVersion7();
        var validTagId = Guid.CreateVersion7();
        var result = new AiStructuredActionParser().Parse(
        [
            new AiProposedActionCandidate(
                AiProposedActionKind.CreateEventDraft,
                $"{{\"title\":\"Community Dinner\",\"eventTypeId\":999,\"categoryIds\":[\"{validCategoryId}\"],\"tagIds\":[\"{validTagId}\"],\"participationConfiguration\":{{\"participationHandlingModeId\":1,\"advanceRegistrationObligationId\":1}}}}",
                "Create draft")
        ]);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Actions[0].PayloadJson).Contains("eventTypeId");
        await Assert.That(result.Actions[0].PayloadJson).Contains(validCategoryId.ToString());
        await Assert.That(result.Actions[0].PayloadJson).Contains(validTagId.ToString());
    }

    [Test]
    public async Task Parse_WhenCreateEventDraftUsesPosterStyleAliases_NormalizesToStructuredDraftPayload()
    {
        var result = new AiStructuredActionParser().Parse(
        [
            new AiProposedActionCandidate(
                AiProposedActionKind.CreateEventDraft,
                """
                  {
                    "title": "Community Iftar",
                    "startTime": "2026-07-10T18:00:00Z",
                    "endTime": "2026-07-10T20:00:00Z",
                    "locationName": "Islamic Centre",
                    "address": "Main Street 1",
                    "postcode": "1000",
                    "country": "Belgium",
                    "city": "Brussels",
                    "roomName": "Main Hall",
                    "genderMode": 3,
                    "participationConfiguration": {
                      "participationHandlingModeId": 1,
                      "advanceRegistrationObligationId": 1
                    }
                  }
                  """,
                "Create draft")
        ]);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Actions.Count).IsEqualTo(1);
        await Assert.That(result.Actions[0].PayloadJson).Contains("\"session\":");
        await Assert.That(result.Actions[0].PayloadJson).Contains("\"location\":");
        await Assert.That(result.Actions[0].PayloadJson).Contains("\"room\":");
        await Assert.That(result.Actions[0].PayloadJson).Contains("\"islamicAspect\":");
        await Assert.That(result.Actions[0].PayloadJson).DoesNotContain("locationName");
        await Assert.That(result.Actions[0].PayloadJson).DoesNotContain("roomName");

        using var document = JsonDocument.Parse(result.Actions[0].PayloadJson);
        await Assert.That(document.RootElement.TryGetProperty("genderMode", out _)).IsFalse();
        await Assert.That(document.RootElement.GetProperty("islamicAspect").GetProperty("genderMode").GetInt32()).IsEqualTo(3);

        var mapping = new CreateEventDraftAiActionMapper().Map(result.Actions[0]);

        await Assert.That(mapping.Succeeded).IsTrue();
        await Assert.That(mapping.Draft!.Locations.Count).IsEqualTo(1);
        await Assert.That(mapping.Draft.Rooms.Count).IsEqualTo(1);
        await Assert.That(mapping.Draft.Sessions.Count).IsEqualTo(1);
        await Assert.That(mapping.Draft.Sessions[0].StartTime).IsEqualTo(new DateTimeOffset(2026, 7, 10, 18, 0, 0, TimeSpan.Zero));
    }

    [Test]
    public async Task Parse_WhenCreateEventDraftContainsPartialPosterNestedDetails_DropsIncompleteNestedRows()
    {
        var result = new AiStructuredActionParser().Parse(
        [
            new AiProposedActionCandidate(
                AiProposedActionKind.CreateEventDraft,
                """
                  {
                    "title": "Community Iftar",
                    "location": {
                      "fullName": "Islamic Centre"
                    },
                    "room": {
                      "name": "Main Hall"
                    },
                    "session": {
                      "startTime": "2026-07-10T18:00:00Z"
                    },
                    "participationConfiguration": {
                      "participationHandlingModeId": 1,
                      "advanceRegistrationObligationId": 1
                    }
                  }
                  """,
                "Create draft")
        ]);

        await Assert.That(result.Succeeded).IsTrue();

        using var document = JsonDocument.Parse(result.Actions[0].PayloadJson);
        await Assert.That(document.RootElement.TryGetProperty("location", out _)).IsFalse();
        await Assert.That(document.RootElement.TryGetProperty("room", out _)).IsFalse();
        await Assert.That(document.RootElement.TryGetProperty("session", out _)).IsFalse();

        var mapping = new CreateEventDraftAiActionMapper().Map(result.Actions[0]);

        await Assert.That(mapping.Succeeded).IsTrue();
        await Assert.That(mapping.Draft!.Locations).IsEmpty();
        await Assert.That(mapping.Draft.Rooms).IsEmpty();
        await Assert.That(mapping.Draft.Sessions).IsEmpty();
    }

    [Test]
    public async Task Parse_WhenPayloadIsInvalidJson_ReturnsInvalidToolArgumentsFailure()
    {
        var result = new AiStructuredActionParser().Parse(
            [new AiProposedActionCandidate(AiProposedActionKind.CreateEventDraft, "not-json")]);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("invalid_tool_arguments");
    }

    [Test]
    public async Task Parse_WhenPayloadIsJsonArray_ReturnsInvalidToolArgumentsFailure()
    {
        var result = new AiStructuredActionParser().Parse(
            [new AiProposedActionCandidate(AiProposedActionKind.CreateEventDraft, "[]")]);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("invalid_tool_arguments");
    }

    [Test]
    public async Task Parse_WhenPayloadFailsSchemaValidation_ReturnsCorrectionMessage()
    {
        var result = new AiStructuredActionParser().Parse(
            [new AiProposedActionCandidate(AiProposedActionKind.CreateEventDraft, "{\"description\":\"Missing title\"}")]);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("missing_tool_argument");
        await Assert.That(result.FailureMessage).DoesNotContain("title");
        await Assert.That(result.CorrectionMessage).Contains("matches the registered schema exactly");
        await Assert.That(result.CorrectionMessage).DoesNotContain("Missing title");
    }

    [Test]
    public async Task Parse_WhenPayloadContainsForbiddenField_ReturnsForbiddenToolArgumentFailure()
    {
        var result = new AiStructuredActionParser().Parse(
            [new AiProposedActionCandidate(AiProposedActionKind.CreateEventDraft, "{\"title\":\"Draft\",\"eventStatusId\":2}")]);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("forbidden_tool_argument");
        await Assert.That(result.FailureMessage).DoesNotContain("eventStatusId");
    }

    [Test]
    public async Task Parse_WhenActionKindIsUnknown_ReturnsUnknownActionFailure()
    {
        var result = new AiStructuredActionParser().Parse(
            [new AiProposedActionCandidate((AiProposedActionKind)999, "{\"title\":\"Draft\"}")]);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("unknown_action_kind");
    }

    [Test]
    public async Task Parse_WhenRegistryDoesNotContainKind_ReturnsUnknownActionFailure()
    {
        var result = new AiStructuredActionParser(new AiToolContractRegistry([])).Parse(
            [new AiProposedActionCandidate(AiProposedActionKind.CreateEventDraft, "{\"title\":\"Draft\"}")]);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("unknown_action_kind");
    }

    [Test]
    public async Task Parse_WhenKindIsMcpOnly_ReturnsUnknownActionFailure()
    {
        foreach (var candidate in CreateMcpOnlyCandidates())
        {
            var result = new AiStructuredActionParser().Parse([candidate]);

            await Assert.That(result.Succeeded).IsFalse();
            await Assert.That(result.FailureCode).IsEqualTo("unknown_action_kind");
        }
    }

    private static IReadOnlyList<AiProposedActionCandidate> CreateMcpOnlyCandidates()
        =>
        [
            new(
                AiProposedActionKind.UpdateEventDraft,
                $$"""
                  {
                    "eventId": "{{Guid.CreateVersion7()}}",
                    "expectedConcurrencyStamp": "{{Guid.CreateVersion7()}}",
                    "title": "Draft update"
                  }
                  """),
            new(
                AiProposedActionKind.PublishEvent,
                $$"""
                  {
                    "eventId": "{{Guid.CreateVersion7()}}",
                    "expectedConcurrencyStamp": "{{Guid.CreateVersion7()}}",
                    "readinessIsReady": true,
                    "readinessErrorCount": 0
                  }
                  """),
            new(
                AiProposedActionKind.DeleteEvent,
                $$"""
                  {
                    "eventId": "{{Guid.CreateVersion7()}}",
                    "expectedConcurrencyStamp": "{{Guid.CreateVersion7()}}",
                    "managementContextHasDelete": true,
                    "destructiveSummary": "Delete event.",
                    "confirmationPhrase": "DELETE_EVENT",
                    "acknowledgedConsequences": true
                  }
                  """),
            new(
                AiProposedActionKind.UpsertEventIslamicAspect,
                $$"""
                  {
                    "eventId": "{{Guid.CreateVersion7()}}",
                    "expectedConcurrencyStamp": "{{Guid.CreateVersion7()}}",
                    "aspectKind": "islamic",
                    "managementContextHasEdit": true,
                    "genderMode": 0
                  }
                  """),
            new(
                AiProposedActionKind.DeleteEventIslamicAspect,
                $$"""
                  {
                    "eventId": "{{Guid.CreateVersion7()}}",
                    "expectedConcurrencyStamp": "{{Guid.CreateVersion7()}}",
                    "aspectKind": "islamic",
                    "managementContextHasEdit": true,
                    "destructiveSummary": "Delete Islamic aspect.",
                    "confirmationPhrase": "DELETE_ISLAMIC_ASPECT",
                    "acknowledgedConsequences": true
                  }
                  """),
            new(
                AiProposedActionKind.UpsertEventTechAspect,
                $$"""
                  {
                    "eventId": "{{Guid.CreateVersion7()}}",
                    "expectedConcurrencyStamp": "{{Guid.CreateVersion7()}}",
                    "aspectKind": "tech",
                    "managementContextHasEdit": true,
                    "skillLevel": 0
                  }
                  """),
            new(
                AiProposedActionKind.DeleteEventTechAspect,
                $$"""
                  {
                    "eventId": "{{Guid.CreateVersion7()}}",
                    "expectedConcurrencyStamp": "{{Guid.CreateVersion7()}}",
                    "aspectKind": "tech",
                    "managementContextHasEdit": true,
                    "destructiveSummary": "Delete Tech aspect.",
                    "confirmationPhrase": "DELETE_TECH_ASPECT",
                    "acknowledgedConsequences": true
                  }
                  """)
        ];
}
