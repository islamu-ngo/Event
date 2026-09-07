using System.Text.Json;
using Explore.Application.DTOs.Event;
using Explore.Application.Features.AiAssistant.Prompting;
using Explore.Application.Features.AiAssistant.Tools;
using Explore.Domain.Ai;
using Explore.Domain.Services.Registration;

namespace Explore.Application.Features.AiAssistant.Actions;

public sealed class UpdateEventDraftAiActionMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public UpdateEventDraftAiActionMappingResult Map(AiParsedProposedAction action)
    {
        if (action.Kind != AiProposedActionKind.UpdateEventDraft)
        {
            return UpdateEventDraftAiActionMappingResult.Failure(
                "unsupported_action_kind",
                "AI proposed action kind is not supported for event draft update mapping.");
        }

        return Map(action.PayloadJson);
    }

    public UpdateEventDraftAiActionMappingResult Map(string payloadJson)
    {
        UpdateEventDraftAiActionPayload? payload;
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return UpdateEventDraftAiActionMappingResult.Failure(
                    "invalid_payload_json",
                    "AI event draft update payload must be a JSON object.");
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!UpdateEventDraftAiToolDefinition.AllowedPayloadFields.Contains(property.Name))
                {
                    return UpdateEventDraftAiActionMappingResult.Failure(
                        "unsupported_payload_field",
                        "AI event draft update payload contains a field that is not allowed.");
                }
            }

            payload = document.RootElement.Deserialize<UpdateEventDraftAiActionPayload>(JsonOptions);
        }
        catch (JsonException)
        {
            return UpdateEventDraftAiActionMappingResult.Failure(
                "invalid_payload_json",
                "AI event draft update payload must be valid JSON.");
        }

        if (payload is null)
        {
            return UpdateEventDraftAiActionMappingResult.Failure(
                "invalid_payload_json",
                "AI event draft update payload could not be read.");
        }

        if (payload.EventId is not { } eventId || eventId == Guid.Empty)
        {
            return UpdateEventDraftAiActionMappingResult.Failure(
                "missing_event_id",
                "AI event draft update payload must include the event id.");
        }

        if (payload.ExpectedConcurrencyStamp is not { } expectedConcurrencyStamp || expectedConcurrencyStamp == Guid.Empty)
        {
            return UpdateEventDraftAiActionMappingResult.Failure(
                "missing_expected_concurrency_stamp",
                "AI event draft update payload must include the expected concurrency stamp.");
        }

        if (payload.ExpectedParticipationConfigurationConcurrencyStamp is not { } expectedParticipationConfigurationConcurrencyStamp
            || expectedParticipationConfigurationConcurrencyStamp == Guid.Empty)
        {
            return UpdateEventDraftAiActionMappingResult.Failure(
                "missing_expected_participation_configuration_concurrency_stamp",
                "AI event draft update payload must include the expected participation configuration concurrency stamp.");
        }

        var title = Normalize(payload.Title);
        if (string.IsNullOrWhiteSpace(title))
        {
            return UpdateEventDraftAiActionMappingResult.Failure(
                "missing_title",
                "AI event draft update payload must include a title.");
        }

        if (!ValidateLength(title, 500, "title", out var lengthFailure)
            || !ValidateLength(payload.Subtitle, 200, "subtitle", out lengthFailure)
            || !ValidateLength(payload.Description, 150, "description", out lengthFailure)
            || !ValidateLength(payload.Content, 5000, "content", out lengthFailure)
            || !ValidateLength(payload.Slug, 500, "slug", out lengthFailure)
            || !ValidateLength(payload.Timezone, 500, "timezone", out lengthFailure)
            || !ValidateLength(payload.EventTimeZoneId, 500, "eventTimeZoneId", out lengthFailure)
            || !ValidateLength(payload.BackgroundColor, 100, "backgroundColor", out lengthFailure)
            || !ValidateLength(payload.BackgroundEffect, 100, "backgroundEffect", out lengthFailure))
        {
            return lengthFailure!;
        }

        if (payload.SeriesOrder < 0)
        {
            return UpdateEventDraftAiActionMappingResult.Failure(
                "invalid_numeric_value",
                "AI event draft update series order cannot be negative.");
        }

        if (payload.ParticipationConfiguration is null)
        {
            return UpdateEventDraftAiActionMappingResult.Failure(
                "missing_participation_configuration",
                "AI event draft update payload must include a participation configuration.");
        }

        if (EventAuthorityRules.ValidateParticipationConfiguration(
                payload.ParticipationConfiguration.ParticipationHandlingModeId,
                payload.ParticipationConfiguration.AdvanceRegistrationObligationId,
                payload.ParticipationConfiguration.IdentityAccessModeId,
                payload.ParticipationConfiguration.GuestRecoveryPolicy).FirstOrDefault() is { } participationError)
        {
            return UpdateEventDraftAiActionMappingResult.Failure(
                "invalid_participation_configuration",
                $"{participationError.Code}: {participationError.Message}");
        }

        var draft = new UpdateEventDraftRequestDto
        {
            ExpectedConcurrencyStamp = expectedConcurrencyStamp,
            ExpectedParticipationConfigurationConcurrencyStamp = expectedParticipationConfigurationConcurrencyStamp,
            Title = title,
            Subtitle = Normalize(payload.Subtitle),
            Description = Normalize(payload.Description),
            Content = Normalize(payload.Content),
            Slug = Normalize(payload.Slug),
            EventTypeId = payload.EventTypeId,
            AudienceGenderId = payload.AudienceGenderId,
            AudienceAgeId = payload.AudienceAgeId,
            FeaturedImageId = payload.FeaturedImageId,
            ParticipationConfiguration = payload.ParticipationConfiguration,
            VisibilityTypeId = payload.VisibilityTypeId,
            EventFormatId = payload.EventFormatId,
            MadhabId = payload.MadhabId,
            Timezone = Normalize(payload.Timezone),
            EventTimeZoneId = Normalize(payload.EventTimeZoneId),
            BackgroundColor = Normalize(payload.BackgroundColor),
            BackgroundEffect = Normalize(payload.BackgroundEffect),
            BackgroundImageId = payload.BackgroundImageId,
            TemplateId = payload.TemplateId,
            EventSeriesId = payload.EventSeriesId,
            SeriesOrder = payload.SeriesOrder,
            RegistrationPolicyId = payload.RegistrationPolicyId
        };

        return UpdateEventDraftAiActionMappingResult.Success(eventId, draft);
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool ValidateLength(
        string? value,
        int maxLength,
        string fieldName,
        out UpdateEventDraftAiActionMappingResult? failure)
    {
        failure = null;

        if (Normalize(value)?.Length > maxLength)
        {
            failure = UpdateEventDraftAiActionMappingResult.Failure(
                "field_too_long",
                $"AI event draft update {fieldName} exceeds the allowed length.");
            return false;
        }

        return true;
    }
}

public sealed record UpdateEventDraftAiActionMappingResult(
    bool Succeeded,
    Guid? EventId,
    UpdateEventDraftRequestDto? Draft,
    string? FailureCode,
    string? FailureMessage)
{
    public static UpdateEventDraftAiActionMappingResult Success(Guid eventId, UpdateEventDraftRequestDto draft)
        => new(true, eventId, draft, null, null);

    public static UpdateEventDraftAiActionMappingResult Failure(string failureCode, string failureMessage)
        => new(false, null, null, failureCode, failureMessage);
}
