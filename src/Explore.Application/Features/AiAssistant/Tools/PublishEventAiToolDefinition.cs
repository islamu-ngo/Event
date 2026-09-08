using Explore.Application.Authorization;
using Explore.Application.Features.AiAssistant.Actions;
using Explore.Domain.Ai;

namespace Explore.Application.Features.AiAssistant.Tools;

public static class PublishEventAiToolDefinition
{
    public static AiToolDefinition Create()
        => new(
            AiProposedActionKind.PublishEvent,
            "PublishEvent",
            "Publish event",
            JsonSchema,
            AllowedPayloadFields,
            ForbiddenPayloadFields,
            typeof(PublishEventAiActionMapper),
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
                        "event-publishing"
                    },
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "event",
                        "event-management-context",
                        "event-publish-readiness"
                    }),
                AiToolRiskClass.High,
                AiToolApprovalMode.HumanConfirmationRequired,
                "Available only when the current API/HAL event context exposes the publish affordance and publish readiness is ready.",
                AiToolFollowUpPolicy.AskClarifyingQuestionBeforeProposal,
                "Read event management context and publish readiness first, use the current concurrency stamp, propose publishing only when readiness is ready, and do not claim the event was published before the user confirms the proposal.",
                new AiToolResultPresentationMetadata(
                    "event-publish-proposal-card",
                    "Review event publish proposal",
                    "Event published",
                    "Event was not published"),
                "publish"));

    public static IReadOnlySet<string> AllowedPayloadFields { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "eventId",
        "expectedConcurrencyStamp",
        "readinessIsReady",
        "readinessErrorCount",
        "readinessCheckedAtUtc",
        "readinessSummary"
    };

    public static IReadOnlySet<string> ForbiddenPayloadFields { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "id",
        "tenantId",
        "actorId",
        "actor",
        "organizationId",
        "groupId",
        "title",
        "subtitle",
        "description",
        "content",
        "slug",
        "eventStatusId",
        "status",
        "isPublished",
        "publishedAt",
        "publishedBy",
        "createdBy",
        "updatedBy",
        "createdAt",
        "updatedAt",
        "deletedBy",
        "deletedAt",
        "isDeleted",
        "outboxMessages",
        "notificationFanout",
        "sessions",
        "sessionGroups",
        "agendaItems",
        "registrations",
        "roleAssignments",
        "concurrencyStamp"
    };

    public const string JsonSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["eventId", "expectedConcurrencyStamp", "readinessIsReady", "readinessErrorCount"],
          "properties": {
            "eventId": { "type": "string", "format": "uuid" },
            "expectedConcurrencyStamp": { "type": "string", "format": "uuid" },
            "readinessIsReady": { "type": "boolean", "enum": [true] },
            "readinessErrorCount": { "type": "integer", "enum": [0] },
            "readinessCheckedAtUtc": { "type": "string", "format": "date-time" },
            "readinessSummary": { "type": "string", "maxLength": 1000 }
          }
        }
        """;
}
