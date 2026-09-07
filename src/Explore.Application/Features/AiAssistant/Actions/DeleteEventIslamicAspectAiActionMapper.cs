using System.Text.Json;
using Explore.Application.Features.AiAssistant.Prompting;
using Explore.Application.Features.AiAssistant.Tools;
using Explore.Application.Features.EventAspects.Requests.Commands;
using Explore.Domain.Ai;

namespace Explore.Application.Features.AiAssistant.Actions;

public sealed class DeleteEventIslamicAspectAiActionMapper
{
    public DeleteEventIslamicAspectAiActionMappingResult Map(AiParsedProposedAction action)
    {
        if (action.Kind != AiProposedActionKind.DeleteEventIslamicAspect)
        {
            return DeleteEventIslamicAspectAiActionMappingResult.Failure(
                "unsupported_action_kind",
                "AI proposed action kind is not supported for Islamic aspect deletion mapping.");
        }

        return Map(action.PayloadJson);
    }

    public DeleteEventIslamicAspectAiActionMappingResult Map(string payloadJson)
    {
        var commonResult = EventAspectDeletionPayloadMapper.Map(
            payloadJson,
            DeleteEventIslamicAspectAiToolDefinition.AllowedPayloadFields,
            "islamic",
            "DELETE_ISLAMIC_ASPECT",
            "AI Islamic aspect deletion payload");

        if (!commonResult.Succeeded)
        {
            return DeleteEventIslamicAspectAiActionMappingResult.Failure(
                commonResult.FailureCode!,
                commonResult.FailureMessage!);
        }

        var command = new DeleteEventIslamicAspectCommand { EventId = commonResult.EventId!.Value };
        return DeleteEventIslamicAspectAiActionMappingResult.Success(
            commonResult.EventId.Value,
            command,
            commonResult.DestructiveContext!);
    }
}

public sealed record DeleteEventIslamicAspectAiActionMappingResult(
    bool Succeeded,
    Guid? EventId,
    DeleteEventIslamicAspectCommand? Command,
    EventAspectAiDestructiveContext? DestructiveContext,
    string? FailureCode,
    string? FailureMessage)
{
    public static DeleteEventIslamicAspectAiActionMappingResult Success(
        Guid eventId,
        DeleteEventIslamicAspectCommand command,
        EventAspectAiDestructiveContext destructiveContext)
        => new(true, eventId, command, destructiveContext, null, null);

    public static DeleteEventIslamicAspectAiActionMappingResult Failure(string failureCode, string failureMessage)
        => new(false, null, null, null, failureCode, failureMessage);
}

internal static class EventAspectDeletionPayloadMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static EventAspectDeletionMappingResult Map(
        string payloadJson,
        IReadOnlySet<string> allowedPayloadFields,
        string requiredAspectKind,
        string requiredConfirmationPhrase,
        string payloadName)
    {
        DeleteEventAspectAiActionPayload? payload;
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return EventAspectDeletionMappingResult.Failure(
                    "invalid_payload_json",
                    $"{payloadName} must be a JSON object.");
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!allowedPayloadFields.Contains(property.Name))
                {
                    return EventAspectDeletionMappingResult.Failure(
                        "unsupported_payload_field",
                        $"{payloadName} contains a field that is not allowed.");
                }
            }

            payload = document.RootElement.Deserialize<DeleteEventAspectAiActionPayload>(JsonOptions);
        }
        catch (JsonException)
        {
            return EventAspectDeletionMappingResult.Failure(
                "invalid_payload_json",
                $"{payloadName} must be valid JSON.");
        }

        if (payload is null)
        {
            return EventAspectDeletionMappingResult.Failure(
                "invalid_payload_json",
                $"{payloadName} could not be read.");
        }

        if (payload.EventId is not { } eventId || eventId == Guid.Empty)
        {
            return EventAspectDeletionMappingResult.Failure(
                "missing_event_id",
                $"{payloadName} must include the event id.");
        }

        if (payload.ExpectedConcurrencyStamp is not { } expectedConcurrencyStamp || expectedConcurrencyStamp == Guid.Empty)
        {
            return EventAspectDeletionMappingResult.Failure(
                "missing_expected_concurrency_stamp",
                $"{payloadName} must include the expected concurrency stamp.");
        }

        if (!string.Equals(payload.AspectKind, requiredAspectKind, StringComparison.Ordinal))
        {
            return EventAspectDeletionMappingResult.Failure(
                "invalid_aspect_kind",
                $"{payloadName} must include the expected aspect module context.");
        }

        if (payload.ManagementContextHasEdit is not true)
        {
            return EventAspectDeletionMappingResult.Failure(
                "missing_edit_affordance_context",
                $"{payloadName} must confirm the current management context exposes edit.");
        }

        var destructiveSummary = Normalize(payload.DestructiveSummary);
        if (string.IsNullOrWhiteSpace(destructiveSummary))
        {
            return EventAspectDeletionMappingResult.Failure(
                "missing_destructive_summary",
                $"{payloadName} must include a destructive summary.");
        }

        if (destructiveSummary.Length > 1_000)
        {
            return EventAspectDeletionMappingResult.Failure(
                "field_too_long",
                $"{payloadName} destructive summary exceeds the allowed length.");
        }

        if (!string.Equals(payload.ConfirmationPhrase, requiredConfirmationPhrase, StringComparison.Ordinal))
        {
            return EventAspectDeletionMappingResult.Failure(
                "missing_destructive_confirmation",
                $"{payloadName} must include the exact destructive confirmation phrase.");
        }

        if (payload.AcknowledgedConsequences is not true)
        {
            return EventAspectDeletionMappingResult.Failure(
                "missing_destructive_acknowledgement",
                $"{payloadName} must acknowledge the destructive consequences.");
        }

        return EventAspectDeletionMappingResult.Success(
            eventId,
            new EventAspectAiDestructiveContext(
                expectedConcurrencyStamp,
                requiredAspectKind,
                ManagementContextHasEdit: true,
                destructiveSummary,
                requiredConfirmationPhrase,
                AcknowledgedConsequences: true));
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed record EventAspectDeletionMappingResult(
    bool Succeeded,
    Guid? EventId,
    EventAspectAiDestructiveContext? DestructiveContext,
    string? FailureCode,
    string? FailureMessage)
{
    public static EventAspectDeletionMappingResult Success(
        Guid eventId,
        EventAspectAiDestructiveContext destructiveContext)
        => new(true, eventId, destructiveContext, null, null);

    public static EventAspectDeletionMappingResult Failure(string failureCode, string failureMessage)
        => new(false, null, null, failureCode, failureMessage);
}
