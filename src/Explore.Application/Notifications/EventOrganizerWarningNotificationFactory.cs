using Explore.Application.Contracts.Notifications;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Notifications;

public sealed class EventOrganizerWarningNotificationFactory
{
    public const string TemplateKey = "report.organizer-warning";
    public const int TemplateVersion = 1;
    public const int PolicyVersion = 1;
    public const string SourceType = "event_report_decision";
    public const string PreferenceDisabledSkipReason = "email_preference_disabled";

    public RecipientNotificationMaterialization Create(
        EventReport report,
        EventReportDecision decision,
        Guid organizerUserId,
        RecipientEmailAddressResolution emailAddress,
        bool emailPreferenceEnabled,
        Guid intentId,
        Guid inAppNotificationId,
        Guid inAppDeliveryId,
        Guid emailDeliveryId,
        Guid emailDispatchOutboxId,
        DateTime materializedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(emailAddress);
        if (organizerUserId == Guid.Empty)
        {
            throw new ArgumentException("An organizer user is required.", nameof(organizerUserId));
        }

        if (decision.TenantId != report.TenantId
            || decision.ReportId != report.Id
            || decision.DecisionKind != EventReportDecisionKind.WarnOrganizer)
        {
            throw new InvalidOperationException("An organizer warning requires the matching WarnOrganizer decision.");
        }

        const string title = "Action required for an event you manage";
        const string body = "A trust and safety review found that an event you manage requires attention. Review the event and make any needed corrections.";
        bool emailEligible = emailPreferenceEnabled && emailAddress.HasVerifiedEmail;
        EmailDispatchOutbox? email = emailEligible
            ? new EmailDispatchOutbox
            {
                Id = emailDispatchOutboxId,
                TenantId = report.TenantId,
                Kind = EmailDispatchKind.OrganizerNotification,
                SourceType = SourceType,
                SourceId = decision.Id,
                EventId = report.EventId,
                RecipientUserId = organizerUserId,
                RecipientAddressSource = RecipientAddressSource.TenantUserVerifiedEmail,
                RecipientEmail = emailAddress.Email!,
                Subject = title,
                PlainTextBody = $"Assalamu alaykum,\n\n{body}\n\nEvent Platform",
                HtmlBody = $"<p>Assalamu alaykum,</p><p>{body}</p><p>Event Platform</p>",
                CorrelationId = $"{decision.Id:D}:{organizerUserId:D}",
                CreatedAt = materializedAtUtc
            }
            : null;

        string deduplicationKey = $"event-report-decision:{decision.Id:N}:organizer-warning:{organizerUserId:N}";
        return new RecipientNotificationMaterialization(
            intentId,
            new NotificationIntentDraft(
                NotificationCategory.TrustSafetyModeration,
                TenantId: report.TenantId,
                RecipientKind: nameof(NotificationRecipientKindEnum.Organizer),
                TemplateKey: TemplateKey,
                SafePayloadReference: $"event-report-decision:{decision.Id:D}:organizer-warning:v{TemplateVersion}",
                DeduplicationKey: deduplicationKey,
                CorrelationId: decision.Id.ToString("D"),
                UserId: organizerUserId,
                EventId: report.EventId,
                ReportId: report.Id,
                ReportDecisionId: decision.Id),
            NotificationDeliveryPolicyEnum.ModerationContextOptional,
            "moderation_context",
            new RecipientInAppNotificationDraft(
                (int)NotificationTypeEnum.General,
                title,
                body,
                (int)ActorTypeEnum.User,
                (int)NotificationReasonEnum.System,
                IsRequired: true),
            email,
            IncludeEmailChannel: true,
            EmailRequired: false,
            EmailSkipReason: ResolveEmailSkipReason(emailAddress, emailPreferenceEnabled),
            PreferenceCategoryCode: NotificationPreferenceCategoryCodes.TrustSafety,
            EmailPreferenceEnabled: emailPreferenceEnabled,
            PolicyVersion: PolicyVersion,
            TemplateVersion: TemplateVersion,
            LinkAllowed: false,
            InAppNotificationId: inAppNotificationId,
            InAppDeliveryId: inAppDeliveryId,
            EmailDeliveryId: emailDeliveryId,
            MaterializedAt: materializedAtUtc);
    }

    private static string? ResolveEmailSkipReason(
        RecipientEmailAddressResolution emailAddress,
        bool emailPreferenceEnabled)
    {
        return emailPreferenceEnabled
            ? emailAddress.SkipReason
            : PreferenceDisabledSkipReason;
    }
}
