using System;
using System.Linq;
using AutoMapper;
using Explore.Application.DTOs.EventSession;
using Explore.Application.DTOs.EventSessionAgendaItem;
using Explore.Application.DTOs.EventSessionGroup;
using Explore.Application.DTOs.EventSessionLanguage;
using Explore.Application.DTOs.EventSessionSpeaker;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Profiles;

public class EventSessionMappingProfile : Profile
{
    public EventSessionMappingProfile()
    {
        // Event Session Islamic Aspect
        CreateMap<EventSessionIslamicAspect, EventSessionIslamicAspectDto>();

        // Event Session
        CreateMap<EventSessionGroupSession, EventSessionGroupAssignmentDto>()
            .ForMember(dest => dest.Name, opt => opt.MapFrom(src => src.EventSessionGroup.Name))
            .ForMember(dest => dest.Slug, opt => opt.MapFrom(src => src.EventSessionGroup.Slug))
            .ForMember(dest => dest.Color, opt => opt.MapFrom(src => src.EventSessionGroup.Color));
        CreateMap<EventSessionGroup, EventSessionGroupDto>()
            .ForMember(dest => dest.EventTitle, opt => opt.MapFrom(src => src.Event != null ? src.Event.Title : null))
            .ForMember(dest => dest.LocationId, opt => opt.Ignore())
            .ForMember(dest => dest.LocationName, opt => opt.Ignore())
            .ForMember(dest => dest.RoomId, opt => opt.Ignore())
            .ForMember(dest => dest.RoomName, opt => opt.Ignore())
            .ForMember(dest => dest.EventLocation, opt => opt.Ignore());
        CreateMap<EventSessionGroup, EventSessionGroupListDto>()
            .ForMember(dest => dest.LocationId, opt => opt.Ignore())
            .ForMember(dest => dest.LocationName, opt => opt.Ignore())
            .ForMember(dest => dest.RoomId, opt => opt.Ignore())
            .ForMember(dest => dest.RoomName, opt => opt.Ignore())
            .ForMember(dest => dest.EventLocation, opt => opt.Ignore());
        CreateMap<CreateEventSessionGroupRequestDto, EventSessionGroup>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.Event, opt => opt.Ignore())
            .ForMember(dest => dest.Location, opt => opt.Ignore())
            .ForMember(dest => dest.Room, opt => opt.Ignore())
            .ForMember(dest => dest.TenantId, opt => opt.Ignore())
            .ForMember(dest => dest.Tenant, opt => opt.Ignore())
            .ForMember(dest => dest.Sessions, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedBy, opt => opt.Ignore())
            .ForMember(dest => dest.IsDeleted, opt => opt.Ignore())
            .ForMember(dest => dest.DeletedAt, opt => opt.Ignore())
            .ForMember(dest => dest.DeletedBy, opt => opt.Ignore())
            .ForMember(dest => dest.ConcurrencyStamp, opt => opt.Ignore());
        CreateMap<EventSession, EventSessionDto>()
            .ForMember(dest => dest.EventTitle, opt => opt.MapFrom(src => src.Event != null ? src.Event.Title : null))
            .ForMember(dest => dest.ParentEventStatusId, opt => opt.MapFrom(src => src.Event.EventStatusId))
            .ForMember(dest => dest.EventSessionKindFullName, opt => opt.MapFrom(src => src.EventSessionKind != null ? src.EventSessionKind.FullName : null))
            .ForMember(dest => dest.EventSessionKindMasterCode, opt => opt.MapFrom(src => src.EventSessionKind != null ? src.EventSessionKind.MasterCode : null))
            .ForMember(dest => dest.EventSessionStatusFullName, opt => opt.MapFrom(src => src.EventSessionStatus != null ? src.EventSessionStatus.FullName : null))
            .ForMember(dest => dest.EventSessionStatusMasterCode, opt => opt.MapFrom(src => src.EventSessionStatus != null ? src.EventSessionStatus.MasterCode : null))
            .ForMember(dest => dest.IsScheduled, opt => opt.MapFrom(src => src.StartTime.HasValue &&
                ((src.EndTimeType == SessionEndTimeType.Fixed && src.EndTime > src.StartTime) ||
                 (src.EndTimeType == SessionEndTimeType.OpenEnded && src.EndTime == null) ||
                 (src.EndTimeType == SessionEndTimeType.RelativeToPrayer &&
                  (src.EndTime == null || src.EndTime > src.StartTime)))))
            .ForMember(dest => dest.LocationId, opt => opt.Ignore())
            .ForMember(dest => dest.LocationFullName, opt => opt.Ignore())
            .ForMember(dest => dest.LocationAddress, opt => opt.Ignore())
            .ForMember(dest => dest.LocationCity, opt => opt.Ignore())
            .ForMember(dest => dest.LocationCountry, opt => opt.Ignore())
            .ForMember(dest => dest.RoomId, opt => opt.Ignore())
            .ForMember(dest => dest.RoomName, opt => opt.Ignore())
            .ForMember(dest => dest.EventLocation, opt => opt.Ignore())
            .ForMember(dest => dest.FeaturedImageUri, opt => opt.MapFrom(src => src.FeaturedImage != null ? src.FeaturedImage.Uri : null))
            .ForMember(dest => dest.FormattedEndTime, opt => opt.MapFrom(src => FormatEndTime(src)))
            .ForMember(dest => dest.SessionGroups, opt => opt.MapFrom(src => src.SessionGroups
                .Where(assignment => assignment.EventSessionGroup.IsPublished)
                .OrderByDescending(assignment => assignment.IsPrimary)
                .ThenBy(assignment => assignment.SortOrder)));
        CreateMap<EventSession, EventSessionListDto>()
            .ForMember(dest => dest.EventTitle, opt => opt.MapFrom(src => src.Event != null ? src.Event.Title : null))
            .ForMember(dest => dest.ParentEventStatusId, opt => opt.MapFrom(src => src.Event.EventStatusId))
            .ForMember(dest => dest.EventSessionKindFullName, opt => opt.MapFrom(src => src.EventSessionKind != null ? src.EventSessionKind.FullName : null))
            .ForMember(dest => dest.EventSessionKindMasterCode, opt => opt.MapFrom(src => src.EventSessionKind != null ? src.EventSessionKind.MasterCode : null))
            .ForMember(dest => dest.EventSessionStatusFullName, opt => opt.MapFrom(src => src.EventSessionStatus != null ? src.EventSessionStatus.FullName : null))
            .ForMember(dest => dest.EventSessionStatusMasterCode, opt => opt.MapFrom(src => src.EventSessionStatus != null ? src.EventSessionStatus.MasterCode : null))
            .ForMember(dest => dest.IsScheduled, opt => opt.MapFrom(src => src.StartTime.HasValue &&
                ((src.EndTimeType == SessionEndTimeType.Fixed && src.EndTime > src.StartTime) ||
                 (src.EndTimeType == SessionEndTimeType.OpenEnded && src.EndTime == null) ||
                 (src.EndTimeType == SessionEndTimeType.RelativeToPrayer &&
                  (src.EndTime == null || src.EndTime > src.StartTime)))))
            .ForMember(dest => dest.LocationId, opt => opt.Ignore())
            .ForMember(dest => dest.LocationFullName, opt => opt.Ignore())
            .ForMember(dest => dest.LocationCity, opt => opt.Ignore())
            .ForMember(dest => dest.RoomId, opt => opt.Ignore())
            .ForMember(dest => dest.RoomName, opt => opt.Ignore())
            .ForMember(dest => dest.EventLocation, opt => opt.Ignore())
            .ForMember(dest => dest.FeaturedImageUri, opt => opt.MapFrom(src => src.FeaturedImage != null ? src.FeaturedImage.Uri : null))
            .ForMember(dest => dest.FormattedEndTime, opt => opt.MapFrom(src => FormatEndTime(src)))
            .ForMember(dest => dest.SessionGroups, opt => opt.MapFrom(src => src.SessionGroups
                .Where(assignment => assignment.EventSessionGroup.IsPublished)
                .OrderByDescending(assignment => assignment.IsPrimary)
                .ThenBy(assignment => assignment.SortOrder)));
        CreateMap<CreateEventSessionDto, EventSession>()
            .ForMember(dest => dest.StartTime, opt => opt.Ignore())
            .ForMember(dest => dest.EndTime, opt => opt.Ignore())
            .ForMember(dest => dest.IslamicAspect, opt => opt.Ignore());
        // Event Session Agenda Item
        CreateMap<EventSessionAgendaItem, EventSessionAgendaItemDto>()
            .ForMember(dest => dest.EventId, opt => opt.MapFrom(src => src.EventSession.EventId))
            .ForMember(dest => dest.EventSessionTitle, opt => opt.MapFrom(src => src.EventSession != null ? src.EventSession.Title : null))
            .ForMember(dest => dest.LocationId, opt => opt.Ignore())
            .ForMember(dest => dest.LocationFullName, opt => opt.Ignore())
            .ForMember(dest => dest.EventLocation, opt => opt.Ignore());
        CreateMap<EventSessionAgendaItem, EventSessionAgendaItemListDto>()
            .ForMember(dest => dest.EventId, opt => opt.MapFrom(src => src.EventSession.EventId))
            .ForMember(dest => dest.EventSessionTitle, opt => opt.MapFrom(src => src.EventSession != null ? src.EventSession.Title : null))
            .ForMember(dest => dest.LocationFullName, opt => opt.Ignore())
            .ForMember(dest => dest.EventLocation, opt => opt.Ignore());
        CreateMap<CreateEventSessionAgendaItemDto, EventSessionAgendaItem>();
        CreateMap<UpdateEventSessionAgendaItemDto, EventSessionAgendaItem>();

        // Event Session Speaker
        CreateMap<EventSessionSpeaker, EventSessionSpeakerDto>()
            .ForMember(dest => dest.EventId, opt => opt.MapFrom(src => src.EventSession != null ? src.EventSession.EventId : Guid.Empty))
            .ForMember(dest => dest.EventSessionTitle, opt => opt.MapFrom(src => src.EventSession != null ? src.EventSession.Title : null))
            .ForMember(dest => dest.ActorDisplayName, opt => opt.MapFrom(src => src.Actor != null ? src.Actor.DisplayName : null));
        CreateMap<EventSessionSpeaker, EventSessionSpeakerListDto>()
            .ForMember(dest => dest.EventId, opt => opt.MapFrom(src => src.EventSession != null ? src.EventSession.EventId : Guid.Empty))
            .ForMember(dest => dest.EventSessionTitle, opt => opt.MapFrom(src => src.EventSession != null ? src.EventSession.Title : null))
            .ForMember(dest => dest.ActorDisplayName, opt => opt.MapFrom(src => src.Actor != null ? src.Actor.DisplayName : null));
        CreateMap<CreateEventSessionSpeakerDto, EventSessionSpeaker>();

        // Event Session Language
        CreateMap<EventSessionLanguage, EventSessionLanguageDto>()
            .ForMember(dest => dest.EventSessionTitle, opt => opt.MapFrom(src => src.EventSession != null ? src.EventSession.Title : null))
            .ForMember(dest => dest.LanguageFullName, opt => opt.MapFrom(src => src.Language != null ? src.Language.FullName : null))
            .ForMember(dest => dest.LanguageMasterCode, opt => opt.MapFrom(src => src.Language != null ? src.Language.MasterCode : null));
        CreateMap<EventSessionLanguage, EventSessionLanguageListDto>();
        CreateMap<CreateEventSessionLanguageDto, EventSessionLanguage>();
    }

    private static string? FormatEndTime(EventSession src)
    {
        return src.EndTimeType switch
        {
            SessionEndTimeType.OpenEnded => "Open-ended",
            SessionEndTimeType.RelativeToPrayer => FormatRelativeEndTime(src.IslamicAspect),
            SessionEndTimeType.Fixed => src.LocalEndTime?.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture),
            _ => null
        };
    }

    private static string? FormatRelativeEndTime(EventSessionIslamicAspect? aspect)
    {
        if (aspect == null || !aspect.EndReferencePrayer.HasValue)
        {
            return "Relative to prayer";
        }

        var prayer = aspect.EndReferencePrayer.Value.ToString();
        var offset = aspect.EndOffsetMinutes ?? 0;

        if (offset == 0)
        {
            return $"Until {prayer} prayer";
        }

        if (offset > 0)
        {
            return $"Until {offset} minutes after {prayer}";
        }

        return $"Until {Math.Abs(offset)} minutes before {prayer}";
    }
}
