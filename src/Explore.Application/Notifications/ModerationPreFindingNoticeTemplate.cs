namespace Explore.Application.Notifications;

public static class ModerationPreFindingNoticeTemplate
{
    public const string Key = "moderation.pre-finding.portal.v1";
    public const string Prefix = "moderation-pre-finding:";
    public const string Title = "Protected moderation notice available";
    public const string NotificationEntityTypeName = "ModerationPreFindingNotice";

    public static string Reference(Guid noticeId) => Prefix + noticeId.ToString("D");

    public static string DeduplicationKey(Guid noticeId, Guid recipientId) =>
        $"moderation-pre-finding:{noticeId:N}:{recipientId:N}";
}
