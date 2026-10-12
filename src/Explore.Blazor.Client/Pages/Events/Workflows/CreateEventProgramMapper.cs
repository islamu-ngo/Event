using System.Text.Json;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Helpers;
using Explore.Blazor.Client.Pages.Events.Models;
using Explore.Blazor.Client.Serialization;

namespace Explore.Blazor.Client.Pages.Events.Workflows;

internal static class CreateEventProgramMapper
{
    public static List<SessionEditorModel> FromRequest(CreateEventDraftRequestDto source)
    {
        var snapshot = Snapshot(source);
        var timezone = snapshot.EventTimeZoneId ?? snapshot.Timezone ?? "UTC";
        return (snapshot.Sessions ?? [])
            .Select(row => new SessionEditorModel
            {
                TempKey = string.IsNullOrWhiteSpace(row.TempKey) ? Guid.NewGuid().ToString("N") : row.TempKey,
                Title = row.Title,
                Description = row.Description,
                StartTime = row.StartTime.HasValue ? DateTimeHelper.ConvertUtcToLocal(row.StartTime.Value, timezone) : null,
                EndTime = row.EndTime.HasValue ? DateTimeHelper.ConvertUtcToLocal(row.EndTime.Value, timezone) : null,
                ExistingStartInstant = row.StartTime,
                ExistingEndInstant = row.EndTime,
                EndTimeType = row.EndTimeType ?? SessionEndTimeType.Fixed,
                LocationId = row.LocationId,
                LocationTempKey = row.LocationTempKey,
                RoomId = row.RoomId,
                RoomTempKey = row.RoomTempKey,
                DayTempKey = row.DayTempKey,
                FeaturedImageId = row.FeaturedImageId,
                UseEventImage = !row.FeaturedImageId.HasValue,
                Slug = row.Slug,
                EventSessionKindId = row.EventSessionKindId,
                MaxAudienceAttendees = row.MaxAudienceAttendees,
                RegistrationModeId = row.RegistrationModeId,
                SessionTemplateId = row.SessionTemplateId,
                IslamicAspect = row.IslamicAspect,
                LanguageIds = Array.AsReadOnly((row.LanguageIds ?? []).ToArray()),
                SpeakerActorIds = Array.AsReadOnly((row.SpeakerActorIds ?? []).ToArray())
            }).ToList();
    }

    public static bool TryMap(
        CreateEventDraftRequestDto parent,
        IReadOnlyList<SessionEditorModel> rows,
        out CreateEventDraftRequestDto? request,
        out string? validationError)
    {
        request = null;
        validationError = null;
        var snapshot = Snapshot(parent);
        snapshot.Title = snapshot.Title?.Trim() ?? string.Empty;
        snapshot.Subtitle = string.IsNullOrWhiteSpace(snapshot.Subtitle) ? null : snapshot.Subtitle.Trim();
        snapshot.Description = string.IsNullOrWhiteSpace(snapshot.Description) ? null : snapshot.Description.Trim();
        snapshot.Content = string.IsNullOrWhiteSpace(snapshot.Content) ? null : snapshot.Content.Trim();
        snapshot.Slug = string.IsNullOrWhiteSpace(snapshot.Slug) ? null : snapshot.Slug.Trim();
        var timezone = snapshot.EventTimeZoneId ?? snapshot.Timezone;
        var mapped = new List<CreateEventGraphSessionDto>(rows.Count);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index].Clone();
            if (!TryValidateRow(row, timezone, snapshot.EventStatusId == 2, out var start, out var end, out var field, out var error))
            {
                validationError = $"Sessions[{index}].{field}: {error}";
                return false;
            }

            mapped.Add(new CreateEventGraphSessionDto
            {
                TempKey = row.TempKey,
                Title = row.Title!.Trim(),
                Description = row.Description,
                StartTime = start,
                EndTime = end,
                EndTimeType = row.EndTimeType,
                LocationId = row.LocationId,
                LocationTempKey = row.LocationTempKey,
                RoomId = row.RoomId,
                RoomTempKey = row.RoomTempKey,
                DayTempKey = row.DayTempKey,
                FeaturedImageId = row.FeaturedImageId,
                Slug = row.Slug,
                EventSessionKindId = row.EventSessionKindId,
                SortOrder = index + 1,
                MaxAudienceAttendees = row.MaxAudienceAttendees,
                RegistrationModeId = row.RegistrationModeId,
                SessionTemplateId = row.SessionTemplateId,
                IslamicAspect = row.IslamicAspect,
                LanguageIds = Array.AsReadOnly(row.LanguageIds.ToArray()),
                SpeakerActorIds = Array.AsReadOnly(row.SpeakerActorIds.ToArray())
            });
        }

        snapshot.Sessions = mapped.AsReadOnly();
        snapshot.CategoryIds = Array.AsReadOnly((snapshot.CategoryIds ?? []).ToArray());
        snapshot.TagIds = Array.AsReadOnly((snapshot.TagIds ?? []).ToArray());
        snapshot.Locations = Array.AsReadOnly((snapshot.Locations ?? []).ToArray());
        snapshot.Days = Array.AsReadOnly((snapshot.Days ?? []).ToArray());
        snapshot.Rooms = Array.AsReadOnly((snapshot.Rooms ?? []).ToArray());
        snapshot.AgendaItems = Array.AsReadOnly((snapshot.AgendaItems ?? []).ToArray());
        request = snapshot;
        return true;
    }

    private static bool TryValidateRow(
        SessionEditorModel row, string? timezone, bool publish,
        out DateTimeOffset? start, out DateTimeOffset? end, out string field, out string? error)
    {
        start = null;
        end = null;
        field = "Title";
        error = null;
        if (string.IsNullOrWhiteSpace(row.Title))
        {
            error = "Program item title is required.";
            return false;
        }
        if (row.MaxAudienceAttendees is <= 0)
        {
            field = "MaxAudienceAttendees";
            error = "Capacity must be greater than zero or left empty.";
            return false;
        }
        field = "StartTime";
        if (!row.StartTime.HasValue && !row.EndTime.HasValue)
        {
            if (publish || row.EndTimeType == SessionEndTimeType.RelativeToPrayer
                || row.IslamicAspect?.StartTimeType == SessionStartTimeType.RelativeToPrayer)
            {
                error = "This program item needs a schedule before publishing or using prayer-relative timing.";
                return false;
            }
            return true;
        }
        if (!row.StartTime.HasValue)
        {
            error = "Choose the start date and time.";
            return false;
        }
        if (!DateTimeHelper.TryConvertLocalToUtc(row.StartTime.Value, timezone, row.ExistingStartInstant, out var startUtc, out error))
        {
            return false;
        }
        start = startUtc;
        field = "EndTime";
        if (row.EndTimeType == SessionEndTimeType.OpenEnded)
        {
            if (row.EndTime.HasValue)
            {
                error = "An open-ended session must not have an end date or time.";
                return false;
            }
            return true;
        }
        if (!row.EndTime.HasValue)
        {
            if (row.EndTimeType == SessionEndTimeType.RelativeToPrayer)
            {
                return true;
            }
            error = "Choose the end date and time for this fixed session.";
            return false;
        }
        if (!DateTimeHelper.TryConvertLocalToUtc(row.EndTime.Value, timezone, row.ExistingEndInstant, out var endUtc, out error))
        {
            return false;
        }
        if (endUtc <= startUtc)
        {
            error = "End date and time must be after start date and time.";
            return false;
        }
        end = endUtc;
        return true;
    }

    public static CreateEventDraftRequestDto Snapshot(CreateEventDraftRequestDto source) =>
        JsonSerializer.Deserialize(
            JsonSerializer.SerializeToUtf8Bytes(source, AppJsonSerializerContext.Default.CreateEventDraftRequestDto),
            AppJsonSerializerContext.Default.CreateEventDraftRequestDto)!;
}
