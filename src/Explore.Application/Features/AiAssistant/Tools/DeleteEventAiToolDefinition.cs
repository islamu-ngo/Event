using Explore.Application.Authorization;
using Explore.Application.Features.AiAssistant.Actions;
using Explore.Domain.Ai;

namespace Explore.Application.Features.AiAssistant.Tools;

public static class DeleteEventAiToolDefinition
{
    public static AiToolDefinition Create()
        => new(
            AiProposedActionKind.DeleteEvent,
            "DeleteEvent",
            "Delete event",
            JsonSchema,
            AllowedPayloadFields,
            ForbiddenPayloadFields,
            typeof(DeleteEventAiActionMapper),
            new AiToolAuthorizationRequirement(ResourceKinds.Event, AuthorizationActions.Delete),
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
                        "event-deletion"
                    },
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "event",
                        "event-management-context"
                    }),
                AiToolRiskClass.High,
                AiToolApprovalMode.HumanConfirmationRequired,
                "Available only when the current API/HAL event context exposes the delete affordance.",
                AiToolFollowUpPolicy.ShowWarningsBeforeConfirmation,
                "Read event management context first, use its concurrency stamp, require explicit destructive confirmation metadata, and do not claim the event was deleted before the user confirms the proposal.",
                new AiToolResultPresentationMetadata(
                    "event-delete-proposal-card",
                    "Review event deletion proposal",
                    "Event deleted",
                    "Event was not deleted"),
                RequiredHalLinkRel: "delete",
                DestructiveHint: true));

    public static IReadOnlySet<string> AllowedPayloadFields { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "eventId",
        "expectedConcurrencyStamp",
        "managementContextHasDelete",
        "destructiveSummary",
        "confirmationPhrase",
        "acknowledgedConsequences"
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
          "required": ["eventId", "expectedConcurrencyStamp", "managementContextHasDelete", "destructiveSummary", "confirmationPhrase", "acknowledgedConsequences"],
          "properties": {
            "eventId": { "type": "string", "format": "uuid" },
            "expectedConcurrencyStamp": { "type": "string", "format": "uuid" },
            "managementContextHasDelete": { "type": "boolean", "enum": [true] },
            "destructiveSummary": { "type": "string", "maxLength": 1000 },
            "confirmationPhrase": { "type": "string", "enum": ["DELETE_EVENT"] },
            "acknowledgedConsequences": { "type": "boolean", "enum": [true] }
          }
        }
        """;
}
