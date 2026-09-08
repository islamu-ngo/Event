using System.Text.Json;
using Explore.Application.DTOs.EventAspects;
using Explore.Application.Features.AiAssistant.Prompting;
using Explore.Application.Features.AiAssistant.Tools;
using Explore.Application.Features.EventAspects.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Ai;

namespace Explore.Application.Features.AiAssistant.Actions;

public sealed class UpsertEventTechAspectAiActionMapper
{
    private const string AspectKind = "tech";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public UpsertEventTechAspectAiActionMappingResult Map(AiParsedProposedAction action)
    {
        if (action.Kind != AiProposedActionKind.UpsertEventTechAspect)
        {
            return UpsertEventTechAspectAiActionMappingResult.Failure(
                "unsupported_action_kind",
                "AI proposed action kind is not supported for Tech aspect mapping.");
        }

        return Map(action.PayloadJson);
    }

    public UpsertEventTechAspectAiActionMappingResult Map(string payloadJson)
    {
        var readResult = ReadPayload(payloadJson);
        if (!readResult.Succeeded)
        {
            return UpsertEventTechAspectAiActionMappingResult.Failure(readResult.FailureCode!, readResult.FailureMessage!);
        }

        var payload = readResult.Payload!;
        var contextResult = ValidateCommonContext(payload);
        if (!contextResult.Succeeded)
        {
            return contextResult;
        }

        if (payload.SkillLevel is not { } skillLevel || !Enum.IsDefined(typeof(SkillLevel), skillLevel))
        {
            return UpsertEventTechAspectAiActionMappingResult.Failure(
                "invalid_skill_level",
                "AI Tech aspect payload must include a valid skill level.");
        }

        if (!ValidateLength(payload.GithubRepoUrl, 500, "GitHub repository URL", out var lengthFailure)
            || !ValidateLength(payload.HackathonTrack, 200, "hackathon track", out lengthFailure)
            || !ValidateLength(payload.TechStackTags, 1_000, "tech stack tags", out lengthFailure)
            || !ValidateLength(payload.PrizeCurrencyCode, 3, "prize currency code", out lengthFailure))
        {
            return lengthFailure!;
        }

        var githubRepoUrl = Normalize(payload.GithubRepoUrl);
        if (githubRepoUrl is not null &&
            (!Uri.TryCreate(githubRepoUrl, UriKind.Absolute, out var uri) ||
             (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
              !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))))
        {
            return UpsertEventTechAspectAiActionMappingResult.Failure(
                "invalid_github_repo_url",
                "AI Tech aspect GitHub repository URL must be an absolute HTTP or HTTPS URL.");
        }

        if (payload.MaxTeamSize is < 1 or > 100)
        {
            return UpsertEventTechAspectAiActionMappingResult.Failure(
                "invalid_max_team_size",
                "AI Tech aspect max team size must be between 1 and 100.");
        }

        if (payload.PrizePool is < 0)
        {
            return UpsertEventTechAspectAiActionMappingResult.Failure(
                "invalid_prize_pool",
                "AI Tech aspect prize pool cannot be negative.");
        }

        var prizeCurrencyCode = Normalize(payload.PrizeCurrencyCode)?.ToUpperInvariant();
        if (payload.PrizePool is > 0 && string.IsNullOrWhiteSpace(prizeCurrencyCode))
        {
            return UpsertEventTechAspectAiActionMappingResult.Failure(
                "missing_prize_currency_code",
                "AI Tech aspect prize currency code is required when a prize pool is specified.");
        }

        if (prizeCurrencyCode is not null &&
            (prizeCurrencyCode.Length != 3 || prizeCurrencyCode.Any(character => character is < 'A' or > 'Z')))
        {
            return UpsertEventTechAspectAiActionMappingResult.Failure(
                "invalid_prize_currency_code",
                "AI Tech aspect prize currency code must be a 3-letter ISO code.");
        }

        var dto = new UpdateEventTechAspectDto
        {
            Repository = new UpdateEventTechRepositoryDto
            {
                GithubRepoUrl = new(true, githubRepoUrl)
            },
            Classification = new UpdateEventTechClassificationDto
            {
                HackathonTrack = new(true, Normalize(payload.HackathonTrack)),
                SkillLevel = (SkillLevel)skillLevel,
                TechStackTags = new(true, Normalize(payload.TechStackTags))
            },
            Participation = new UpdateEventTechParticipationDto
            {
                RequiresLaptop = payload.RequiresLaptop,
                IsCodingCompetition = payload.IsCodingCompetition
            },
            Prize = new UpdateEventTechPrizeDto
            {
                MaxTeamSize = new(true, payload.MaxTeamSize),
                PrizePool = new(true, payload.PrizePool),
                PrizeCurrencyCode = new(true, prizeCurrencyCode)
            }
        };
        var command = new UpdateEventTechAspectCommand
        {
            EventId = contextResult.EventId!.Value,
            AspectDto = dto
        };

        return UpsertEventTechAspectAiActionMappingResult.Success(
            contextResult.EventId.Value,
            command,
            contextResult.PermissionContext!);
    }

    private static UpsertEventTechAspectAiActionMappingResult ValidateCommonContext(
        UpsertEventTechAspectAiActionPayload payload)
    {
        if (payload.EventId is not { } eventId || eventId == Guid.Empty)
        {
            return UpsertEventTechAspectAiActionMappingResult.Failure(
                "missing_event_id",
                "AI Tech aspect payload must include the event id.");
        }

        if (payload.ExpectedConcurrencyStamp is not { } expectedConcurrencyStamp || expectedConcurrencyStamp == Guid.Empty)
        {
            return UpsertEventTechAspectAiActionMappingResult.Failure(
                "missing_expected_concurrency_stamp",
                "AI Tech aspect payload must include the expected concurrency stamp.");
        }

        if (!string.Equals(payload.AspectKind, AspectKind, StringComparison.Ordinal))
        {
            return UpsertEventTechAspectAiActionMappingResult.Failure(
                "invalid_aspect_kind",
                "AI Tech aspect payload must include the Tech aspect module context.");
        }

        if (payload.ManagementContextHasEdit is not true)
        {
            return UpsertEventTechAspectAiActionMappingResult.Failure(
                "missing_edit_affordance_context",
                "AI Tech aspect payload must confirm the current management context exposes edit.");
        }

        return UpsertEventTechAspectAiActionMappingResult.ContextOnly(
            eventId,
            new EventAspectAiPermissionContext(expectedConcurrencyStamp, AspectKind, ManagementContextHasEdit: true));
    }

    private static TechAspectPayloadReadResult ReadPayload(string payloadJson)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return TechAspectPayloadReadResult.Failure(
                    "invalid_payload_json",
                    "AI Tech aspect payload must be a JSON object.");
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!UpsertEventTechAspectAiToolDefinition.AllowedPayloadFields.Contains(property.Name))
                {
                    return TechAspectPayloadReadResult.Failure(
                        "unsupported_payload_field",
                        "AI Tech aspect payload contains a field that is not allowed.");
                }
            }

            var payload = document.RootElement.Deserialize<UpsertEventTechAspectAiActionPayload>(JsonOptions);
            return payload is null
                ? TechAspectPayloadReadResult.Failure("invalid_payload_json", "AI Tech aspect payload could not be read.")
                : TechAspectPayloadReadResult.Success(payload);
        }
        catch (JsonException)
        {
            return TechAspectPayloadReadResult.Failure(
                "invalid_payload_json",
                "AI Tech aspect payload must be valid JSON.");
        }
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool ValidateLength(
        string? value,
        int maxLength,
        string fieldName,
        out UpsertEventTechAspectAiActionMappingResult? failure)
    {
        failure = null;

        if (Normalize(value)?.Length > maxLength)
        {
            failure = UpsertEventTechAspectAiActionMappingResult.Failure(
                "field_too_long",
                $"AI Tech aspect {fieldName} exceeds the allowed length.");
            return false;
        }

        return true;
    }
}

public sealed record UpsertEventTechAspectAiActionMappingResult(
    bool Succeeded,
    Guid? EventId,
    UpdateEventTechAspectCommand? Command,
    EventAspectAiPermissionContext? PermissionContext,
    string? FailureCode,
    string? FailureMessage)
{
    public static UpsertEventTechAspectAiActionMappingResult Success(
        Guid eventId,
        UpdateEventTechAspectCommand command,
        EventAspectAiPermissionContext permissionContext)
        => new(true, eventId, command, permissionContext, null, null);

    public static UpsertEventTechAspectAiActionMappingResult ContextOnly(
        Guid eventId,
        EventAspectAiPermissionContext permissionContext)
        => new(true, eventId, null, permissionContext, null, null);

    public static UpsertEventTechAspectAiActionMappingResult Failure(string failureCode, string failureMessage)
        => new(false, null, null, null, failureCode, failureMessage);
}

sealed record TechAspectPayloadReadResult(
    bool Succeeded,
    UpsertEventTechAspectAiActionPayload? Payload,
    string? FailureCode,
    string? FailureMessage)
{
    public static TechAspectPayloadReadResult Success(UpsertEventTechAspectAiActionPayload payload)
        => new(true, payload, null, null);

    public static TechAspectPayloadReadResult Failure(string failureCode, string failureMessage)
        => new(false, null, failureCode, failureMessage);
}
