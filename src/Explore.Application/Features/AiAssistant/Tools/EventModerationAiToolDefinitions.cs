using Explore.Application.Authorization;
using Explore.Application.Features.AiAssistant.Actions;
using Explore.Application.Hateoas;
using Explore.Domain.Ai;

namespace Explore.Application.Features.AiAssistant.Tools;

public static class EventModerationAiToolDefinitions
{
    private const string Uuid = """{ "type": "string", "format": "uuid" }""";
    private const string ReasonCode = """{ "type": "string", "maxLength": 128 }""";
    private const string NullableCorrelationId = """{ "type": ["string", "null"], "maxLength": 128 }""";
    private const string TrueBoolean = """{ "type": "boolean", "enum": [true] }""";
    private const string DestructiveSummary = """{ "type": "string", "maxLength": 1000 }""";

    private static readonly IReadOnlyDictionary<string, string> LightModerationFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["eventId"] = Uuid,
        ["expectedConcurrencyStamp"] = Uuid,
        ["managementContextHasModerateLight"] = TrueBoolean,
        ["reasonCode"] = ReasonCode,
        ["correlationId"] = NullableCorrelationId
    };

    private static readonly IReadOnlyDictionary<string, string> HeavyModerationFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["eventId"] = Uuid,
        ["expectedConcurrencyStamp"] = Uuid,
        ["managementContextHasModerateHeavy"] = TrueBoolean,
        ["reasonCode"] = ReasonCode,
        ["correlationId"] = NullableCorrelationId,
        ["destructiveSummary"] = DestructiveSummary,
        ["confirmationPhrase"] = $$"""{ "type": "string", "enum": ["{{EventModerationAiActionMapper.HeavyModerationConfirmationPhrase}}"] }""",
        ["acknowledgedConsequences"] = TrueBoolean
    };

    private static readonly IReadOnlyDictionary<string, string> UnmoderationFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["eventId"] = Uuid,
        ["expectedConcurrencyStamp"] = Uuid,
        ["managementContextHasUnmoderate"] = TrueBoolean,
        ["reasonCode"] = ReasonCode,
        ["correlationId"] = NullableCorrelationId
    };

    private static readonly IReadOnlySet<string> ForbiddenPayloadFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
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
        "moderationRecords",
        "outboxMessages",
        "notificationFanout",
        "sessions",
        "sessionGroups",
        "agendaItems",
        "registrations",
        "roleAssignments",
        "concurrencyStamp"
    };

    public static IReadOnlyList<AiToolDefinition> CreateAll()
        =>
        [
            Definition(
                AiProposedActionKind.LightModerateEvent,
                "LightModerateEvent",
                "Light moderate event",
                AuthorizationActions.Events.ModerateLight,
                LightModerationFields,
                ["eventId", "expectedConcurrencyStamp", "managementContextHasModerateLight", "reasonCode"],
                LinkRelations.ModerateLight,
                "event-moderation-light-proposal-card",
                destructive: false),
            Definition(
                AiProposedActionKind.HeavyModerateEvent,
                "HeavyModerateEvent",
                "Heavy moderate event",
                AuthorizationActions.Events.ModerateHeavy,
                HeavyModerationFields,
                ["eventId", "expectedConcurrencyStamp", "managementContextHasModerateHeavy", "reasonCode", "destructiveSummary", "confirmationPhrase", "acknowledgedConsequences"],
                LinkRelations.ModerateHeavy,
                "event-moderation-heavy-proposal-card",
                destructive: true),
            Definition(
                AiProposedActionKind.UnmoderateEvent,
                "UnmoderateEvent",
                "Unmoderate event",
                AuthorizationActions.Events.Unmoderate,
                UnmoderationFields,
                ["eventId", "expectedConcurrencyStamp", "managementContextHasUnmoderate", "reasonCode"],
                LinkRelations.Unmoderate,
                "event-unmoderation-proposal-card",
                destructive: false)
        ];

    private static AiToolDefinition Definition(
        AiProposedActionKind kind,
        string name,
        string displayName,
        string authorizationAction,
        IReadOnlyDictionary<string, string> fields,
        IReadOnlyList<string> requiredFields,
        string requiredHalLinkRel,
        string presentationCard,
        bool destructive)
        => new(
            kind,
            name,
            displayName,
            BuildSchema(requiredFields, fields),
            fields.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase),
            ForbiddenPayloadFields,
            typeof(EventModerationAiActionMapper),
            new AiToolAuthorizationRequirement(ResourceKinds.Event, authorizationAction),
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
                        "event-moderation"
                    },
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "event",
                        "event-management-context",
                        "event-moderation-context"
                    }),
                destructive ? AiToolRiskClass.Critical : AiToolRiskClass.High,
                AiToolApprovalMode.HumanConfirmationRequired,
                "Available only when the current API/HAL event management context exposes the required moderation affordance.",
                destructive ? AiToolFollowUpPolicy.ShowWarningsBeforeConfirmation : AiToolFollowUpPolicy.AskClarifyingQuestionBeforeProposal,
                $"Read event management context first, use the current concurrency stamp, require the matching moderation HAL affordance, propose {displayName.ToLowerInvariant()} only, and do not claim moderation happened before confirmation.",
                new AiToolResultPresentationMetadata(
                    presentationCard,
                    $"Review {displayName.ToLowerInvariant()} proposal",
                    $"{displayName} confirmed",
                    $"{displayName} was not applied"),
                requiredHalLinkRel,
                destructive));

    private static string BuildSchema(IReadOnlyList<string> requiredFields, IReadOnlyDictionary<string, string> fields)
    {
        var requiredJson = string.Join(", ", requiredFields.Select(field => $"\"{field}\""));
        var propertiesJson = string.Join(
            ",\n",
            fields.Select(field => $"            \"{field.Key}\": {field.Value}"));

        return $$"""
            {
              "type": "object",
              "additionalProperties": false,
              "required": [{{requiredJson}}],
              "properties": {
                {{propertiesJson}}
              }
            }
            """;
    }
}
