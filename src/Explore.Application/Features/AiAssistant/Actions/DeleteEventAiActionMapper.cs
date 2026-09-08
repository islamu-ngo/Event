using System.Text.Json;
using Explore.Application.Features.AiAssistant.Prompting;
using Explore.Application.Features.AiAssistant.Tools;
using Explore.Domain.Ai;

namespace Explore.Application.Features.AiAssistant.Actions;

public sealed class DeleteEventAiActionMapper
{
    private const string RequiredConfirmationPhrase = "DELETE_EVENT";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public DeleteEventAiActionMappingResult Map(AiParsedProposedAction action)
    {
        if (action.Kind != AiProposedActionKind.DeleteEvent)
        {
            return DeleteEventAiActionMappingResult.Failure(
                "unsupported_action_kind",
                "AI proposed action kind is not supported for event deletion mapping.");
        }

        return Map(action.PayloadJson);
    }

    public DeleteEventAiActionMappingResult Map(string payloadJson)
    {
        var payloadResult = ReadPayload(payloadJson);
        if (!payloadResult.Succeeded)
        {
            return DeleteEventAiActionMappingResult.Failure(payloadResult.FailureCode!, payloadResult.FailureMessage!);
        }

        var payload = payloadResult.Payload!;
        if (payload.EventId is not { } eventId || eventId == Guid.Empty)
        {
            return DeleteEventAiActionMappingResult.Failure(
                "missing_event_id",
                "AI event deletion payload must include the event id.");
        }

        if (payload.ExpectedConcurrencyStamp is not { } expectedConcurrencyStamp || expectedConcurrencyStamp == Guid.Empty)
        {
            return DeleteEventAiActionMappingResult.Failure(
                "missing_expected_concurrency_stamp",
                "AI event deletion payload must include the expected concurrency stamp.");
        }

        if (payload.ManagementContextHasDelete is not true)
        {
            return DeleteEventAiActionMappingResult.Failure(
                "missing_delete_affordance_context",
                "AI event deletion payload must confirm the current management context exposes delete.");
        }

        var destructiveSummary = Normalize(payload.DestructiveSummary);
        if (string.IsNullOrWhiteSpace(destructiveSummary))
        {
            return DeleteEventAiActionMappingResult.Failure(
                "missing_destructive_summary",
                "AI event deletion payload must include a destructive summary.");
        }

        if (destructiveSummary.Length > 1_000)
        {
            return DeleteEventAiActionMappingResult.Failure(
                "field_too_long",
                "AI event deletion destructive summary exceeds the allowed length.");
        }

        if (!string.Equals(payload.ConfirmationPhrase, RequiredConfirmationPhrase, StringComparison.Ordinal))
        {
            return DeleteEventAiActionMappingResult.Failure(
                "missing_destructive_confirmation",
                "AI event deletion payload must include the exact destructive confirmation phrase.");
        }

        if (payload.AcknowledgedConsequences is not true)
        {
            return DeleteEventAiActionMappingResult.Failure(
                "missing_destructive_acknowledgement",
                "AI event deletion payload must acknowledge the destructive consequences.");
        }

        var context = new DeleteEventAiDestructiveContext(
            expectedConcurrencyStamp,
            ManagementContextHasDelete: true,
            destructiveSummary,
            RequiredConfirmationPhrase,
            AcknowledgedConsequences: true);

        return DeleteEventAiActionMappingResult.Success(eventId, context);
    }

    private static DeleteEventPayloadReadResult ReadPayload(string payloadJson)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return DeleteEventPayloadReadResult.Failure(
                    "invalid_payload_json",
                    "AI event deletion payload must be a JSON object.");
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!DeleteEventAiToolDefinition.AllowedPayloadFields.Contains(property.Name))
                {
                    return DeleteEventPayloadReadResult.Failure(
                        "unsupported_payload_field",
                        "AI event deletion payload contains a field that is not allowed.");
                }
            }

            var payload = document.RootElement.Deserialize<DeleteEventAiActionPayload>(JsonOptions);
            return payload is null
                ? DeleteEventPayloadReadResult.Failure("invalid_payload_json", "AI event deletion payload could not be read.")
                : DeleteEventPayloadReadResult.Success(payload);
        }
        catch (JsonException)
        {
            return DeleteEventPayloadReadResult.Failure(
                "invalid_payload_json",
                "AI event deletion payload must be valid JSON.");
        }
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record DeleteEventAiDestructiveContext(
    Guid ExpectedConcurrencyStamp,
    bool ManagementContextHasDelete,
    string DestructiveSummary,
    string ConfirmationPhrase,
    bool AcknowledgedConsequences);

public sealed record DeleteEventAiActionMappingResult(
    bool Succeeded,
    Guid? EventId,
    DeleteEventAiDestructiveContext? DestructiveContext,
    string? FailureCode,
    string? FailureMessage)
{
    public static DeleteEventAiActionMappingResult Success(Guid eventId, DeleteEventAiDestructiveContext destructiveContext)
        => new(true, eventId, destructiveContext, null, null);

    public static DeleteEventAiActionMappingResult Failure(string failureCode, string failureMessage)
        => new(false, null, null, failureCode, failureMessage);
}

sealed record DeleteEventPayloadReadResult(
    bool Succeeded,
    DeleteEventAiActionPayload? Payload,
    string? FailureCode,
    string? FailureMessage)
{
    public static DeleteEventPayloadReadResult Success(DeleteEventAiActionPayload payload)
        => new(true, payload, null, null);

    public static DeleteEventPayloadReadResult Failure(string failureCode, string failureMessage)
        => new(false, null, failureCode, failureMessage);
}
