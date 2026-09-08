using Explore.Application.Authorization;
using Explore.Application.Features.AiAssistant.Actions;
using Explore.Domain.Ai;

namespace Explore.Application.Features.AiAssistant.Tools;

public static class UpdateEventDraftAiToolDefinition
{
    public static AiToolDefinition Create()
        => new(
            AiProposedActionKind.UpdateEventDraft,
            "UpdateEventDraft",
            "Update event draft",
            JsonSchema,
            AllowedPayloadFields,
            ForbiddenPayloadFields,
            typeof(UpdateEventDraftAiActionMapper),
            new AiToolAuthorizationRequirement(ResourceKinds.Event, AuthorizationActions.Update),
            ExposeToProvider: false,
            ExposeToMcp: true,
            AgentMetadata: new AiToolAgentMetadata(
                new AiToolScopeMetadata(
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "/events/{eventId}",
                        "/events/detail",
                        "/events/manage",
                        "/calendar"
                    },
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "event-management",
                        "event-drafting"
                    },
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "event",
                        "event-management-context"
                    }),
                AiToolRiskClass.Medium,
                AiToolApprovalMode.HumanConfirmationRequired,
                "Available only when the current API/HAL event context exposes the edit affordance.",
                AiToolFollowUpPolicy.AskClarifyingQuestionBeforeProposal,
                "Read event management context first, use its concurrency stamp, propose a draft update only, and do not claim the event was updated before the user confirms the proposal.",
                new AiToolResultPresentationMetadata(
                    "event-draft-update-proposal-card",
                    "Review event draft update",
                    "Event draft updated",
                    "Event draft was not updated"),
                "edit"));

    public static IReadOnlySet<string> AllowedPayloadFields { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "eventId",
        "expectedConcurrencyStamp",
        "expectedParticipationConfigurationConcurrencyStamp",
        "title",
        "subtitle",
        "description",
        "content",
        "slug",
        "eventTypeId",
        "audienceGenderId",
        "audienceAgeId",
        "featuredImageId",
        "participationConfiguration",
        "visibilityTypeId",
        "eventFormatId",
        "madhabId",
        "timezone",
        "eventTimeZoneId",
        "eventUrl",
        "backgroundColor",
        "backgroundEffect",
        "backgroundImageId",
        "templateId",
        "eventSeriesId",
        "seriesOrder",
        "registrationPolicyId"
    };

    public static IReadOnlySet<string> ForbiddenPayloadFields { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "id",
        "tenantId",
        "actorId",
        "actor",
        "organizationId",
        "groupId",
        "eventStatusId",
        "status",
        "createdBy",
        "updatedBy",
        "createdAt",
        "updatedAt",
        "deletedBy",
        "deletedAt",
        "isDeleted",
        "publishedAt",
        "isPublished",
        "totalViews",
        "views",
        "viewCount",
        "sessionCount",
        "firstSessionDate",
        "lastSessionDate",
        "firstSessionStartUtc",
        "lastSessionStartUtc",
        "sessions",
        "sessionGroups",
        "days",
        "rooms",
        "agendaItems",
        "moderationRecords",
        "roleAssignments",
        "registrations",
        "sourceTemplateKey",
        "sourceTemplateVersion",
        "instantiatedFromTemplateAt",
        "lastSyncedFromTemplateAt",
        "concurrencyStamp",
        "atprotoRecordId",
        "isUserReported"
    };

    public const string JsonSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["eventId", "expectedConcurrencyStamp", "expectedParticipationConfigurationConcurrencyStamp", "title", "participationConfiguration"],
          "properties": {
            "eventId": { "type": "string", "format": "uuid" },
            "expectedConcurrencyStamp": { "type": "string", "format": "uuid" },
            "expectedParticipationConfigurationConcurrencyStamp": { "type": "string", "format": "uuid" },
            "title": { "type": "string", "maxLength": 500 },
            "subtitle": { "type": "string", "maxLength": 200 },
            "description": { "type": "string", "maxLength": 150 },
            "content": { "type": "string", "maxLength": 5000 },
            "slug": { "type": "string", "maxLength": 500 },
            "eventTypeId": { "type": "integer" },
            "audienceGenderId": { "type": "integer" },
            "audienceAgeId": { "type": "integer" },
            "featuredImageId": { "type": "string", "format": "uuid" },
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
            "timezone": { "type": "string", "maxLength": 500 },
            "eventTimeZoneId": { "type": "string", "maxLength": 500 },
            "eventUrl": { "type": "string", "maxLength": 500 },
            "backgroundColor": { "type": "string", "maxLength": 100 },
            "backgroundEffect": { "type": "string", "maxLength": 100 },
            "backgroundImageId": { "type": "string", "format": "uuid" },
            "templateId": { "type": "string", "format": "uuid" },
            "eventSeriesId": { "type": "string", "format": "uuid" },
            "seriesOrder": { "type": "integer", "minimum": 0 },
            "registrationPolicyId": { "type": "integer" }
          }
        }
        """;
}
