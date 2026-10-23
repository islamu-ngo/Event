using Explore.Application.Models;
using Explore.Application.Contracts.Notifications;
using Explore.Domain;

namespace Explore.Application.Notifications;

/// <summary>
/// Pointer-only native template. It deliberately accepts no explanation, finding or recipient
/// display name. The reference is not a bearer capability and does not invent a browser route.
/// </summary>
public static class ModerationNoticeEmailTemplate
{
    public const string Key = "moderation.notice.portal.v1";
    public const string SourceType = "moderation_notice";
    public const string Subject = "Moderation notice available";

    public static string Reference(Guid noticeId) => $"moderation-notice:{noticeId:D}";

    public static string Body(Guid noticeId) =>
        "Sign in to your account to open your protected moderation notices.\n"
        + $"Notice reference: {Reference(noticeId)}\n"
        + "This email contains no decision explanation and does not acknowledge receipt of the notice.";

    public static EmailMessage Create(Guid noticeId, string address, DateTimeOffset until,
        ModerationNoticeEmailWindow window) => new()
    {
        To = address, Subject = Subject, PlainTextBody = Body(noticeId),
        DisclosureUntilUtc = until.UtcDateTime, ModerationNoticeWindow = window
    };

    public static bool Matches(EmailDispatchOutbox dispatch, NotificationIntent intent, Guid noticeId) =>
        dispatch.Kind == EmailDispatchKind.AccountModerationOriginalNotice
        && dispatch.SourceType == SourceType && dispatch.SourceId == noticeId
        && dispatch.NotificationIntentId == intent.Id && dispatch.TenantId == intent.TenantId
        && dispatch.RecipientUserId == intent.RecipientUserId
        && dispatch.RecipientAddressSource == RecipientAddressSource.TenantUserVerifiedEmail
        && dispatch.ManagedTenantProvisioningOperationId is null
        && dispatch.EventId is null && dispatch.RegistrationOrderId is null
        && dispatch.Subject == Subject && dispatch.PlainTextBody == Body(noticeId)
        && dispatch.HtmlBody is null && dispatch.ReplyTo is null && dispatch.CorrelationId is null
        && intent.TemplateKey == Key && intent.SafePayloadReference == Reference(noticeId)
        && intent.FanoutOccurrenceId is null && intent.EventId is null
        && intent.ReportId is null && intent.ReportDecisionId is null;
}
