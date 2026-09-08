using System.Globalization;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Features.EventReporting;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Notifications;

public sealed class ReportReceiptNotificationFactory
{
    public const string TemplateKey = "report.receipt";
    public const int TemplateVersion = 1;
    public const int PolicyVersion = 1;
    public const string SourceType = "event_report";
    public const string ConsentNotGrantedSkipReason = "report_case_update_consent_not_granted";
    public const string PreferenceDisabledSkipReason = "email_preference_disabled";

    public RecipientNotificationMaterialization Create(
        EventReport report,
        int caseSlaHours,
        RecipientEmailAddressResolution emailAddress,
        bool emailPreferenceEnabled,
        Guid intentId,
        Guid inAppNotificationId,
        Guid inAppDeliveryId,
        Guid emailDeliveryId,
        Guid emailDispatchOutboxId,
        DateTime materializedAt)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(emailAddress);
        if (report.ReporterUserId is not Guid reporterUserId || reporterUserId == Guid.Empty)
        {
            throw new InvalidOperationException("A persisted reporter user is required for receipt notification materialization.");
        }

        if (caseSlaHours is < EventReportSubmissionOptions.MinCaseSlaHours
            or > EventReportSubmissionOptions.MaxCaseSlaHours)
        {
            throw new ArgumentOutOfRangeException(nameof(caseSlaHours));
        }

        string slaHours = caseSlaHours.ToString(CultureInfo.InvariantCulture);
        string title = "We received your event report";
        string body = $"Thank you for reporting an event. We normally review reports within {slaHours} hours.";
        bool emailEligible = report.ReportCaseUpdatesConsent
            && emailPreferenceEnabled
            && emailAddress.HasVerifiedEmail;
        EmailDispatchOutbox? email = emailEligible
            ? new EmailDispatchOutbox
            {
                Id = emailDispatchOutboxId,
                TenantId = report.TenantId,
                Kind = EmailDispatchKind.ReportReceipt,
                SourceType = SourceType,
                SourceId = report.Id,
                EventId = report.EventId,
                RecipientUserId = reporterUserId,
                RecipientAddressSource = RecipientAddressSource.TenantUserVerifiedEmail,
                RecipientEmail = emailAddress.Email!,
                Subject = title,
                PlainTextBody = $"Assalamu alaykum,\n\n{body}\n\nEvent Platform",
                HtmlBody = $"<p>Assalamu alaykum,</p><p>{body}</p><p>Event Platform</p>",
                CorrelationId = report.Id.ToString("D"),
                CreatedAt = materializedAt
            }
            : null;

        string deduplicationKey = $"event-report:{report.Id:N}:receipt";
        return new RecipientNotificationMaterialization(
            intentId,
            new NotificationIntentDraft(
                NotificationCategory.TrustSafetyReporting,
                TenantId: report.TenantId,
                RecipientKind: nameof(NotificationRecipientKindEnum.Reporter),
                TemplateKey: TemplateKey,
                SafePayloadReference: $"event-report:{report.Id:D}:receipt:v{TemplateVersion}:sla-hours:{slaHours}",
                DeduplicationKey: deduplicationKey,
                CorrelationId: report.Id.ToString("D"),
                UserId: reporterUserId,
                EventId: report.EventId,
                ReportId: report.Id),
            NotificationDeliveryPolicyEnum.ReportCaseUpdate,
            "report_case_update",
            new RecipientInAppNotificationDraft(
                (int)NotificationTypeEnum.General,
                title,
                body,
                (int)ActorTypeEnum.User,
                (int)NotificationReasonEnum.System),
            email,
            IncludeEmailChannel: true,
            EmailRequired: false,
            EmailSkipReason: ResolveEmailSkipReason(report, emailAddress, emailPreferenceEnabled),
            PreferenceCategoryCode: NotificationPreferenceCategoryCodes.TrustSafety,
            EmailPreferenceEnabled: emailPreferenceEnabled,
            ConsentPurpose: ReportEmailConsentPurposeCodes.CaseUpdates,
            ConsentVersion: 1,
            PolicyVersion: PolicyVersion,
            TemplateVersion: TemplateVersion,
            LinkAllowed: false,
            InAppNotificationId: inAppNotificationId,
            InAppDeliveryId: inAppDeliveryId,
            EmailDeliveryId: emailDeliveryId,
            MaterializedAt: materializedAt);
    }

    private static string? ResolveEmailSkipReason(
        EventReport report,
        RecipientEmailAddressResolution emailAddress,
        bool emailPreferenceEnabled)
    {
        if (!report.ReportCaseUpdatesConsent)
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
