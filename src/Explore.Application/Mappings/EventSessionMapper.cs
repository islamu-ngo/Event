using Explore.Application.DTOs.EventSession;
using Explore.Application.DTOs.EventSessionAgendaItem;
using Explore.Application.DTOs.EventSessionGroup;
using Explore.Application.DTOs.EventSessionLanguage;
using Explore.Application.DTOs.EventSessionSpeaker;
using Explore.Domain;
using Explore.Domain.Enums;
using Riok.Mapperly.Abstractions;

namespace Explore.Application.Mappings;

// Read projections only. Handlers own writes, lifecycle transitions and location disclosure.
[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Both, AutoUserMappings = false)]
public static partial class EventSessionMapper
{
    public static EventSessionDto ToDetail(EventSession source) => MapDetail(source) with
    {
        IsScheduled = IsScheduled(source),
        FormattedEndTime = FormatEndTime(source)
    };

    public static EventSessionListDto ToListItem(EventSession source) => MapListItem(source) with
    {
        IsScheduled = IsScheduled(source),
        FormattedEndTime = FormatEndTime(source)
    };

    // No audit, template provenance, tenant graph or physical location is disclosed by the mapper.
    // Schedule-derived fields are assigned by the wrappers; the loaded local cache is read-only.
    [MapperIgnoreSource(nameof(EventSession.EventDay))]
    [MapperIgnoreSource(nameof(EventSession.EventLocationId))]
    [MapperIgnoreSource(nameof(EventSession.EventLocation))]
    [MapperIgnoreSource(nameof(EventSession.LocationId))]
    [MapperIgnoreSource(nameof(EventSession.Location))]
    [MapperIgnoreSource(nameof(EventSession.RoomId))]
    [MapperIgnoreSource(nameof(EventSession.Room))]
    [MapperIgnoreSource(nameof(EventSession.Tenant))]
    [MapperIgnoreSource(nameof(EventSession.SourceTemplateId))]
    [MapperIgnoreSource(nameof(EventSession.SourceTemplateKey))]
    [MapperIgnoreSource(nameof(EventSession.SourceTemplateVersion))]
    [MapperIgnoreSource(nameof(EventSession.InstantiatedFromTemplateAt))]
    [MapperIgnoreSource(nameof(EventSession.LastSyncedFromTemplateAt))]
    [MapperIgnoreSource(nameof(EventSession.CreatedAt))]
    [MapperIgnoreSource(nameof(EventSession.CreatedBy))]
    [MapperIgnoreSource(nameof(EventSession.UpdatedAt))]
    [MapperIgnoreSource(nameof(EventSession.UpdatedBy))]
    [MapperIgnoreSource(nameof(EventSession.IsDeleted))]
    [MapperIgnoreSource(nameof(EventSession.DeletedAt))]
    [MapperIgnoreSource(nameof(EventSession.DeletedBy))]
    [MapperIgnoreTarget(nameof(EventSessionDto.IsScheduled))]
    [MapperIgnoreTarget(nameof(EventSessionDto.FormattedEndTime))]
    [MapperIgnoreTarget(nameof(EventSessionDto.LocationId))]
    [MapperIgnoreTarget(nameof(EventSessionDto.LocationFullName))]
    [MapperIgnoreTarget(nameof(EventSessionDto.LocationAddress))]
    [MapperIgnoreTarget(nameof(EventSessionDto.LocationCity))]
    [MapperIgnoreTarget(nameof(EventSessionDto.LocationCountry))]
    [MapperIgnoreTarget(nameof(EventSessionDto.RoomId))]
    [MapperIgnoreTarget(nameof(EventSessionDto.RoomName))]
    [MapperIgnoreTarget(nameof(EventSessionDto.EventLocation))]
    [MapProperty(nameof(EventSession.Event), nameof(EventSessionDto.EventTitle), Use = nameof(EventTitle))]
    [MapProperty(nameof(EventSession.Event), nameof(EventSessionDto.ParentEventStatusId), Use = nameof(ParentStatus))]
    [MapProperty(nameof(EventSession.EventSessionKind), nameof(EventSessionDto.EventSessionKindFullName), Use = nameof(KindName))]
    [MapProperty(nameof(EventSession.EventSessionKind), nameof(EventSessionDto.EventSessionKindMasterCode), Use = nameof(KindCode))]
    [MapProperty(nameof(EventSession.EventSessionStatus), nameof(EventSessionDto.EventSessionStatusFullName), Use = nameof(StatusName))]
    [MapProperty(nameof(EventSession.EventSessionStatus), nameof(EventSessionDto.EventSessionStatusMasterCode), Use = nameof(StatusCode))]
    [MapProperty(nameof(EventSession.RegistrationMode), nameof(EventSessionDto.RegistrationModeFullName), Use = nameof(RegistrationName))]
    [MapProperty(nameof(EventSession.RegistrationMode), nameof(EventSessionDto.RegistrationModeMasterCode), Use = nameof(RegistrationCode))]
    [MapProperty(nameof(EventSession.FeaturedImage), nameof(EventSessionDto.FeaturedImageUri), Use = nameof(ImageUri))]
    [MapProperty(nameof(EventSession.IslamicAspect), nameof(EventSessionDto.IslamicAspect), Use = nameof(MapAspect))]
    [MapProperty(nameof(EventSession.SessionGroups), nameof(EventSessionDto.SessionGroups), Use = nameof(PublishedGroups))]
    private static partial EventSessionDto MapDetail(EventSession source);

    // List intentionally omits description, local end date/minute caches and registration code.
    [MapperIgnoreSource(nameof(EventSession.Description))]
    [MapperIgnoreSource(nameof(EventSession.LocalEndDate))]
    [MapperIgnoreSource(nameof(EventSession.LocalStartMinuteOfDay))]
    [MapperIgnoreSource(nameof(EventSession.LocalEndMinuteOfDay))]
    [MapperIgnoreSource(nameof(EventSession.EventDay))]
    [MapperIgnoreSource(nameof(EventSession.EventLocationId))]
    [MapperIgnoreSource(nameof(EventSession.EventLocation))]
    [MapperIgnoreSource(nameof(EventSession.LocationId))]
    [MapperIgnoreSource(nameof(EventSession.Location))]
    [MapperIgnoreSource(nameof(EventSession.RoomId))]
    [MapperIgnoreSource(nameof(EventSession.Room))]
    [MapperIgnoreSource(nameof(EventSession.Tenant))]
    [MapperIgnoreSource(nameof(EventSession.SourceTemplateId))]
    [MapperIgnoreSource(nameof(EventSession.SourceTemplateKey))]
    [MapperIgnoreSource(nameof(EventSession.SourceTemplateVersion))]
    [MapperIgnoreSource(nameof(EventSession.InstantiatedFromTemplateAt))]
    [MapperIgnoreSource(nameof(EventSession.LastSyncedFromTemplateAt))]
    [MapperIgnoreSource(nameof(EventSession.CreatedAt))]
    [MapperIgnoreSource(nameof(EventSession.CreatedBy))]
    [MapperIgnoreSource(nameof(EventSession.UpdatedAt))]
    [MapperIgnoreSource(nameof(EventSession.UpdatedBy))]
    [MapperIgnoreSource(nameof(EventSession.IsDeleted))]
    [MapperIgnoreSource(nameof(EventSession.DeletedAt))]
    [MapperIgnoreSource(nameof(EventSession.DeletedBy))]
    [MapperIgnoreTarget(nameof(EventSessionListDto.IsScheduled))]
    [MapperIgnoreTarget(nameof(EventSessionListDto.FormattedEndTime))]
    [MapperIgnoreTarget(nameof(EventSessionListDto.LocationId))]
    [MapperIgnoreTarget(nameof(EventSessionListDto.LocationFullName))]
    [MapperIgnoreTarget(nameof(EventSessionListDto.LocationCity))]
    [MapperIgnoreTarget(nameof(EventSessionListDto.RoomId))]
    [MapperIgnoreTarget(nameof(EventSessionListDto.RoomName))]
    [MapperIgnoreTarget(nameof(EventSessionListDto.EventLocation))]
    [MapProperty(nameof(EventSession.Event), nameof(EventSessionListDto.EventTitle), Use = nameof(EventTitle))]
    [MapProperty(nameof(EventSession.Event), nameof(EventSessionListDto.ParentEventStatusId), Use = nameof(ParentStatus))]
    [MapProperty(nameof(EventSession.EventSessionKind), nameof(EventSessionListDto.EventSessionKindFullName), Use = nameof(KindName))]
    [MapProperty(nameof(EventSession.EventSessionKind), nameof(EventSessionListDto.EventSessionKindMasterCode), Use = nameof(KindCode))]
    [MapProperty(nameof(EventSession.EventSessionStatus), nameof(EventSessionListDto.EventSessionStatusFullName), Use = nameof(StatusName))]
    [MapProperty(nameof(EventSession.EventSessionStatus), nameof(EventSessionListDto.EventSessionStatusMasterCode), Use = nameof(StatusCode))]
    [MapProperty(nameof(EventSession.RegistrationMode), nameof(EventSessionListDto.RegistrationModeFullName), Use = nameof(RegistrationName))]
    [MapProperty(nameof(EventSession.FeaturedImage), nameof(EventSessionListDto.FeaturedImageUri), Use = nameof(ImageUri))]
    [MapProperty(nameof(EventSession.IslamicAspect), nameof(EventSessionListDto.IslamicAspect), Use = nameof(MapAspect))]
    [MapProperty(nameof(EventSession.SessionGroups), nameof(EventSessionListDto.SessionGroups), Use = nameof(PublishedGroups))]
    private static partial EventSessionListDto MapListItem(EventSession source);

    // Shared key and back reference do not belong in the Islamic extension response.
    [MapperIgnoreSource(nameof(EventSessionIslamicAspect.EventSessionId))]
    [MapperIgnoreSource(nameof(EventSessionIslamicAspect.EventSession))]
    private static partial EventSessionIslamicAspectDto? MapAspect(EventSessionIslamicAspect? source);

    // The group DTO is a bounded assignment, never a copy of either side of the relationship.
    [MapperIgnoreSource(nameof(EventSessionGroupSession.Id))]
    [MapperIgnoreSource(nameof(EventSessionGroupSession.EventSessionId))]
    [MapperIgnoreSource(nameof(EventSessionGroupSession.EventSession))]
    [MapperIgnoreSource(nameof(EventSessionGroupSession.EventId))]
    [MapperIgnoreSource(nameof(EventSessionGroupSession.Event))]
    [MapperIgnoreSource(nameof(EventSessionGroupSession.TenantId))]
    [MapperIgnoreSource(nameof(EventSessionGroupSession.Tenant))]
    [MapperIgnoreSource(nameof(EventSessionGroupSession.CreatedAt))]
    [MapperIgnoreSource(nameof(EventSessionGroupSession.CreatedBy))]
    [MapperIgnoreSource(nameof(EventSessionGroupSession.UpdatedAt))]
    [MapperIgnoreSource(nameof(EventSessionGroupSession.UpdatedBy))]
    [MapperIgnoreSource(nameof(EventSessionGroupSession.IsDeleted))]
    [MapperIgnoreSource(nameof(EventSessionGroupSession.DeletedAt))]
    [MapperIgnoreSource(nameof(EventSessionGroupSession.DeletedBy))]
    [MapProperty("EventSessionGroup.Name", nameof(EventSessionGroupAssignmentDto.Name))]
    [MapProperty("EventSessionGroup.Slug", nameof(EventSessionGroupAssignmentDto.Slug))]
    [MapProperty("EventSessionGroup.Color", nameof(EventSessionGroupAssignmentDto.Color))]
    private static partial EventSessionGroupAssignmentDto MapAssignment(EventSessionGroupSession source);

    // Physical location and audit/relationship graphs remain outside both group projections.
    [MapperIgnoreSource(nameof(EventSessionGroup.EventLocationId))]
    [MapperIgnoreSource(nameof(EventSessionGroup.EventLocation))]
    [MapperIgnoreSource(nameof(EventSessionGroup.LocationId))]
    [MapperIgnoreSource(nameof(EventSessionGroup.Location))]
    [MapperIgnoreSource(nameof(EventSessionGroup.RoomId))]
    [MapperIgnoreSource(nameof(EventSessionGroup.Room))]
    [MapperIgnoreSource(nameof(EventSessionGroup.Tenant))]
    [MapperIgnoreSource(nameof(EventSessionGroup.Sessions))]
    [MapperIgnoreSource(nameof(EventSessionGroup.CreatedAt))]
    [MapperIgnoreSource(nameof(EventSessionGroup.CreatedBy))]
    [MapperIgnoreSource(nameof(EventSessionGroup.UpdatedAt))]
    [MapperIgnoreSource(nameof(EventSessionGroup.UpdatedBy))]
    [MapperIgnoreSource(nameof(EventSessionGroup.IsDeleted))]
    [MapperIgnoreSource(nameof(EventSessionGroup.DeletedAt))]
    [MapperIgnoreSource(nameof(EventSessionGroup.DeletedBy))]
    [MapperIgnoreTarget(nameof(EventSessionGroupDto.LocationId))]
    [MapperIgnoreTarget(nameof(EventSessionGroupDto.LocationName))]
    [MapperIgnoreTarget(nameof(EventSessionGroupDto.RoomId))]
    [MapperIgnoreTarget(nameof(EventSessionGroupDto.RoomName))]
    [MapperIgnoreTarget(nameof(EventSessionGroupDto.EventLocation))]
    [MapProperty(nameof(EventSessionGroup.Event), nameof(EventSessionGroupDto.EventTitle), Use = nameof(EventTitle))]
    public static partial EventSessionGroupDto ToDetail(EventSessionGroup source);

    [MapperIgnoreSource(nameof(EventSessionGroup.Event))]
    [MapperIgnoreSource(nameof(EventSessionGroup.EventLocationId))]
    [MapperIgnoreSource(nameof(EventSessionGroup.EventLocation))]
    [MapperIgnoreSource(nameof(EventSessionGroup.LocationId))]
    [MapperIgnoreSource(nameof(EventSessionGroup.Location))]
    [MapperIgnoreSource(nameof(EventSessionGroup.RoomId))]
    [MapperIgnoreSource(nameof(EventSessionGroup.Room))]
    [MapperIgnoreSource(nameof(EventSessionGroup.Tenant))]
    [MapperIgnoreSource(nameof(EventSessionGroup.Sessions))]
    [MapperIgnoreSource(nameof(EventSessionGroup.CreatedAt))]
    [MapperIgnoreSource(nameof(EventSessionGroup.CreatedBy))]
    [MapperIgnoreSource(nameof(EventSessionGroup.UpdatedAt))]
    [MapperIgnoreSource(nameof(EventSessionGroup.UpdatedBy))]
    [MapperIgnoreSource(nameof(EventSessionGroup.IsDeleted))]
    [MapperIgnoreSource(nameof(EventSessionGroup.DeletedAt))]
    [MapperIgnoreSource(nameof(EventSessionGroup.DeletedBy))]
    [MapperIgnoreTarget(nameof(EventSessionGroupListDto.LocationId))]
    [MapperIgnoreTarget(nameof(EventSessionGroupListDto.LocationName))]
    [MapperIgnoreTarget(nameof(EventSessionGroupListDto.RoomId))]
    [MapperIgnoreTarget(nameof(EventSessionGroupListDto.RoomName))]
    [MapperIgnoreTarget(nameof(EventSessionGroupListDto.EventLocation))]
    public static partial EventSessionGroupListDto ToListItem(EventSessionGroup source);

    // Agenda location disclosure is resolved after mapping by the owning handler.
    [MapperIgnoreSource(nameof(EventSessionAgendaItem.EventLocationId))]
    [MapperIgnoreSource(nameof(EventSessionAgendaItem.EventLocation))]
    [MapperIgnoreSource(nameof(EventSessionAgendaItem.LocationId))]
    [MapperIgnoreSource(nameof(EventSessionAgendaItem.Location))]
    [MapperIgnoreSource(nameof(EventSessionAgendaItem.Tenant))]
    [MapperIgnoreTarget(nameof(EventSessionAgendaItemDto.LocationId))]
    [MapperIgnoreTarget(nameof(EventSessionAgendaItemDto.LocationFullName))]
    [MapperIgnoreTarget(nameof(EventSessionAgendaItemDto.EventLocation))]
    [MapProperty(nameof(EventSessionAgendaItem.EventSession), nameof(EventSessionAgendaItemDto.EventId), Use = nameof(SessionEventId))]
    [MapProperty(nameof(EventSessionAgendaItem.EventSession), nameof(EventSessionAgendaItemDto.EventSessionTitle), Use = nameof(SessionTitle))]
    public static partial EventSessionAgendaItemDto ToDetail(EventSessionAgendaItem source);

    [MapperIgnoreSource(nameof(EventSessionAgendaItem.Description))]
    [MapperIgnoreSource(nameof(EventSessionAgendaItem.EventLocationId))]
    [MapperIgnoreSource(nameof(EventSessionAgendaItem.EventLocation))]
    [MapperIgnoreSource(nameof(EventSessionAgendaItem.LocationId))]
    [MapperIgnoreSource(nameof(EventSessionAgendaItem.Location))]
    [MapperIgnoreSource(nameof(EventSessionAgendaItem.Tenant))]
    [MapperIgnoreTarget(nameof(EventSessionAgendaItemListDto.LocationFullName))]
    [MapperIgnoreTarget(nameof(EventSessionAgendaItemListDto.EventLocation))]
    [MapProperty(nameof(EventSessionAgendaItem.EventSession), nameof(EventSessionAgendaItemListDto.EventId), Use = nameof(SessionEventId))]
    [MapProperty(nameof(EventSessionAgendaItem.EventSession), nameof(EventSessionAgendaItemListDto.EventSessionTitle), Use = nameof(SessionTitle))]
    public static partial EventSessionAgendaItemListDto ToListItem(EventSessionAgendaItem source);

    // Speaker disclosure is limited to actor display name, not its PII or identity graph.
    [MapperIgnoreSource(nameof(EventSessionSpeaker.Tenant))]
    [MapProperty(nameof(EventSessionSpeaker.Actor), nameof(EventSessionSpeakerDto.ActorDisplayName), Use = nameof(ActorName))]
    [MapProperty(nameof(EventSessionSpeaker.EventSession), nameof(EventSessionSpeakerDto.EventId), Use = nameof(SessionEventId))]
    [MapProperty(nameof(EventSessionSpeaker.EventSession), nameof(EventSessionSpeakerDto.EventSessionTitle), Use = nameof(SessionTitle))]
    public static partial EventSessionSpeakerDto ToDetail(EventSessionSpeaker source);

    [MapperIgnoreSource(nameof(EventSessionSpeaker.Tenant))]
    [MapProperty(nameof(EventSessionSpeaker.Actor), nameof(EventSessionSpeakerListDto.ActorDisplayName), Use = nameof(ActorName))]
    [MapProperty(nameof(EventSessionSpeaker.EventSession), nameof(EventSessionSpeakerListDto.EventId), Use = nameof(SessionEventId))]
    [MapProperty(nameof(EventSessionSpeaker.EventSession), nameof(EventSessionSpeakerListDto.EventSessionTitle), Use = nameof(SessionTitle))]
    public static partial EventSessionSpeakerListDto ToListItem(EventSessionSpeaker source);

    // EventId was not flattened by the old language maps; selected handlers populate it explicitly.
    [MapperIgnoreSource(nameof(EventSessionLanguage.Tenant))]
    [MapperIgnoreTarget(nameof(EventSessionLanguageDto.EventId))]
    [MapProperty(nameof(EventSessionLanguage.EventSession), nameof(EventSessionLanguageDto.EventSessionTitle), Use = nameof(SessionTitle))]
    [MapProperty(nameof(EventSessionLanguage.Language), nameof(EventSessionLanguageDto.LanguageFullName), Use = nameof(LanguageName))]
    [MapProperty(nameof(EventSessionLanguage.Language), nameof(EventSessionLanguageDto.LanguageMasterCode), Use = nameof(LanguageCode))]
    public static partial EventSessionLanguageDto ToDetail(EventSessionLanguage source);

    [MapperIgnoreSource(nameof(EventSessionLanguage.Tenant))]
    [MapperIgnoreTarget(nameof(EventSessionLanguageListDto.EventId))]
    [MapProperty(nameof(EventSessionLanguage.EventSession), nameof(EventSessionLanguageListDto.EventSessionTitle), Use = nameof(SessionTitle))]
    [MapProperty(nameof(EventSessionLanguage.Language), nameof(EventSessionLanguageListDto.LanguageFullName), Use = nameof(LanguageName))]
    [MapProperty(nameof(EventSessionLanguage.Language), nameof(EventSessionLanguageListDto.LanguageMasterCode), Use = nameof(LanguageCode))]
    public static partial EventSessionLanguageListDto ToListItem(EventSessionLanguage source);

    private static IReadOnlyList<EventSessionGroupAssignmentDto> PublishedGroups(ICollection<EventSessionGroupSession> assignments) =>
        assignments.Where(assignment => assignment.EventSessionGroup.IsPublished)
            .OrderByDescending(assignment => assignment.IsPrimary)
            .ThenBy(assignment => assignment.SortOrder)
            .Select(MapAssignment).ToArray();

    // Required entity navigation annotations do not imply that a query loaded the navigation.
    // An unloaded parent has no display title; identity and status remain separate fields.
    private static string? EventTitle(Event? parent) => parent?.Title;
    private static int ParentStatus(Event? parent) => parent?.EventStatusId ?? 0;
    private static string? KindName(EventSessionKind? kind) => kind?.FullName;
    private static string? KindCode(EventSessionKind? kind) => kind?.MasterCode;
    private static string? StatusName(EventSessionStatus? status) => status?.FullName;
    private static string? StatusCode(EventSessionStatus? status) => status?.MasterCode;
    private static string? RegistrationName(RegistrationMode? mode) => mode?.FullName;
    private static string? RegistrationCode(RegistrationMode? mode) => mode?.MasterCode;
    private static string? ImageUri(StorageObject? image) => image?.Uri;
    private static Guid SessionEventId(EventSession? session) => session?.EventId ?? Guid.Empty;
    private static string? SessionTitle(EventSession? session) => session?.Title;
    private static string? ActorName(Actor? actor) => actor?.Pii?.DisplayName;
    private static string? LanguageName(Language? language) => language?.FullName;
    private static string? LanguageCode(Language? language) => language?.MasterCode;

    private static bool IsScheduled(EventSession source) => source.StartTime.HasValue &&
        ((source.EndTimeType == SessionEndTimeType.Fixed && source.EndTime > source.StartTime) ||
         (source.EndTimeType == SessionEndTimeType.OpenEnded && source.EndTime == null) ||
         (source.EndTimeType == SessionEndTimeType.RelativeToPrayer &&
          (source.EndTime == null || source.EndTime > source.StartTime)));

    private static string? FormatEndTime(EventSession source) => source.EndTimeType switch
    {
        SessionEndTimeType.OpenEnded => "Open-ended",
        SessionEndTimeType.RelativeToPrayer => FormatRelativeEndTime(source.IslamicAspect),
        SessionEndTimeType.Fixed => source.LocalEndTime?.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture),
        _ => null
    };

    private static string FormatRelativeEndTime(EventSessionIslamicAspect? aspect)
    {
        if (aspect?.EndReferencePrayer is not { } prayer)
            return "Relative to prayer";

        var offset = aspect.EndOffsetMinutes ?? 0;
        return offset switch
        {
            0 => $"Until {prayer} prayer",
            > 0 => $"Until {offset} minutes after {prayer}",
            _ => $"Until {Math.Abs(offset)} minutes before {prayer}"
        };
    }
}
