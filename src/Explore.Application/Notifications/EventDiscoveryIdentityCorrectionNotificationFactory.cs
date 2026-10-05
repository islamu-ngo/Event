using Explore.Application.Contracts.Notifications;
using Explore.Application.Features.Events.Discovery.Commands;
using Explore.Domain.Enums;

namespace Explore.Application.Notifications;

public sealed class EventDiscoveryIdentityCorrectionNotificationFactory
{
    public const string TemplateKey = "event.discovery-identity-correction";
    public const int TemplateVersion = 1;
    public const int PolicyVersion = 1;

    public RecipientNotificationMaterialization Create(
        Guid outboxMessageId,
        EventDiscoveryIdentityCorrectionRequested request,
        Guid recipientUserId,
        Guid recipientEventId,
        DateTime materializedAtUtc)
    {
        if (outboxMessageId == Guid.Empty
            || recipientUserId == Guid.Empty
            || recipientEventId == Guid.Empty)
        {
            throw new ArgumentException("Correction notification identifiers must be non-empty.");
        }

        string body = request.Decision switch
        {
            "same-offering" =>
                "A discovery identity review changed how an event you manage appears in discovery.",
            "different-offering" =>
                "A discovery identity review kept an event you manage separate in discovery.",
            "reverse" =>
                "A discovery identity correction was reversed for an event you manage.",
            _ => throw new InvalidOperationException("Unsupported discovery identity correction decision.")
        };
        body += $" Your original event and attendee commitments remain unchanged. Reason code: {request.ReasonCode}.";

        string deduplicationKey =
            $"event-discovery-identity:{request.TenantId:N}:{outboxMessageId:N}:{recipientEventId:N}:{recipientUserId:N}";
        return new RecipientNotificationMaterialization(
            Guid.CreateVersion7(),
            new NotificationIntentDraft(
                NotificationCategory.EventLifecycle,
                TenantId: request.TenantId,
                RecipientKind: nameof(NotificationRecipientKindEnum.Organizer),
                TemplateKey: TemplateKey,
                SafePayloadReference:
                    $"event-discovery-identity:{recipientEventId:D}:revision:{request.Revision}",
                DeduplicationKey: deduplicationKey,
                CorrelationId: outboxMessageId.ToString("D"),
                UserId: recipientUserId,
                EventId: recipientEventId),
            NotificationDeliveryPolicyEnum.CriticalEventUpdateOptional,
            "own_event_correction",
            new RecipientInAppNotificationDraft(
                (int)NotificationTypeEnum.EventUpdated,
                "Discovery listing review completed",
                body,
                (int)ActorTypeEnum.User,
                (int)NotificationReasonEnum.System,
                (int)NotificationEntityTypeEnum.Event,
                recipientEventId.ToString("D"),
                IsRequired: true),
            Email: null,
            IncludeEmailChannel: false,
            EmailRequired: false,
            PolicyVersion: PolicyVersion,
            TemplateVersion: TemplateVersion,
            LinkAllowed: true,
            MaterializedAt: materializedAtUtc);
    }
}
