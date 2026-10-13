using System.Text.Json;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Helpers;
using Explore.Blazor.Client.Serialization;

namespace Explore.Blazor.Client.Pages.Events.Models;

public class SessionEditorModel
{
    public Guid LocalId { get; private set; } = Guid.NewGuid();
    public string TempKey { get; set; } = Guid.NewGuid().ToString("N");
    public Guid? Id { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public DateTimeOffset? ExistingStartInstant { get; set; }
    public DateTimeOffset? ExistingEndInstant { get; set; }
    public SessionEndTimeType EndTimeType { get; set; } = SessionEndTimeType.Fixed;
    public Guid? LocationId { get; set; }
    public string? LocationTempKey { get; set; }
    public Guid? RoomId { get; set; }
    public string? RoomTempKey { get; set; }
    public string? DayTempKey { get; set; }
    public string? Slug { get; set; }
    public int? EventSessionKindId { get; set; }
    public EventSessionIslamicAspectDto? IslamicAspect { get; set; }
    public int? MaxAudienceAttendees { get; set; }
    public int? RegistrationModeId { get; set; } = 1;
    public Guid? SessionTemplateId { get; set; }
    public IReadOnlyCollection<int> LanguageIds { get; set; } = new HashSet<int>();
    public IReadOnlyCollection<Guid> SpeakerActorIds { get; set; } = Array.Empty<Guid>();

    // Media
    public Guid? FeaturedImageId { get; set; }
    public string? FeaturedImagePreviewUrl { get; set; }
    public bool UseEventImage { get; set; } = true;
    public SessionEditorModel Clone(bool newLocalId = false)
    {
        var clone = (SessionEditorModel)MemberwiseClone();
        clone.LanguageIds = Array.AsReadOnly(LanguageIds.ToArray());
        clone.SpeakerActorIds = Array.AsReadOnly(SpeakerActorIds.ToArray());
        if (IslamicAspect is not null)
        {
            clone.IslamicAspect = JsonSerializer.Deserialize(
                JsonSerializer.SerializeToUtf8Bytes(IslamicAspect, AppJsonSerializerContext.Default.EventSessionIslamicAspectDto),
                AppJsonSerializerContext.Default.EventSessionIslamicAspectDto);
        }
        if (newLocalId)
        {
            clone.LocalId = Guid.NewGuid();
            clone.TempKey = clone.LocalId.ToString("N");
            clone.Id = null;
        }
        return clone;
    }

    public static SessionEditorModel FromDto(EventSessionListDto dto, string? eventImageUri = null, string eventTimeZoneId = "UTC")
    {
        return new SessionEditorModel
        {
            Id = dto.Id,
            Title = dto.Title ?? dto.EventTitle,
            Description = null,
            StartTime = dto.StartTime.HasValue ? DateTimeHelper.ConvertUtcToLocal(dto.StartTime.Value, eventTimeZoneId) : null,
            EndTime = dto.EndTime.HasValue ? DateTimeHelper.ConvertUtcToLocal(dto.EndTime.Value, eventTimeZoneId) : null,
            ExistingStartInstant = dto.StartTime,
            ExistingEndInstant = dto.EndTime,
            EndTimeType = dto.EndTimeType ?? SessionEndTimeType.Fixed,
            LocationId = dto.LocationId,
            RoomId = dto.RoomId,
            Slug = dto.Slug,
            EventSessionKindId = dto.EventSessionKindId,
            IslamicAspect = dto.IslamicAspect,
            MaxAudienceAttendees = dto.MaxAudienceAttendees,
            RegistrationModeId = dto.RegistrationModeId,
            FeaturedImageId = dto.FeaturedImageId,
            FeaturedImagePreviewUrl = dto.FeaturedImageUri ?? eventImageUri,
            UseEventImage = !dto.FeaturedImageId.HasValue
        }.Clone();
    }
}
