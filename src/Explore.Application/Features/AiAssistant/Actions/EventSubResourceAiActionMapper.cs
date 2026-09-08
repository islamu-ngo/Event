using System.Text.Json;
using Explore.Application.Features.AiAssistant.Prompting;
using Explore.Application.Features.AiAssistant.Tools;
using Explore.Domain.Ai;

namespace Explore.Application.Features.AiAssistant.Actions;

public sealed class EventSubResourceAiActionMapper
{
    private static readonly IAiToolContractRegistry Registry = new AiToolContractRegistry(
        EventSubResourceAiToolDefinitions.CreateAll());
    private static readonly IReadOnlySet<AiProposedActionKind> SupportedKinds = EventSubResourceAiToolDefinitions
        .CreateAll()
        .Select(definition => definition.Kind)
        .ToHashSet();

    public EventSubResourceAiActionMappingResult Map(AiParsedProposedAction action)
    {
        if (!SupportedKinds.Contains(action.Kind))
        {
            return EventSubResourceAiActionMappingResult.Failure(
                "unsupported_action_kind",
                "AI proposed action kind is not supported for event sub-resource mapping.");
        }

        return Map(action.Kind, action.PayloadJson);
    }

    public EventSubResourceAiActionMappingResult Map(AiProposedActionKind kind, string payloadJson)
    {
        if (!SupportedKinds.Contains(kind))
        {
            return EventSubResourceAiActionMappingResult.Failure(
                "unsupported_action_kind",
                "AI proposed action kind is not supported for event sub-resource mapping.");
        }

        var validation = Registry.ValidatePayload(kind, payloadJson);
        if (!validation.Succeeded)
        {
            return EventSubResourceAiActionMappingResult.Failure(
                validation.FailureCode ?? "invalid_tool_arguments",
                validation.FailureMessage ?? "AI event sub-resource payload failed validation.");
        }

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;
            var eventId = TryReadGuid(root, "eventId");
            var targetId = TryReadFirstGuid(
                root,
                "sessionId",
                "groupId",
                "dayId",
                "agendaItemId",
                "definitionId",
                "eventCustomPropertyDefinitionId",
                "registrationId",
                "assignmentId",
                "templateId",
                "sessionTemplateId");
            var expectedConcurrencyStamp = TryReadGuid(root, "expectedConcurrencyStamp");
            var destructive = root.TryGetProperty("acknowledgedConsequences", out var acknowledged)
                && acknowledged.ValueKind is JsonValueKind.True;

            return EventSubResourceAiActionMappingResult.Success(
                kind,
                payloadJson,
                eventId,
                targetId,
                expectedConcurrencyStamp,
                destructive);
        }
        catch (JsonException)
        {
            return EventSubResourceAiActionMappingResult.Failure(
                "invalid_payload_json",
                "AI event sub-resource payload must be valid JSON.");
        }
    }

    private static Guid? TryReadFirstGuid(JsonElement root, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            var value = TryReadGuid(root, propertyName);
            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }

    private static Guid? TryReadGuid(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.String
            || !Guid.TryParse(value.GetString(), out var parsed)
            || parsed == Guid.Empty)
        {
            return null;
        }

        return parsed;
    }
}

public sealed record EventSubResourceAiActionMappingResult(
    bool Succeeded,
    AiProposedActionKind? Kind,
    string? PayloadJson,
    Guid? EventId,
    Guid? TargetId,
    Guid? ExpectedConcurrencyStamp,
    bool Destructive,
    string? FailureCode,
    string? FailureMessage)
{
    public static EventSubResourceAiActionMappingResult Success(
        AiProposedActionKind kind,
        string payloadJson,
        Guid? eventId,
        Guid? targetId,
        Guid? expectedConcurrencyStamp,
        bool destructive)
        => new(true, kind, payloadJson, eventId, targetId, expectedConcurrencyStamp, destructive, null, null);

    public static EventSubResourceAiActionMappingResult Failure(string failureCode, string failureMessage)
        => new(false, null, null, null, null, null, false, failureCode, failureMessage);
}
