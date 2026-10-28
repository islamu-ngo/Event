using Explore.Application.Contracts.Notifications;
using Explore.Application.Notifications;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Services.Moderation;

/// <summary>
/// Required, content-free native manager notification, committed with the escalation receipt.
/// No email, private allegation, original template, contest clock or payload access is involved.
/// </summary>
public sealed class ModerationResourceReviewEscalationMaterializer(
    INotificationOwnershipResolver ownership, IRecipientNotificationMaterializer materializer)
{
    public async Task<bool> MaterializeAsync(
        EventResourceModerationReviewEscalation escalation, Guid recipientId, CancellationToken token)
    {
        escalation.ValidatePersistedState();
        var draft = new NotificationIntentDraft(Explore.Application.Notifications.NotificationCategory.TrustSafetyModeration,
            TenantId: escalation.TenantId, UserId: recipientId, RecipientKind: "Moderator",
            TemplateKey: "moderation.resource-review-overdue.v1",
            SafePayloadReference: $"moderation.resource-review-overdue:{escalation.Id:N}",
            DeduplicationKey: $"resource-review-overdue:{escalation.Id:N}:{recipientId:N}");
        if (!(await ownership.ResolveAsync(draft, token)).IsLocalIslamuDelivery) return false;
        var result = await materializer.MaterializeInCurrentTransactionAsync(new(
            Guid.CreateVersion7(), draft, NotificationDeliveryPolicyEnum.TenantAdministrationRequired,
            "content_free_operational_duty",
            new RecipientInAppNotificationDraft((int)NotificationTypeEnum.General,
                "Resource review overdue", "An object review requires an independently qualified reviewer. Quarantine remains in force.",
                (int)ActorTypeEnum.User, (int)NotificationReasonEnum.System),
            Email: null, IncludeEmailChannel: false, EmailRequired: false,
            LinkAllowed: false, MaterializedAt: escalation.EscalatedAt.UtcDateTime), token);
        return !result.IsSkipped && result.Notification is not null;
    }
}
