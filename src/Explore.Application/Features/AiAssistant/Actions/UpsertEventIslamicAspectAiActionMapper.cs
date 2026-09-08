using System.Text.Json;
using Explore.Application.DTOs.EventAspects;
using Explore.Application.Features.AiAssistant.Prompting;
using Explore.Application.Features.AiAssistant.Tools;
using Explore.Application.Features.EventAspects.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Ai;

namespace Explore.Application.Features.AiAssistant.Actions;

public sealed class UpsertEventIslamicAspectAiActionMapper
{
    private const string AspectKind = "islamic";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public UpsertEventIslamicAspectAiActionMappingResult Map(AiParsedProposedAction action)
    {
        if (action.Kind != AiProposedActionKind.UpsertEventIslamicAspect)
        {
            return UpsertEventIslamicAspectAiActionMappingResult.Failure(
                "unsupported_action_kind",
                "AI proposed action kind is not supported for Islamic aspect mapping.");
        }

        return Map(action.PayloadJson);
    }

    public UpsertEventIslamicAspectAiActionMappingResult Map(string payloadJson)
    {
        var readResult = ReadPayload(payloadJson);
        if (!readResult.Succeeded)
        {
            return UpsertEventIslamicAspectAiActionMappingResult.Failure(readResult.FailureCode!, readResult.FailureMessage!);
        }

        var payload = readResult.Payload!;
        var contextResult = ValidateCommonContext(payload);
        if (!contextResult.Succeeded)
        {
            return contextResult;
        }

        if (payload.GenderMode is not { } genderMode || !Enum.IsDefined(typeof(GenderSegregationMode), genderMode))
        {
            return UpsertEventIslamicAspectAiActionMappingResult.Failure(
                "invalid_gender_mode",
                "AI Islamic aspect payload must include a valid gender mode.");
        }

        if (payload.ReferencePrayer is { } referencePrayer && !Enum.IsDefined(typeof(PrayerTime), referencePrayer))
        {
            return UpsertEventIslamicAspectAiActionMappingResult.Failure(
                "invalid_reference_prayer",
                "AI Islamic aspect payload includes an invalid reference prayer.");
        }

        if (payload.PrayerTimeOffset is < -180 or > 180)
        {
            return UpsertEventIslamicAspectAiActionMappingResult.Failure(
                "invalid_prayer_time_offset",
                "AI Islamic aspect prayer time offset must be between -180 and 180 minutes.");
        }

        if (payload.PrayerTimeOffset.HasValue && !payload.ReferencePrayer.HasValue)
        {
            return UpsertEventIslamicAspectAiActionMappingResult.Failure(
                "missing_reference_prayer",
                "AI Islamic aspect prayer time offset requires a reference prayer.");
        }

        if (payload.MadhabId is <= 0 || payload.PrimaryLanguageId is <= 0)
        {
            return UpsertEventIslamicAspectAiActionMappingResult.Failure(
                "invalid_lookup_reference",
                "AI Islamic aspect lookup references must be positive identifiers.");
        }

        var dto = new UpdateEventIslamicAspectDto
        {
            Jurisprudence = new UpdateEventIslamicJurisprudenceDto
            {
                MadhabId = new(true, payload.MadhabId)
            },
            PrayerSchedule = new UpdateEventIslamicPrayerScheduleDto
            {
                ReferencePrayer = new(true, payload.ReferencePrayer.HasValue ? (PrayerTime)payload.ReferencePrayer.Value : null),
                PrayerTimeOffset = new(true, payload.PrayerTimeOffset)
            },
            Participation = new UpdateEventIslamicParticipationDto
            {
                GenderMode = (GenderSegregationMode)genderMode,
                IncludesQuranRecitation = payload.IncludesQuranRecitation
            },
            Language = new UpdateEventIslamicLanguageDto
            {
                PrimaryLanguageId = new(true, payload.PrimaryLanguageId)
            }
        };
        var command = new UpdateEventIslamicAspectCommand
        {
            EventId = contextResult.EventId!.Value,
            AspectDto = dto
        };

        return UpsertEventIslamicAspectAiActionMappingResult.Success(
            contextResult.EventId.Value,
            command,
            contextResult.PermissionContext!);
    }

    private static UpsertEventIslamicAspectAiActionMappingResult ValidateCommonContext(
        UpsertEventIslamicAspectAiActionPayload payload)
    {
        if (payload.EventId is not { } eventId || eventId == Guid.Empty)
        {
            return UpsertEventIslamicAspectAiActionMappingResult.Failure(
                "missing_event_id",
                "AI Islamic aspect payload must include the event id.");
        }

        if (payload.ExpectedConcurrencyStamp is not { } expectedConcurrencyStamp || expectedConcurrencyStamp == Guid.Empty)
        {
            return UpsertEventIslamicAspectAiActionMappingResult.Failure(
                "missing_expected_concurrency_stamp",
                "AI Islamic aspect payload must include the expected concurrency stamp.");
        }

        if (!string.Equals(payload.AspectKind, AspectKind, StringComparison.Ordinal))
        {
            return UpsertEventIslamicAspectAiActionMappingResult.Failure(
                "invalid_aspect_kind",
                "AI Islamic aspect payload must include the Islamic aspect module context.");
        }

        if (payload.ManagementContextHasEdit is not true)
        {
            return UpsertEventIslamicAspectAiActionMappingResult.Failure(
                "missing_edit_affordance_context",
                "AI Islamic aspect payload must confirm the current management context exposes edit.");
        }

        return UpsertEventIslamicAspectAiActionMappingResult.ContextOnly(
            eventId,
            new EventAspectAiPermissionContext(expectedConcurrencyStamp, AspectKind, ManagementContextHasEdit: true));
    }

    private static IslamicAspectPayloadReadResult ReadPayload(string payloadJson)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return IslamicAspectPayloadReadResult.Failure(
                    "invalid_payload_json",
                    "AI Islamic aspect payload must be a JSON object.");
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!UpsertEventIslamicAspectAiToolDefinition.AllowedPayloadFields.Contains(property.Name))
                {
                    return IslamicAspectPayloadReadResult.Failure(
                        "unsupported_payload_field",
                        "AI Islamic aspect payload contains a field that is not allowed.");
                }
            }

            var payload = document.RootElement.Deserialize<UpsertEventIslamicAspectAiActionPayload>(JsonOptions);
            return payload is null
                ? IslamicAspectPayloadReadResult.Failure("invalid_payload_json", "AI Islamic aspect payload could not be read.")
                : IslamicAspectPayloadReadResult.Success(payload);
        }
        catch (JsonException)
        {
            return IslamicAspectPayloadReadResult.Failure(
                "invalid_payload_json",
                "AI Islamic aspect payload must be valid JSON.");
        }
    }
}

public sealed record UpsertEventIslamicAspectAiActionMappingResult(
    bool Succeeded,
    Guid? EventId,
    UpdateEventIslamicAspectCommand? Command,
    EventAspectAiPermissionContext? PermissionContext,
    string? FailureCode,
    string? FailureMessage)
{
    public static UpsertEventIslamicAspectAiActionMappingResult Success(
        Guid eventId,
        UpdateEventIslamicAspectCommand command,
        EventAspectAiPermissionContext permissionContext)
        => new(true, eventId, command, permissionContext, null, null);

    public static UpsertEventIslamicAspectAiActionMappingResult ContextOnly(
        Guid eventId,
        EventAspectAiPermissionContext permissionContext)
        => new(true, eventId, null, permissionContext, null, null);

    public static UpsertEventIslamicAspectAiActionMappingResult Failure(string failureCode, string failureMessage)
        => new(false, null, null, null, failureCode, failureMessage);
}

sealed record IslamicAspectPayloadReadResult(
    bool Succeeded,
    UpsertEventIslamicAspectAiActionPayload? Payload,
    string? FailureCode,
    string? FailureMessage)
{
    public static IslamicAspectPayloadReadResult Success(UpsertEventIslamicAspectAiActionPayload payload)
        => new(true, payload, null, null);

    public static IslamicAspectPayloadReadResult Failure(string failureCode, string failureMessage)
        => new(false, null, failureCode, failureMessage);
}
