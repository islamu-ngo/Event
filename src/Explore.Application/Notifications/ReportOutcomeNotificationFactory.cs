using Explore.Application.Contracts.Notifications;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Notifications;

public sealed class ReportOutcomeNotificationFactory
{
    public const string TemplateKey = "report.outcome";
    public const int TemplateVersion = 1;
    public const int PolicyVersion = 1;
    public const string SourceType = "event_report_decision";
    public const string ConsentNotGrantedSkipReason = "report_case_update_consent_not_granted";
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
            throw new InvalidOperationException("A persisted reporter user is required for outcome notification materialization.");
        }

        if (decision.TenantId != report.TenantId || decision.ReportId != report.Id)
        {
            throw new InvalidOperationException("The outcome decision must belong to the report and tenant.");
        }

        (string title, string body, string payloadOutcome) = ResolveCopy(decision.DecisionKind);
        bool emailEligible = report.ReportCaseUpdatesConsent
            && emailPreferenceEnabled
            && emailAddress.HasVerifiedEmail;
        EmailDispatchOutbox? email = emailEligible
            ? new EmailDispatchOutbox
            {
                Id = emailDispatchOutboxId,
                TenantId = report.TenantId,
                Kind = EmailDispatchKind.ReportOutcome,
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

        string deduplicationKey = $"event-report-decision:{decision.Id:N}:reporter-outcome";
        return new RecipientNotificationMaterialization(
            intentId,
            new NotificationIntentDraft(
                NotificationCategory.TrustSafetyReporting,
                TenantId: report.TenantId,
                RecipientKind: nameof(NotificationRecipientKindEnum.Reporter),
                TemplateKey: TemplateKey,
                SafePayloadReference: $"event-report-decision:{decision.Id:D}:outcome:{payloadOutcome}:v{TemplateVersion}",
                DeduplicationKey: deduplicationKey,
                CorrelationId: decision.Id.ToString("D"),
                UserId: reporterUserId,
                EventId: report.EventId,
                ReportId: report.Id,
                ReportDecisionId: decision.Id),
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
            MaterializedAt: materializedAtUtc);
    }

    private static (string Title, string Body, string PayloadOutcome) ResolveCopy(
        EventReportDecisionKind decisionKind) => decisionKind switch
        {
            EventReportDecisionKind.NoViolation or EventReportDecisionKind.Duplicate =>
                ("Review of your event report is complete",
                    "We completed our review and did not take additional action. Thank you for taking the time to report your concern.",
                    "no-action"),
            EventReportDecisionKind.LightModerate
                or EventReportDecisionKind.HeavyRedact
                or EventReportDecisionKind.WarnOrganizer =>
                ("Action taken after your event report",
                    "We completed our review and took action. Thank you for helping keep the community safe.",
                    "action-taken"),
            _ => throw new InvalidOperationException("This decision kind does not have a final reporter outcome.")
        };

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
