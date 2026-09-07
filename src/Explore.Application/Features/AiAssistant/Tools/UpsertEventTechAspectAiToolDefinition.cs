using Explore.Application.Authorization;
using Explore.Application.Features.AiAssistant.Actions;
using Explore.Domain.Ai;

namespace Explore.Application.Features.AiAssistant.Tools;

public static class UpsertEventTechAspectAiToolDefinition
{
    public static AiToolDefinition Create()
        => new(
            AiProposedActionKind.UpsertEventTechAspect,
            "UpsertEventTechAspect",
            "Upsert event Tech aspect",
            JsonSchema,
            AllowedPayloadFields,
            ForbiddenPayloadFields,
            typeof(UpsertEventTechAspectAiActionMapper),
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
        "githubRepoUrl",
        "hackathonTrack",
        "skillLevel",
        "techStackTags",
        "requiresLaptop",
        "isCodingCompetition",
        "maxTeamSize",
        "prizePool",
        "prizeCurrencyCode"
    };

    public static IReadOnlySet<string> ForbiddenPayloadFields { get; } = EventAspectToolFieldPolicy.ForbiddenPayloadFields;

    public const string JsonSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["eventId", "expectedConcurrencyStamp", "aspectKind", "managementContextHasEdit", "skillLevel"],
          "properties": {
            "eventId": { "type": "string", "format": "uuid" },
            "expectedConcurrencyStamp": { "type": "string", "format": "uuid" },
            "aspectKind": { "type": "string", "enum": ["tech"] },
            "managementContextHasEdit": { "type": "boolean", "enum": [true] },
            "githubRepoUrl": { "type": "string", "maxLength": 500 },
            "hackathonTrack": { "type": "string", "maxLength": 200 },
            "skillLevel": { "type": "integer", "enum": [0, 1, 2, 3] },
            "techStackTags": { "type": "string", "maxLength": 1000 },
            "requiresLaptop": { "type": "boolean" },
            "isCodingCompetition": { "type": "boolean" },
            "maxTeamSize": { "type": "integer" },
            "prizePool": { "type": "number", "minimum": 0 },
            "prizeCurrencyCode": { "type": "string", "maxLength": 3 }
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
            "Read event management context first, use its concurrency stamp, include the Tech aspect module context, and do not claim the aspect was changed before the user confirms the proposal.",
            new AiToolResultPresentationMetadata(
                "event-tech-aspect-upsert-proposal-card",
                "Review Tech aspect proposal",
                "Tech aspect saved",
                "Tech aspect was not saved"),
            RequiredHalLinkRel: "edit");
}
