using AutoMapper;
using Explore.Domain;

namespace Explore.Application.Profiles;

public class NotificationMappingProfile : Profile
{
    public NotificationMappingProfile()
    {
        CreateMap<Notification, DTOs.Notification.NotificationDto>()
            .ForMember(d => d.NotificationTypeName, opt => opt.MapFrom(s => s.NotificationType != null ? s.NotificationType.FullName : null))
            .ForMember(d => d.NotificationEntityTypeName, opt => opt.MapFrom(s => s.NotificationEntityType != null ? s.NotificationEntityType.FullName : null))
            .ForMember(d => d.NotificationScopeName, opt => opt.MapFrom(s => s.NotificationScope != null ? s.NotificationScope.FullName : null))
            .ForMember(d => d.NotificationReasonName, opt => opt.MapFrom(s => s.NotificationReason != null ? s.NotificationReason.FullName : null))
            .ForMember(d => d.SourceActorName, opt => opt.MapFrom(s => s.SourceActor != null && s.SourceActor.Pii != null ? s.SourceActor.Pii.DisplayName : null))
            .ForMember(d => d.RecipientContextActorName, opt => opt.MapFrom(s => s.RecipientContextActor != null && s.RecipientContextActor.Pii != null ? s.RecipientContextActor.Pii.DisplayName : null));
        CreateMap<Notification, DTOs.Notification.NotificationListDto>()
            .ForMember(d => d.NotificationTypeName, opt => opt.MapFrom(s => s.NotificationType != null ? s.NotificationType.FullName : null))
            .ForMember(d => d.NotificationEntityTypeName, opt => opt.MapFrom(s => s.NotificationEntityType != null ? s.NotificationEntityType.FullName : null))
            .ForMember(d => d.NotificationScopeName, opt => opt.MapFrom(s => s.NotificationScope != null ? s.NotificationScope.FullName : null))
            .ForMember(d => d.NotificationReasonName, opt => opt.MapFrom(s => s.NotificationReason != null ? s.NotificationReason.FullName : null))
            .ForMember(d => d.SourceActorName, opt => opt.MapFrom(s => s.SourceActor != null && s.SourceActor.Pii != null ? s.SourceActor.Pii.DisplayName : null))
            .ForMember(d => d.RecipientContextActorName, opt => opt.MapFrom(s => s.RecipientContextActor != null && s.RecipientContextActor.Pii != null ? s.RecipientContextActor.Pii.DisplayName : null));

        CreateMap<CustomPropertyProjectionStatus, DTOs.CustomPropertyProjection.ProjectionStatusDto>();
        CreateMap<CustomPropertyProjectionDirtyScope, DTOs.CustomPropertyProjection.ProjectionDirtyScopeDto>();
        CreateMap<EventCustomPropertyProjection, DTOs.CustomPropertyProjection.EventCustomPropertyProjectionDto>();
        CreateMap<EventSessionCustomPropertyProjection, DTOs.CustomPropertyProjection.EventSessionCustomPropertyProjectionDto>();
    }
}
