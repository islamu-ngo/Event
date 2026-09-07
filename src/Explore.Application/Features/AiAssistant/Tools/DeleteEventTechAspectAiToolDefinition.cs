using Explore.Application.Authorization;
using Explore.Application.Features.AiAssistant.Actions;
using Explore.Domain.Ai;

namespace Explore.Application.Features.AiAssistant.Tools;

public static class DeleteEventTechAspectAiToolDefinition
{
    public static AiToolDefinition Create()
        => new(
            AiProposedActionKind.DeleteEventTechAspect,
            "DeleteEventTechAspect",
            "Delete event Tech aspect",
            JsonSchema,
            AllowedPayloadFields,
            ForbiddenPayloadFields,
            typeof(DeleteEventTechAspectAiActionMapper),
            new AiToolAuthorizationRequirement(ResourceKinds.Event, AuthorizationActions.Update),
            ExposeToProvider: false,
            ExposeToMcp: true,
            AgentMetadata: CreateMetadata());

    public static IReadOnlySet<string> AllowedPayloadFields { get; } = EventAspectToolFieldPolicy.DestructiveAspectPayloadFields;

    public static IReadOnlySet<string> ForbiddenPayloadFields { get; } = EventAspectToolFieldPolicy.ForbiddenPayloadFields;

    public const string JsonSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["eventId", "expectedConcurrencyStamp", "aspectKind", "managementContextHasEdit", "destructiveSummary", "confirmationPhrase", "acknowledgedConsequences"],
          "properties": {
            "eventId": { "type": "string", "format": "uuid" },
            "expectedConcurrencyStamp": { "type": "string", "format": "uuid" },
            "aspectKind": { "type": "string", "enum": ["tech"] },
            "managementContextHasEdit": { "type": "boolean", "enum": [true] },
            "destructiveSummary": { "type": "string", "maxLength": 1000 },
            "confirmationPhrase": { "type": "string", "enum": ["DELETE_TECH_ASPECT"] },
            "acknowledgedConsequences": { "type": "boolean", "enum": [true] }
          }
        }
        """;

    private static AiToolAgentMetadata CreateMetadata()
        => new(
            EventAspectToolFieldPolicy.ScopeMetadata,
            AiToolRiskClass.High,
            AiToolApprovalMode.HumanConfirmationRequired,
            "Available only when the current API/HAL event context exposes the edit affordance for event aspect management.",
            AiToolFollowUpPolicy.ShowWarningsBeforeConfirmation,
            "Read event management context first, use its concurrency stamp, require explicit destructive confirmation metadata, and do not claim the Tech aspect was deleted before the user confirms the proposal.",
            new AiToolResultPresentationMetadata(
                "event-tech-aspect-delete-proposal-card",
                "Review Tech aspect deletion proposal",
                "Tech aspect deleted",
                "Tech aspect was not deleted"),
            RequiredHalLinkRel: "edit",
            DestructiveHint: true);
}
