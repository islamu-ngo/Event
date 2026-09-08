using AutoMapper;
using Explore.Application.DTOs.ActorSubscription;
using Explore.Domain;

namespace Explore.Application.Profiles;

public class ActorSubscriptionMappingProfile : Profile
{
    public ActorSubscriptionMappingProfile()
    {
        CreateMap<ActorSubscription, ActorSubscriptionDto>()
            .ForMember(dto => dto.TargetActorTypeName, opt => opt.MapFrom(subscription => subscription.TargetActorType != null ? subscription.TargetActorType.FullName : null))
            .ForMember(dto => dto.TargetActorName, opt => opt.MapFrom(subscription => subscription.TargetActor != null && subscription.TargetActor.Pii != null ? subscription.TargetActor.Pii.DisplayName : null))
            .ForMember(dto => dto.StatusCode, opt => opt.MapFrom(subscription => subscription.Status != null ? subscription.Status.MasterCode : null))
            .ForMember(dto => dto.StatusName, opt => opt.MapFrom(subscription => subscription.Status != null ? subscription.Status.FullName : null))
            .ForMember(dto => dto.NotificationLevelCode, opt => opt.MapFrom(subscription => subscription.NotificationLevel != null ? subscription.NotificationLevel.MasterCode : null))
            .ForMember(dto => dto.NotificationLevelName, opt => opt.MapFrom(subscription => subscription.NotificationLevel != null ? subscription.NotificationLevel.FullName : null));

        CreateMap<ActorSubscription, ActorSubscriptionListDto>()
            .ForMember(dto => dto.TargetActorTypeName, opt => opt.MapFrom(subscription => subscription.TargetActorType != null ? subscription.TargetActorType.FullName : null))
            .ForMember(dto => dto.TargetActorName, opt => opt.MapFrom(subscription => subscription.TargetActor != null && subscription.TargetActor.Pii != null ? subscription.TargetActor.Pii.DisplayName : null))
            .ForMember(dto => dto.StatusCode, opt => opt.MapFrom(subscription => subscription.Status != null ? subscription.Status.MasterCode : null))
            .ForMember(dto => dto.StatusName, opt => opt.MapFrom(subscription => subscription.Status != null ? subscription.Status.FullName : null))
            .ForMember(dto => dto.NotificationLevelCode, opt => opt.MapFrom(subscription => subscription.NotificationLevel != null ? subscription.NotificationLevel.MasterCode : null))
            .ForMember(dto => dto.NotificationLevelName, opt => opt.MapFrom(subscription => subscription.NotificationLevel != null ? subscription.NotificationLevel.FullName : null));
    }
}
