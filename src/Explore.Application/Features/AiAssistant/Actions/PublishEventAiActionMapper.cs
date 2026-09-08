using System.Text.Json;
using Explore.Application.DTOs.Event;
using Explore.Application.Features.AiAssistant.Prompting;
using Explore.Application.Features.AiAssistant.Tools;
using Explore.Domain.Ai;

namespace Explore.Application.Features.AiAssistant.Actions;

public sealed class PublishEventAiActionMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public PublishEventAiActionMappingResult Map(AiParsedProposedAction action)
    {
        if (action.Kind != AiProposedActionKind.PublishEvent)
        {
            return PublishEventAiActionMappingResult.Failure(
                "unsupported_action_kind",
                "AI proposed action kind is not supported for event publish mapping.");
        }

        return Map(action.PayloadJson);
    }

    public PublishEventAiActionMappingResult Map(string payloadJson)
    {
        PublishEventAiActionPayload? payload;
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return PublishEventAiActionMappingResult.Failure(
                    "invalid_payload_json",
                    "AI event publish payload must be a JSON object.");
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!PublishEventAiToolDefinition.AllowedPayloadFields.Contains(property.Name))
                {
                    return PublishEventAiActionMappingResult.Failure(
                        "unsupported_payload_field",
                        "AI event publish payload contains a field that is not allowed.");
                }
            }

            payload = document.RootElement.Deserialize<PublishEventAiActionPayload>(JsonOptions);
        }
        catch (JsonException)
        {
            return PublishEventAiActionMappingResult.Failure(
                "invalid_payload_json",
                "AI event publish payload must be valid JSON.");
        }

        if (payload is null)
        {
            return PublishEventAiActionMappingResult.Failure(
                "invalid_payload_json",
                "AI event publish payload could not be read.");
        }

        if (payload.EventId is not { } eventId || eventId == Guid.Empty)
        {
            return PublishEventAiActionMappingResult.Failure(
                "missing_event_id",
                "AI event publish payload must include the event id.");
        }

        if (payload.ExpectedConcurrencyStamp is not { } expectedConcurrencyStamp || expectedConcurrencyStamp == Guid.Empty)
        {
            return PublishEventAiActionMappingResult.Failure(
                "missing_expected_concurrency_stamp",
                "AI event publish payload must include the expected concurrency stamp.");
        }

        if (payload.ReadinessIsReady is not true)
        {
            return PublishEventAiActionMappingResult.Failure(
                "publish_readiness_not_ready",
                "AI event publish payload must include a ready publish-readiness context.");
        }

        if (payload.ReadinessErrorCount is not 0)
        {
            return PublishEventAiActionMappingResult.Failure(
                "publish_readiness_has_errors",
                "AI event publish payload cannot propose publishing when readiness errors are present.");
        }

        if (!ValidateLength(payload.ReadinessSummary, 1_000, out var lengthFailure))
        {
            return lengthFailure!;
        }

        var request = new PublishEventRequestDto
        {
            ExpectedConcurrencyStamp = expectedConcurrencyStamp
        };
        var readinessContext = new PublishEventAiReadinessContext(
            IsReady: true,
            ErrorCount: 0,
            CheckedAtUtc: payload.ReadinessCheckedAtUtc,
            Summary: Normalize(payload.ReadinessSummary));

        return PublishEventAiActionMappingResult.Success(eventId, request, readinessContext);
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool ValidateLength(
        string? value,
        int maxLength,
        out PublishEventAiActionMappingResult? failure)
    {
        failure = null;

        if (Normalize(value)?.Length > maxLength)
        {
            failure = PublishEventAiActionMappingResult.Failure(
                "field_too_long",
                "AI event publish readiness summary exceeds the allowed length.");
            return false;
        }

        return true;
    }
}

public sealed record PublishEventAiReadinessContext(
    bool IsReady,
    int ErrorCount,
    DateTimeOffset? CheckedAtUtc,
    string? Summary);

public sealed record PublishEventAiActionMappingResult(
    bool Succeeded,
    Guid? EventId,
    PublishEventRequestDto? Request,
    PublishEventAiReadinessContext? ReadinessContext,
    string? FailureCode,
    string? FailureMessage)
{
    public static PublishEventAiActionMappingResult Success(
        Guid eventId,
        PublishEventRequestDto request,
        PublishEventAiReadinessContext readinessContext)
        => new(true, eventId, request, readinessContext, null, null);

    public static PublishEventAiActionMappingResult Failure(string failureCode, string failureMessage)
        => new(false, null, null, null, failureCode, failureMessage);
}
