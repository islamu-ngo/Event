using Explore.Application.Authorization;
using Explore.Application.Features.AiAssistant.Actions;
using Explore.Domain.Ai;

namespace Explore.Application.Features.AiAssistant.Tools;

public static class UpsertEventIslamicAspectAiToolDefinition
{
    public static AiToolDefinition Create()
        => new(
            AiProposedActionKind.UpsertEventIslamicAspect,
            "UpsertEventIslamicAspect",
            "Upsert event Islamic aspect",
            JsonSchema,
            AllowedPayloadFields,
            ForbiddenPayloadFields,
            typeof(UpsertEventIslamicAspectAiActionMapper),
            new AiToolAuthorizationRequirement(ResourceKinds.Event, AuthorizationActions.Update),
            ExposeToProvider: false,
            ExposeToMcp: true,
            AgentMetadata: CreateMetadata());

    public static IReadOnlySet<string> AllowedPayloadFields { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "eventId",
        "expectedConcurrencyStamp",
        "aspectKind",
        "managementContextHasEdit",
        "madhabId",
        "referencePrayer",
        "prayerTimeOffset",
        "genderMode",
        "includesQuranRecitation",
        "primaryLanguageId"
    };

    public static IReadOnlySet<string> ForbiddenPayloadFields { get; } = EventAspectToolFieldPolicy.ForbiddenPayloadFields;

    public const string JsonSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["eventId", "expectedConcurrencyStamp", "aspectKind", "managementContextHasEdit", "genderMode"],
          "properties": {
            "eventId": { "type": "string", "format": "uuid" },
            "expectedConcurrencyStamp": { "type": "string", "format": "uuid" },
            "aspectKind": { "type": "string", "enum": ["islamic"] },
            "managementContextHasEdit": { "type": "boolean", "enum": [true] },
            "madhabId": { "type": "integer" },
            "referencePrayer": { "type": "integer", "enum": [1, 2, 3, 4, 5, 6] },
            "prayerTimeOffset": { "type": "integer" },
            "genderMode": { "type": "integer", "enum": [0, 1, 2, 3, 4] },
            "includesQuranRecitation": { "type": "boolean" },
            "primaryLanguageId": { "type": "integer" }
          }
        }
        """;

    private static AiToolAgentMetadata CreateMetadata()
        => new(
            EventAspectToolFieldPolicy.ScopeMetadata,
            AiToolRiskClass.Medium,
            AiToolApprovalMode.HumanConfirmationRequired,
            "Available only when the current API/HAL event context exposes the edit affordance for event aspect management.",
            AiToolFollowUpPolicy.AskClarifyingQuestionBeforeProposal,
            "Read event management context first, use its concurrency stamp, include the Islamic aspect module context, and do not claim the aspect was changed before the user confirms the proposal.",
            new AiToolResultPresentationMetadata(
                "event-islamic-aspect-upsert-proposal-card",
                "Review Islamic aspect proposal",
                "Islamic aspect saved",
                "Islamic aspect was not saved"),
            RequiredHalLinkRel: "edit");
}
