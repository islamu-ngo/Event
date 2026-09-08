using Explore.Application.Contracts.Notifications;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Notifications;

public sealed class ReportNeedsMoreInformationNotificationFactory
{
    public const string TemplateKey = "report.needs-more-information";
    public const int TemplateVersion = 1;
    public const int PolicyVersion = 1;
    public const string SourceType = "event_report_decision";
    public const string ConsentNotGrantedSkipReason = "report_follow_up_contact_consent_not_granted";
    public const string PreferenceDisabledSkipReason = "email_preference_disabled";

    public RecipientNotificationMaterialization Create(
        EventReport report,
        EventReportDecision decision,
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
        if (report.ReporterUserId is not { } reporterUserId || reporterUserId == Guid.Empty)
        {
            throw new InvalidOperationException("A persisted reporter user is required for follow-up notification materialization.");
        }

        if (decision.TenantId != report.TenantId
            || decision.ReportId != report.Id
            || decision.DecisionKind != EventReportDecisionKind.NeedsMoreInfo)
        {
            throw new InvalidOperationException("A reporter follow-up requires the matching NeedsMoreInfo decision.");
        }

        const string title = "More information is needed for your report";
        const string body = "We need more information before we can continue reviewing your report. This is a case update, not a final decision.";
        bool emailEligible = report.ReportFollowUpContactConsent
            && emailPreferenceEnabled
            && emailAddress.HasVerifiedEmail;
        EmailDispatchOutbox? email = emailEligible
            ? new EmailDispatchOutbox
            {
                Id = emailDispatchOutboxId,
                TenantId = report.TenantId,
                Kind = EmailDispatchKind.ReportNeedsMoreInformation,
                SourceType = SourceType,
                SourceId = decision.Id,
                EventId = report.EventId,
                RecipientUserId = reporterUserId,
                RecipientAddressSource = RecipientAddressSource.TenantUserVerifiedEmail,
                RecipientEmail = emailAddress.Email!,
                Subject = title,
                PlainTextBody = $"Assalamu alaykum,\n\n{body}\n\nEvent Platform",
                HtmlBody = $"<p>Assalamu alaykum,</p><p>{body}</p><p>Event Platform</p>",
                CorrelationId = decision.Id.ToString("D"),
                CreatedAt = materializedAtUtc
            }
            : null;

        string deduplicationKey = $"event-report-decision:{decision.Id:N}:reporter-needs-more-information";
        return new RecipientNotificationMaterialization(
            intentId,
            new NotificationIntentDraft(
                NotificationCategory.TrustSafetyReporting,
                TenantId: report.TenantId,
                RecipientKind: nameof(NotificationRecipientKindEnum.Reporter),
                TemplateKey: TemplateKey,
                SafePayloadReference: $"event-report-decision:{decision.Id:D}:needs-more-information:v{TemplateVersion}",
                DeduplicationKey: deduplicationKey,
                CorrelationId: decision.Id.ToString("D"),
                UserId: reporterUserId,
                EventId: report.EventId,
                ReportId: report.Id,
                ReportDecisionId: decision.Id),
            NotificationDeliveryPolicyEnum.ReportFollowUpContact,
            "report_follow_up_contact",
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
            EmailSkipReason: ResolveEmailSkipReason(report, emailAddress, emailPreferenceEnabled),
            PreferenceCategoryCode: NotificationPreferenceCategoryCodes.TrustSafety,
            EmailPreferenceEnabled: emailPreferenceEnabled,
            ConsentPurpose: ReportEmailConsentPurposeCodes.FollowUpContact,
            ConsentVersion: 1,
            PolicyVersion: PolicyVersion,
            TemplateVersion: TemplateVersion,
            LinkAllowed: false,
            InAppNotificationId: inAppNotificationId,
            InAppDeliveryId: inAppDeliveryId,
            EmailDeliveryId: emailDeliveryId,
            MaterializedAt: materializedAtUtc);
    }

    private static string? ResolveEmailSkipReason(
        EventReport report,
        RecipientEmailAddressResolution emailAddress,
        bool emailPreferenceEnabled)
    {
        if (!report.ReportFollowUpContactConsent)
        {
            return ConsentNotGrantedSkipReason;
        }

        if (!emailPreferenceEnabled)
        {
            return PreferenceDisabledSkipReason;
        }

        return emailAddress.SkipReason;
    }
}
