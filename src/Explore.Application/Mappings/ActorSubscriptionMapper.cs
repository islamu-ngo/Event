using Explore.Application.DTOs.ActorSubscription;
using Explore.Domain;
using Riok.Mapperly.Abstractions;

namespace Explore.Application.Mappings;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Both, AutoUserMappings = false)]
public static partial class ActorSubscriptionMapper
{
    // Detail discloses subscriber identifiers, but never identity graphs or audit/deletion state.
    [MapperIgnoreSource(nameof(ActorSubscription.Tenant))]
    [MapperIgnoreSource(nameof(ActorSubscription.SubscriberTenantUser))]
    [MapperIgnoreSource(nameof(ActorSubscription.SubscriberUser))]
    [MapperIgnoreSource(nameof(ActorSubscription.CreatedAt))]
    [MapperIgnoreSource(nameof(ActorSubscription.CreatedBy))]
    [MapperIgnoreSource(nameof(ActorSubscription.UpdatedAt))]
    [MapperIgnoreSource(nameof(ActorSubscription.UpdatedBy))]
    [MapperIgnoreSource(nameof(ActorSubscription.IsDeleted))]
    [MapperIgnoreSource(nameof(ActorSubscription.DeletedAt))]
    [MapperIgnoreSource(nameof(ActorSubscription.DeletedBy))]
    [MapProperty(nameof(ActorSubscription.TargetActorType), nameof(ActorSubscriptionDto.TargetActorTypeName), Use = nameof(ActorTypeName))]
    [MapProperty(nameof(ActorSubscription.TargetActor), nameof(ActorSubscriptionDto.TargetActorName), Use = nameof(ActorName))]
    [MapProperty(nameof(ActorSubscription.Status), nameof(ActorSubscriptionDto.StatusCode), Use = nameof(StatusCode))]
    [MapProperty(nameof(ActorSubscription.Status), nameof(ActorSubscriptionDto.StatusName), Use = nameof(StatusName))]
    [MapProperty(nameof(ActorSubscription.NotificationLevel), nameof(ActorSubscriptionDto.NotificationLevelCode), Use = nameof(NotificationLevelCode))]
    [MapProperty(nameof(ActorSubscription.NotificationLevel), nameof(ActorSubscriptionDto.NotificationLevelName), Use = nameof(NotificationLevelName))]
    public static partial ActorSubscriptionDto ToDetail(ActorSubscription source);

    // List additionally omits both subscriber identifiers; lookup helpers select labels, not graphs.
    [MapperIgnoreSource(nameof(ActorSubscription.SubscriberTenantUserId))]
    [MapperIgnoreSource(nameof(ActorSubscription.SubscriberUserId))]
    [MapperIgnoreSource(nameof(ActorSubscription.Tenant))]
    [MapperIgnoreSource(nameof(ActorSubscription.SubscriberTenantUser))]
    [MapperIgnoreSource(nameof(ActorSubscription.SubscriberUser))]
    [MapperIgnoreSource(nameof(ActorSubscription.CreatedAt))]
    [MapperIgnoreSource(nameof(ActorSubscription.CreatedBy))]
    [MapperIgnoreSource(nameof(ActorSubscription.UpdatedAt))]
    [MapperIgnoreSource(nameof(ActorSubscription.UpdatedBy))]
    [MapperIgnoreSource(nameof(ActorSubscription.IsDeleted))]
    [MapperIgnoreSource(nameof(ActorSubscription.DeletedAt))]
    [MapperIgnoreSource(nameof(ActorSubscription.DeletedBy))]
    [MapProperty(nameof(ActorSubscription.TargetActorType), nameof(ActorSubscriptionListDto.TargetActorTypeName), Use = nameof(ActorTypeName))]
    [MapProperty(nameof(ActorSubscription.TargetActor), nameof(ActorSubscriptionListDto.TargetActorName), Use = nameof(ActorName))]
    [MapProperty(nameof(ActorSubscription.Status), nameof(ActorSubscriptionListDto.StatusCode), Use = nameof(StatusCode))]
    [MapProperty(nameof(ActorSubscription.Status), nameof(ActorSubscriptionListDto.StatusName), Use = nameof(StatusName))]
    [MapProperty(nameof(ActorSubscription.NotificationLevel), nameof(ActorSubscriptionListDto.NotificationLevelCode), Use = nameof(NotificationLevelCode))]
    [MapProperty(nameof(ActorSubscription.NotificationLevel), nameof(ActorSubscriptionListDto.NotificationLevelName), Use = nameof(NotificationLevelName))]
    public static partial ActorSubscriptionListDto ToListItem(ActorSubscription source);

    // Required navigation annotations do not guarantee that a query loaded the navigation.
    private static string? ActorTypeName(ActorType? type) => type?.FullName;
    private static string? ActorName(Actor? actor) => actor?.Pii?.DisplayName;
    private static string? StatusCode(ActorSubscriptionStatus? status) => status?.MasterCode;
    private static string? StatusName(ActorSubscriptionStatus? status) => status?.FullName;
    private static string? NotificationLevelCode(ActorSubscriptionNotificationLevel? level) => level?.MasterCode;
    private static string? NotificationLevelName(ActorSubscriptionNotificationLevel? level) => level?.FullName;
}
