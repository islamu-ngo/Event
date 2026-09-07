using Explore.Application.Authorization;
using Explore.Application.Features.AiAssistant.Actions;
using Explore.Domain.Ai;

namespace Explore.Application.Features.AiAssistant.Tools;

public static class CreateEventDraftAiToolDefinition
{
    public static AiToolDefinition Create()
        => new(
            AiProposedActionKind.CreateEventDraft,
            "CreateEventDraft",
            "Create event draft",
            JsonSchema,
            AllowedPayloadFields,
            ForbiddenPayloadFields,
            typeof(CreateEventDraftAiActionMapper),
            new AiToolAuthorizationRequirement(ResourceKinds.Event, AuthorizationActions.Create),
            AgentMetadata: new AiToolAgentMetadata(
                new AiToolScopeMetadata(
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "/events",
                        "/events/new",
                        "/calendar"
                    },
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "event-drafting",
                        "event-planning"
                    },
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "event",
                        "selected-references"
                    }),
                AiToolRiskClass.Medium,
                AiToolApprovalMode.HumanConfirmationRequired,
                "Available only when AI tool proposals are enabled and the current API/HAL context allows event creation.",
                AiToolFollowUpPolicy.AskClarifyingQuestionBeforeProposal,
                "Create a draft proposal only. Put poster-derived date, time, location, gender mode, and primary-session speaker actor references in structured fields instead of prose; keep description as a short summary. The initial event draft may include at most one primary session because event creation creates the first draft session by convention. Use the dedicated event-session draft workflow only after an event exists and the source clearly contains additional sessions. Do not publish, invite attendees, assign roles, or claim the event exists before the user confirms the proposal.",
                new AiToolResultPresentationMetadata(
                    "event-draft-proposal-card",
                    "Review event draft proposal",
                    "Event draft created",
                    "Event draft was not created"),
                "create-event"));

    public static IReadOnlySet<string> AllowedPayloadFields { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "title",
        "subtitle",
        "description",
        "content",
        "slug",
        "eventTypeId",
        "audienceGenderId",
        "audienceAgeId",
        "organizationId",
        "groupId",
        "participationConfiguration",
        "visibilityTypeId",
        "eventFormatId",
        "madhabId",
        "islamicAspect",
        "timezone",
        "eventTimeZoneId",
        "eventUrl",
        "categoryIds",
        "tagIds",
        "location",
        "room",
        "session"
    };

    public static IReadOnlySet<string> ForbiddenPayloadFields { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "tenantId",
        "eventStatusId",
        "createdBy",
        "updatedBy",
        "publishedAt",
        "isPublished",
        "firstSessionDate",
        "lastSessionDate",
        "firstSessionStartUtc",
        "lastSessionStartUtc",
        "sessionCount",
        "actorId",
        "locations",
        "days",
        "rooms",
        "sessions",
        "agendaItems"
    };

    public const string JsonSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["title", "participationConfiguration"],
          "properties": {
            "title": { "type": "string", "maxLength": 200 },
            "subtitle": { "type": "string", "maxLength": 200 },
            "description": { "type": "string", "maxLength": 150 },
            "content": { "type": "string", "maxLength": 5000 },
            "slug": { "type": "string", "maxLength": 500 },
            "eventTypeId": { "type": "integer" },
            "audienceGenderId": { "type": "integer" },
            "audienceAgeId": { "type": "integer" },
            "organizationId": { "type": "string", "format": "uuid" },
            "groupId": { "type": "string", "format": "uuid" },
            "participationConfiguration": {
              "type": "object",
              "additionalProperties": false,
              "required": ["participationHandlingModeId", "advanceRegistrationObligationId"],
              "properties": {
                "participationHandlingModeId": { "type": "integer", "minimum": 1 },
                "advanceRegistrationObligationId": { "type": "integer", "minimum": 1 },
                "identityAccessModeId": { "type": "integer", "minimum": 1 },
                "guestRecoveryPolicy": { "type": "integer", "enum": [1, 2, 3, 4, 5] }
              }
            },
            "visibilityTypeId": { "type": "integer" },
            "eventFormatId": { "type": "integer" },
            "madhabId": { "type": "integer" },
            "islamicAspect": {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "madhabId": { "type": "integer" },
                "referencePrayer": { "type": "integer", "enum": [1, 2, 3, 4, 5, 6] },
                "prayerTimeOffset": { "type": "integer" },
                "genderMode": { "type": "integer", "enum": [0, 1, 2, 3, 4] },
                "includesQuranRecitation": { "type": "boolean" },
                "primaryLanguageId": { "type": "integer" }
              }
            },
            "timezone": { "type": "string", "maxLength": 100 },
            "eventTimeZoneId": { "type": "string", "maxLength": 100 },
            "eventUrl": { "type": "string", "maxLength": 500 },
            "categoryIds": { "type": "array", "items": { "type": "string", "format": "uuid" } },
            "tagIds": { "type": "array", "items": { "type": "string", "format": "uuid" } },
            "location": {
              "type": "object",
              "additionalProperties": false,
              "required": ["fullName", "address", "postcode", "country", "city"],
              "properties": {
                "fullName": { "type": "string", "maxLength": 500 },
                "address": { "type": "string", "maxLength": 500 },
                "postcode": { "type": "string", "maxLength": 500 },
                "country": { "type": "string", "maxLength": 500 },
                "city": { "type": "string", "maxLength": 500 },
                "timezone": { "type": "string", "maxLength": 500 }
              }
            },
            "room": {
              "type": "object",
              "additionalProperties": false,
              "required": ["name"],
              "properties": {
                "name": { "type": "string", "maxLength": 200 },
                "slug": { "type": "string", "maxLength": 500 },
                "description": { "type": "string", "maxLength": 2000 },
                "capacity": { "type": "integer" }
              }
            },
            "session": {
              "type": "object",
              "additionalProperties": false,
              "required": ["startTime", "endTime"],
              "properties": {
                "startTime": { "type": "string", "format": "date-time" },
                "endTime": { "type": "string", "format": "date-time" },
                "title": { "type": "string", "maxLength": 500 },
                "eventSessionKindId": { "type": "integer" },
                "description": { "type": "string", "maxLength": 5000 },
                "slug": { "type": "string", "maxLength": 500 },
                "maxAudienceAttendees": { "type": "integer" },
                "registrationModeId": { "type": "integer" },
                "islamicAspect": {
                  "type": "object",
                  "additionalProperties": false,
                  "properties": {
                    "startTimeType": { "type": "integer", "enum": [0, 1] },
                    "referencePrayer": { "type": "integer", "enum": [1, 2, 3, 4, 5, 6] },
                    "offsetMinutes": { "type": "integer" },
                    "requiresWudu": { "type": "boolean" },
                    "ritualRequirementsJson": { "type": "string", "maxLength": 2000 }
                  }
                },
                "languageIds": { "type": "array", "items": { "type": "integer" } },
                "speakerActorIds": { "type": "array", "items": { "type": "string", "format": "uuid" } }
              }
            }
          }
        }
        """;
}
