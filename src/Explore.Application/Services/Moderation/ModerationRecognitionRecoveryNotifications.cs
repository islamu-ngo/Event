using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Notifications;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Services.Moderation;

public sealed class ModerationRecognitionRecoveryNotifications(
    INotificationOwnershipResolver ownership, IRecipientNotificationMaterializer materializer)
    : IModerationRecognitionRecoveryNotifications
{
    public async Task<Guid?> DeliverAsync(ModerationRecognitionEscalation escalation, Guid tenantId,
        CancellationToken token)
    {
        escalation.ValidatePersistedState();
        var draft = new NotificationIntentDraft(Notifications.NotificationCategory.PlatformOperations,
            TenantId: tenantId, UserId: escalation.RecipientHumanId, RecipientKind: "Moderator",
            TemplateKey: "moderation.recognition-recovery.owner.v1",
            SafePayloadReference: $"recognition-recovery:{escalation.Id:D}",
            DeduplicationKey: $"recognition-recovery:{escalation.Id:N}");
        if (!(await ownership.ResolveAsync(draft, token)).IsLocalIslamuDelivery) return null;
        var result = await materializer.MaterializeInCurrentTransactionAsync(new(
            Guid.CreateVersion7(), draft, NotificationDeliveryPolicyEnum.TenantAdministrationRequired,
            "recognition_recovery_owner_pointer",
            new RecipientInAppNotificationDraft((int)NotificationTypeEnum.General,
                "Privacy recovery requires owner action", null, (int)ActorTypeEnum.User,
                (int)NotificationReasonEnum.System, EntityId: escalation.Id.ToString("D")),
            Email: null, IncludeEmailChannel: false, EmailRequired: false,
            MaterializedAt: escalation.RequiredAt.UtcDateTime), token);
        return result.IsSkipped ? null : result.Notification?.Id;
    }
}
