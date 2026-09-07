using System.Text.Json;
using Explore.Application.Features.AiAssistant.Prompting;
using Explore.Application.Features.AiAssistant.Tools;
using Explore.Domain.Ai;

namespace Explore.Application.Features.AiAssistant.Actions;

public sealed class EventModerationAiActionMapper
{
    public const string HeavyModerationConfirmationPhrase = "HEAVY_MODERATE_EVENT";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly IAiToolContractRegistry Registry = new AiToolContractRegistry(
        EventModerationAiToolDefinitions.CreateAll());

    private static readonly IReadOnlySet<AiProposedActionKind> SupportedKinds = EventModerationAiToolDefinitions
        .CreateAll()
        .Select(definition => definition.Kind)
        .ToHashSet();

    public EventModerationAiActionMappingResult Map(AiParsedProposedAction action)
    {
        if (!SupportedKinds.Contains(action.Kind))
        {
            return EventModerationAiActionMappingResult.Failure(
                "unsupported_action_kind",
                "AI proposed action kind is not supported for event moderation mapping.");
        }

        return Map(action.Kind, action.PayloadJson);
    }

    public EventModerationAiActionMappingResult Map(AiProposedActionKind kind, string payloadJson)
    {
        if (!SupportedKinds.Contains(kind))
        {
            return EventModerationAiActionMappingResult.Failure(
                "unsupported_action_kind",
                "AI proposed action kind is not supported for event moderation mapping.");
        }

        var validation = Registry.ValidatePayload(kind, payloadJson);
        if (!validation.Succeeded)
        {
            return EventModerationAiActionMappingResult.Failure(
                validation.FailureCode ?? "invalid_tool_arguments",
                validation.FailureMessage ?? "AI event moderation payload failed validation.");
        }

        EventModerationAiActionPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<EventModerationAiActionPayload>(payloadJson, JsonOptions);
        }
        catch (JsonException)
        {
            return EventModerationAiActionMappingResult.Failure(
                "invalid_payload_json",
                "AI event moderation payload must be valid JSON.");
        }

        if (payload is null)
        {
            return EventModerationAiActionMappingResult.Failure(
                "invalid_payload_json",
                "AI event moderation payload could not be read.");
        }

        if (payload.EventId is not { } eventId || eventId == Guid.Empty)
        {
            return EventModerationAiActionMappingResult.Failure(
                "missing_event_id",
                "AI event moderation payload must include the event id.");
        }

        if (payload.ExpectedConcurrencyStamp is not { } expectedConcurrencyStamp || expectedConcurrencyStamp == Guid.Empty)
        {
            return EventModerationAiActionMappingResult.Failure(
                "missing_expected_concurrency_stamp",
                "AI event moderation payload must include the expected concurrency stamp.");
        }

        var requiredHalFailure = ValidateRequiredHalEvidence(kind, payload);
        if (requiredHalFailure is not null)
        {
            return requiredHalFailure;
        }

        var reasonCode = Normalize(payload.ReasonCode);
        if (string.IsNullOrWhiteSpace(reasonCode))
        {
            return EventModerationAiActionMappingResult.Failure(
                "missing_reason_code",
                "AI event moderation payload must include a moderation reason code.");
        }

        if (reasonCode.Length > 128)
        {
            return EventModerationAiActionMappingResult.Failure(
                "field_too_long",
                "AI event moderation reason code exceeds the allowed length.");
        }

        var correlationId = Normalize(payload.CorrelationId);
        if (correlationId?.Length > 128)
        {
            return EventModerationAiActionMappingResult.Failure(
                "field_too_long",
                "AI event moderation correlation id exceeds the allowed length.");
        }

        var heavyContext = kind == AiProposedActionKind.HeavyModerateEvent
            ? ValidateHeavyModerationContext(payload)
            : EventModerationHeavyContext.NotRequired;
        if (!heavyContext.Succeeded)
        {
            return EventModerationAiActionMappingResult.Failure(
                heavyContext.FailureCode!,
                heavyContext.FailureMessage!);
        }

        return EventModerationAiActionMappingResult.Success(
            kind,
            eventId,
            expectedConcurrencyStamp,
            reasonCode,
            correlationId,
            heavyContext.DestructiveSummary,
            kind == AiProposedActionKind.HeavyModerateEvent);
    }

    private static EventModerationAiActionMappingResult? ValidateRequiredHalEvidence(
        AiProposedActionKind kind,
        EventModerationAiActionPayload payload)
        => kind switch
        {
            AiProposedActionKind.LightModerateEvent when payload.ManagementContextHasModerateLight is not true =>
                EventModerationAiActionMappingResult.Failure(
                    "missing_moderate_light_affordance_context",
                    "AI event light moderation payload must confirm the current management context exposes moderate-light."),
            AiProposedActionKind.HeavyModerateEvent when payload.ManagementContextHasModerateHeavy is not true =>
                EventModerationAiActionMappingResult.Failure(
                    "missing_moderate_heavy_affordance_context",
                    "AI event heavy moderation payload must confirm the current management context exposes moderate-heavy."),
            AiProposedActionKind.UnmoderateEvent when payload.ManagementContextHasUnmoderate is not true =>
                EventModerationAiActionMappingResult.Failure(
                    "missing_unmoderate_affordance_context",
                    "AI event unmoderation payload must confirm the current management context exposes unmoderate."),
            _ => null
        };

    private static EventModerationHeavyContext ValidateHeavyModerationContext(EventModerationAiActionPayload payload)
    {
        var destructiveSummary = Normalize(payload.DestructiveSummary);
        if (string.IsNullOrWhiteSpace(destructiveSummary))
        {
            return EventModerationHeavyContext.Failure(
                "missing_destructive_summary",
                "AI event heavy moderation payload must include a destructive summary.");
        }

        if (destructiveSummary.Length > 1_000)
        {
            return EventModerationHeavyContext.Failure(
                "field_too_long",
                "AI event heavy moderation destructive summary exceeds the allowed length.");
        }

        if (!string.Equals(payload.ConfirmationPhrase, HeavyModerationConfirmationPhrase, StringComparison.Ordinal))
        {
            return EventModerationHeavyContext.Failure(
                "missing_destructive_confirmation",
                "AI event heavy moderation payload must include the exact destructive confirmation phrase.");
        }

        if (payload.AcknowledgedConsequences is not true)
        {
            return EventModerationHeavyContext.Failure(
                "missing_destructive_acknowledgement",
                "AI event heavy moderation payload must acknowledge irreversible consequences.");
        }

        return EventModerationHeavyContext.Success(destructiveSummary);
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record EventModerationAiActionMappingResult(
    bool Succeeded,
    AiProposedActionKind? Kind,
    Guid? EventId,
    Guid? ExpectedConcurrencyStamp,
    string? ReasonCode,
    string? CorrelationId,
    string? DestructiveSummary,
    bool Destructive,
    string? FailureCode,
    string? FailureMessage)
{
    public static EventModerationAiActionMappingResult Success(
        AiProposedActionKind kind,
        Guid eventId,
        Guid expectedConcurrencyStamp,
        string reasonCode,
        string? correlationId,
        string? destructiveSummary,
        bool destructive)
        => new(
            true,
            kind,
            eventId,
            expectedConcurrencyStamp,
            reasonCode,
            correlationId,
            destructiveSummary,
            destructive,
            null,
            null);

    public static EventModerationAiActionMappingResult Failure(string failureCode, string failureMessage)
        => new(false, null, null, null, null, null, null, false, failureCode, failureMessage);
}

sealed record EventModerationHeavyContext(
    bool Succeeded,
    string? DestructiveSummary,
    string? FailureCode,
    string? FailureMessage)
{
    public static EventModerationHeavyContext NotRequired { get; } = new(true, null, null, null);

    public static EventModerationHeavyContext Success(string destructiveSummary)
        => new(true, destructiveSummary, null, null);

    public static EventModerationHeavyContext Failure(string failureCode, string failureMessage)
        => new(false, null, failureCode, failureMessage);
}
