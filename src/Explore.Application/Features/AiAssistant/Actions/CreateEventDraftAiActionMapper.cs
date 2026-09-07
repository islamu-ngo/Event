using System.Text.Json;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.EventAspects;
using Explore.Application.DTOs.EventSession;
using Explore.Application.Features.AiAssistant.Prompting;
using Explore.Application.Features.AiAssistant.Tools;
using Explore.Domain.Ai;
using Explore.Domain.Services.Registration;

namespace Explore.Application.Features.AiAssistant.Actions;

public sealed class CreateEventDraftAiActionMapper
{
    private const string PrimaryLocationTempKey = "primary-location";
    private const string PrimaryRoomTempKey = "primary-room";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public CreateEventDraftAiActionMappingResult Map(
        AiParsedProposedAction action,
        CreateEventDraftAiActionMappingContext? context = null)
    {
        if (action.Kind != AiProposedActionKind.CreateEventDraft)
        {
            return CreateEventDraftAiActionMappingResult.Failure(
                "unsupported_action_kind",
                "AI proposed action kind is not supported for event draft mapping.");
        }

        return Map(action.PayloadJson, context);
    }

    public CreateEventDraftAiActionMappingResult Map(
        string payloadJson,
        CreateEventDraftAiActionMappingContext? context = null)
    {
        context ??= CreateEventDraftAiActionMappingContext.Empty;

        CreateEventDraftAiActionPayload? payload;
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return CreateEventDraftAiActionMappingResult.Failure(
                    "invalid_payload_json",
                    "AI event draft payload must be a JSON object.");
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!CreateEventDraftAiToolDefinition.AllowedPayloadFields.Contains(property.Name))
                {
                    return CreateEventDraftAiActionMappingResult.Failure(
                        "unsupported_payload_field",
                        "AI event draft payload contains a field that is not allowed.");
                }
            }

            payload = document.RootElement.Deserialize<CreateEventDraftAiActionPayload>(JsonOptions);
        }
        catch (JsonException)
        {
            return CreateEventDraftAiActionMappingResult.Failure(
                "invalid_payload_json",
                "AI event draft payload must be valid JSON.");
        }

        if (payload is null)
        {
            return CreateEventDraftAiActionMappingResult.Failure(
                "invalid_payload_json",
                "AI event draft payload could not be read.");
        }

        var title = Normalize(payload.Title);
        if (string.IsNullOrWhiteSpace(title))
        {
            return CreateEventDraftAiActionMappingResult.Failure(
                "missing_title",
                "AI event draft payload must include a title.");
        }

        if (!ValidateLength(title, 200, "title", out var lengthFailure)
            || !ValidateLength(payload.Subtitle, 200, "subtitle", out lengthFailure)
            || !ValidateLength(payload.Description, 150, "description", out lengthFailure)
            || !ValidateLength(payload.Content, 5000, "content", out lengthFailure)
            || !ValidateLength(payload.Slug, 500, "slug", out lengthFailure)
            || !ValidateLength(payload.Timezone, 100, "timezone", out lengthFailure)
            || !ValidateLength(payload.EventTimeZoneId, 100, "eventTimeZoneId", out lengthFailure))
        {
            return lengthFailure!;
        }

        if (payload.ParticipationConfiguration is null)
        {
            return CreateEventDraftAiActionMappingResult.Failure(
                "missing_participation_configuration",
                "AI event draft payload must include a participation configuration.");
        }

        if (EventAuthorityRules.ValidateParticipationConfiguration(
                payload.ParticipationConfiguration.ParticipationHandlingModeId,
                payload.ParticipationConfiguration.AdvanceRegistrationObligationId,
                payload.ParticipationConfiguration.IdentityAccessModeId,
                payload.ParticipationConfiguration.GuestRecoveryPolicy).FirstOrDefault() is { } participationError)
        {
            return CreateEventDraftAiActionMappingResult.Failure(
                "invalid_participation_configuration",
                $"{participationError.Code}: {participationError.Message}");
        }

        var hasForcedOwnerScope = context.HasForcedOwnerScope;
        if (!hasForcedOwnerScope && payload.OrganizationId.HasValue && payload.GroupId.HasValue)
        {
            return CreateEventDraftAiActionMappingResult.Failure(
                "conflicting_owner_scope",
                "AI event draft payload cannot target both an organization and a group.");
        }

        if (!hasForcedOwnerScope
            && payload.OrganizationId.HasValue
            && !context.AllowedOrganizationIds.Contains(payload.OrganizationId.Value))
        {
            return CreateEventDraftAiActionMappingResult.Failure(
                "invalid_organization_scope",
                "AI event draft organization is not allowed for this mapping context.");
        }

        if (!hasForcedOwnerScope
            && payload.GroupId.HasValue
            && !context.AllowedGroupIds.Contains(payload.GroupId.Value))
        {
            return CreateEventDraftAiActionMappingResult.Failure(
                "invalid_group_scope",
                "AI event draft group is not allowed for this mapping context.");
        }

        var (organizationId, groupId) = ResolveOwnerScope(payload, context);

        var locations = NormalizeLocation(payload.Location);
        var rooms = NormalizeRoom(payload.Room, locations.Count > 0);
        var sessions = NormalizeSession(payload.Session, locations.Count > 0, rooms.Count > 0);

        var draft = new CreateEventDraftRequestDto
        {
            Title = title,
            Subtitle = Normalize(payload.Subtitle),
            Description = Normalize(payload.Description),
            Content = Normalize(payload.Content),
            Slug = Normalize(payload.Slug),
            EventTypeId = payload.EventTypeId,
            AudienceGenderId = payload.AudienceGenderId,
            AudienceAgeId = payload.AudienceAgeId,
            OrganizationId = organizationId,
            GroupId = groupId,
            ParticipationConfiguration = payload.ParticipationConfiguration,
            VisibilityTypeId = payload.VisibilityTypeId == 0 ? 1 : payload.VisibilityTypeId,
            EventFormatId = payload.EventFormatId == 0 ? 1 : payload.EventFormatId,
            MadhabId = payload.MadhabId,
            Timezone = Normalize(payload.Timezone),
            EventTimeZoneId = Normalize(payload.EventTimeZoneId),
            IslamicAspect = NormalizeIslamicAspect(payload.IslamicAspect),
            CategoryIds = payload.CategoryIds.Distinct().ToList(),
            TagIds = payload.TagIds.Distinct().ToList(),
            Locations = locations,
            Rooms = rooms,
            Sessions = sessions
        };

        return CreateEventDraftAiActionMappingResult.Success(draft);
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeRequired(string? value)
        => Normalize(value) ?? string.Empty;

    private static CreateUpdateIslamicAspectDto? NormalizeIslamicAspect(CreateUpdateIslamicAspectDto? aspect)
        => aspect is null
            ? null
            : new CreateUpdateIslamicAspectDto
            {
                MadhabId = aspect.MadhabId,
                ReferencePrayer = aspect.ReferencePrayer,
                PrayerTimeOffset = aspect.PrayerTimeOffset,
                GenderMode = aspect.GenderMode,
                IncludesQuranRecitation = aspect.IncludesQuranRecitation,
                PrimaryLanguageId = aspect.PrimaryLanguageId
            };

    private static EventSessionIslamicAspectDto? NormalizeSessionIslamicAspect(EventSessionIslamicAspectDto? aspect)
        => aspect is null
            ? null
            : new EventSessionIslamicAspectDto
            {
                StartTimeType = aspect.StartTimeType,
                ReferencePrayer = aspect.ReferencePrayer,
                OffsetMinutes = aspect.OffsetMinutes,
                RequiresWudu = aspect.RequiresWudu,
                RitualRequirementsJson = Normalize(aspect.RitualRequirementsJson)
            };

    private static List<CreateEventLocationDto> NormalizeLocation(CreateEventDraftLocationPayload? location)
    {
        if (location is null || !HasCompleteLocation(location))
        {
            return [];
        }

        return
        [
            new CreateEventLocationDto
            {
                TempKey = PrimaryLocationTempKey,
                FullName = NormalizeRequired(location.FullName),
                Address = NormalizeRequired(location.Address),
                Postcode = NormalizeRequired(location.Postcode),
                Country = NormalizeRequired(location.Country),
                City = NormalizeRequired(location.City),
                Timezone = Normalize(location.Timezone)
            }
        ];
    }

    private static List<CreateEventRoomDto> NormalizeRoom(CreateEventDraftRoomPayload? room, bool hasPrimaryLocation)
    {
        if (room is null || !hasPrimaryLocation || string.IsNullOrWhiteSpace(room.Name))
        {
            return [];
        }

        return
        [
            new CreateEventRoomDto
            {
                TempKey = PrimaryRoomTempKey,
                LocationTempKey = PrimaryLocationTempKey,
                Name = NormalizeRequired(room.Name),
                Slug = Normalize(room.Slug),
                Description = Normalize(room.Description),
                Capacity = room.Capacity
            }
        ];
    }

    private static List<CreateEventGraphSessionDto> NormalizeSession(
        CreateEventDraftSessionPayload? session,
        bool hasPrimaryLocation,
        bool hasPrimaryRoom)
    {
        if (session is null || session.StartTime == default || session.EndTime == default || session.EndTime <= session.StartTime)
        {
            return [];
        }

        return
        [
            new CreateEventGraphSessionDto
            {
                TempKey = "primary-session",
                RoomTempKey = hasPrimaryLocation && hasPrimaryRoom ? PrimaryRoomTempKey : null,
                StartTime = session.StartTime,
                EndTime = session.EndTime,
                LocationTempKey = hasPrimaryLocation && !hasPrimaryRoom ? PrimaryLocationTempKey : null,
                Title = Normalize(session.Title),
                EventSessionKindId = session.EventSessionKindId,
                Description = Normalize(session.Description),
                Slug = Normalize(session.Slug),
                MaxAudienceAttendees = session.MaxAudienceAttendees,
                RegistrationModeId = session.RegistrationModeId,
                IslamicAspect = NormalizeSessionIslamicAspect(session.IslamicAspect),
                LanguageIds = session.LanguageIds.Distinct().ToList(),
                SpeakerActorIds = session.SpeakerActorIds.Distinct().ToList()
            }
        ];
    }

    private static bool HasCompleteLocation(CreateEventDraftLocationPayload location)
        => !string.IsNullOrWhiteSpace(location.FullName)
            && !string.IsNullOrWhiteSpace(location.Address)
            && !string.IsNullOrWhiteSpace(location.Postcode)
            && !string.IsNullOrWhiteSpace(location.Country)
            && !string.IsNullOrWhiteSpace(location.City);

    private static (Guid? OrganizationId, Guid? GroupId) ResolveOwnerScope(
        CreateEventDraftAiActionPayload payload,
        CreateEventDraftAiActionMappingContext context)
    {
        if (context.ForcePersonalOwnerScope)
        {
            return (null, null);
        }

        if (context.ForcedOrganizationId.HasValue)
        {
            return (context.ForcedOrganizationId, null);
        }

        if (context.ForcedGroupId.HasValue)
        {
            return (null, context.ForcedGroupId);
        }

        return (payload.OrganizationId, payload.GroupId);
    }

    private static bool ValidateLength(
        string? value,
        int maxLength,
        string fieldName,
        out CreateEventDraftAiActionMappingResult? failure)
    {
        failure = null;

        if (Normalize(value)?.Length > maxLength)
        {
            failure = CreateEventDraftAiActionMappingResult.Failure(
                "field_too_long",
                $"AI event draft {fieldName} exceeds the allowed length.");
            return false;
        }

        return true;
    }
}

public sealed record CreateEventDraftAiActionMappingContext(
    IReadOnlySet<Guid> AllowedOrganizationIds,
    IReadOnlySet<Guid> AllowedGroupIds)
{
    public Guid? ForcedOrganizationId { get; init; }
    public Guid? ForcedGroupId { get; init; }
    public bool ForcePersonalOwnerScope { get; init; }
    public bool HasForcedOwnerScope => ForcePersonalOwnerScope || ForcedOrganizationId.HasValue || ForcedGroupId.HasValue;

    public static CreateEventDraftAiActionMappingContext Empty { get; } = new(new HashSet<Guid>(), new HashSet<Guid>());
}

public sealed record CreateEventDraftAiActionMappingResult(
    bool Succeeded,
    CreateEventDraftRequestDto? Draft,
    string? FailureCode,
    string? FailureMessage)
{
    public static CreateEventDraftAiActionMappingResult Success(CreateEventDraftRequestDto draft)
        => new(true, draft, null, null);

    public static CreateEventDraftAiActionMappingResult Failure(string failureCode, string failureMessage)
        => new(false, null, failureCode, failureMessage);
}
