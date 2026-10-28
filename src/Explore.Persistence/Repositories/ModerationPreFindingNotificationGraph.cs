using Explore.Application.Notifications;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Persistence.Repositories;

/// <summary>Graph shape only; the caller independently owns the original private case custody.</summary>
internal static class ModerationPreFindingNotificationGraph
{
    internal static bool Matches(NotificationIntent intent, ModerationPreFindingNotice notice)
    {
        var key = ModerationPreFindingNoticeTemplate.DeduplicationKey(notice.Id, notice.RecipientHumanId);
        if (intent.TenantId != notice.TenantId || intent.RecipientUserId != notice.RecipientHumanId
            || intent.TemplateKey != ModerationPreFindingNoticeTemplate.Key || intent.DeduplicationKey != key
            || intent.SafePayloadReference != ModerationPreFindingNoticeTemplate.Reference(notice.Id)
            || intent.SafePayloadHash is not null || intent.CorrelationId is not null
            || intent.CategoryId != (int)NotificationCategoryEnum.TrustSafetyModeration
            || intent.OwnershipTypeId != (int)NotificationOwnershipTypeEnum.IslamuEvent
            || intent.RecipientKindId != (int)NotificationRecipientKindEnum.User
            || intent.StatusId != (int)NotificationIntentStatusEnum.Resolved
            || intent.IsDeleted || intent.CreatedAt != notice.IssuedAt.UtcDateTime
            || intent.FanoutOccurrenceId is not null || intent.EventId is not null
            || intent.ReportId is not null || intent.ReportDecisionId is not null
            || intent.Deliveries.Count != 1 || intent.ExternalDelegations.Count != 0)
            return false;
        var delivery = intent.Deliveries.Single();
        var notification = delivery.Notification;
        return delivery.TenantId == notice.TenantId && delivery.NotificationIntentId == intent.Id
            && delivery.ChannelId == (int)NotificationPreferenceChannelEnum.InApp
            && delivery.DeliveryPolicyId == (int)NotificationDeliveryPolicyEnum.ModerationAvailabilityRequired
            && delivery.IsRequired && delivery.StatusId == (int)NotificationDeliveryStatusEnum.Delivered
            && delivery.TemplateKey == ModerationPreFindingNoticeTemplate.Key
            && delivery.TemplateVersion == 1 && delivery.PolicyVersion == 1
            && delivery.DisclosureLevel == "moderation_pre_finding_pointer"
            && !delivery.LinkAllowed && delivery.EmailDispatchOutboxId is null
            && delivery.NotificationId == notification?.Id && notification is not null
            && delivery.CreatedAt == notice.IssuedAt.UtcDateTime
            && notification.TenantId == notice.TenantId && notification.UserId == notice.RecipientHumanId
            && notification.NotificationIntentId == intent.Id && !notification.IsDeleted
            && notification.Title == ModerationPreFindingNoticeTemplate.Title && notification.Body is null
            && notification.EntityId == notice.Id.ToString("D") && notification.DeduplicationKey == key + ":in-app"
            && notification.NotificationEntityTypeId is null
            && notification.NotificationTypeId == (int)NotificationTypeEnum.General
            && notification.NotificationScopeId == (int)ActorTypeEnum.User
            && notification.NotificationReasonId == (int)NotificationReasonEnum.System
            && notification.CreatedAt == notice.IssuedAt.UtcDateTime;
    }
}
