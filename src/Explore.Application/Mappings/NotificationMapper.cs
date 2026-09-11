using Explore.Application.DTOs.Notification;
using Explore.Domain;
using Riok.Mapperly.Abstractions;

namespace Explore.Application.Mappings;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Both, AutoUserMappings = false)]
public static partial class NotificationMapper
{
    // Detail exposes recipient identifiers and creation time, not intent, identity graphs or deletion state.
    [MapperIgnoreSource(nameof(Notification.NotificationIntentId))]
    [MapperIgnoreSource(nameof(Notification.NotificationIntent))]
    [MapperIgnoreSource(nameof(Notification.User))]
    [MapperIgnoreSource(nameof(Notification.Tenant))]
    [MapperIgnoreSource(nameof(Notification.DeduplicationKey))]
    [MapperIgnoreSource(nameof(Notification.CreatedBy))]
    [MapperIgnoreSource(nameof(Notification.UpdatedAt))]
    [MapperIgnoreSource(nameof(Notification.UpdatedBy))]
    [MapperIgnoreSource(nameof(Notification.IsDeleted))]
    [MapperIgnoreSource(nameof(Notification.DeletedAt))]
    [MapperIgnoreSource(nameof(Notification.DeletedBy))]
    [MapProperty(nameof(Notification.NotificationType), nameof(NotificationDto.NotificationTypeName), Use = nameof(TypeName))]
    [MapProperty(nameof(Notification.NotificationEntityType), nameof(NotificationDto.NotificationEntityTypeName), Use = nameof(EntityTypeName))]
    [MapProperty(nameof(Notification.NotificationScope), nameof(NotificationDto.NotificationScopeName), Use = nameof(ScopeName))]
    [MapProperty(nameof(Notification.NotificationReason), nameof(NotificationDto.NotificationReasonName), Use = nameof(ReasonName))]
    [MapProperty(nameof(Notification.SourceActor), nameof(NotificationDto.SourceActorName), Use = nameof(ActorName))]
    [MapProperty(nameof(Notification.RecipientContextActor), nameof(NotificationDto.RecipientContextActorName), Use = nameof(ActorName))]
    public static partial NotificationDto ToDetail(Notification source);

    // The collection contract additionally omits the owning user and tenant identifiers.
    [MapperIgnoreSource(nameof(Notification.UserId))]
    [MapperIgnoreSource(nameof(Notification.TenantId))]
    [MapperIgnoreSource(nameof(Notification.NotificationIntentId))]
    [MapperIgnoreSource(nameof(Notification.NotificationIntent))]
    [MapperIgnoreSource(nameof(Notification.User))]
    [MapperIgnoreSource(nameof(Notification.Tenant))]
    [MapperIgnoreSource(nameof(Notification.DeduplicationKey))]
    [MapperIgnoreSource(nameof(Notification.CreatedBy))]
    [MapperIgnoreSource(nameof(Notification.UpdatedAt))]
    [MapperIgnoreSource(nameof(Notification.UpdatedBy))]
    [MapperIgnoreSource(nameof(Notification.IsDeleted))]
    [MapperIgnoreSource(nameof(Notification.DeletedAt))]
    [MapperIgnoreSource(nameof(Notification.DeletedBy))]
    [MapProperty(nameof(Notification.NotificationType), nameof(NotificationListDto.NotificationTypeName), Use = nameof(TypeName))]
    [MapProperty(nameof(Notification.NotificationEntityType), nameof(NotificationListDto.NotificationEntityTypeName), Use = nameof(EntityTypeName))]
    [MapProperty(nameof(Notification.NotificationScope), nameof(NotificationListDto.NotificationScopeName), Use = nameof(ScopeName))]
    [MapProperty(nameof(Notification.NotificationReason), nameof(NotificationListDto.NotificationReasonName), Use = nameof(ReasonName))]
    [MapProperty(nameof(Notification.SourceActor), nameof(NotificationListDto.SourceActorName), Use = nameof(ActorName))]
    [MapProperty(nameof(Notification.RecipientContextActor), nameof(NotificationListDto.RecipientContextActorName), Use = nameof(ActorName))]
    public static partial NotificationListDto ToListItem(Notification source);

    private static string? TypeName(NotificationType? type) => type?.FullName;
    private static string? EntityTypeName(NotificationEntityType? type) => type?.FullName;
    private static string? ScopeName(NotificationScopeType? scope) => scope?.FullName;
    private static string? ReasonName(NotificationReason? reason) => reason?.FullName;
    private static string? ActorName(Actor? actor) => actor?.Pii?.DisplayName;
}
