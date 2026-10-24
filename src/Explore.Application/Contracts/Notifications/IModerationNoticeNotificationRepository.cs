namespace Explore.Application.Contracts.Notifications;

public enum ModerationNoticeAcknowledgementOutcome { Acknowledged = 1, Unauthenticated, NotFound, Conflict, Unavailable }

public sealed record ModerationNoticeAcknowledgement(
    Guid TenantId, Guid SubjectId, Guid NoticeId, Guid OperationId, string SnapshotHash);

public sealed record ModerationNoticeAcknowledgementResult(
    ModerationNoticeAcknowledgementOutcome Outcome, Guid? ReceiptId = null);

public interface IModerationNoticeNotificationRepository
{
    /// <summary>
    /// Native explicit subject acknowledgement, never called by GET, pointer materialization or
    /// reviewer reads. First proof wins; a changed operation or original hash cannot renew it.
    /// </summary>
    Task<ModerationNoticeAcknowledgementResult> AcknowledgeAsync(
        ModerationNoticeAcknowledgement request, CancellationToken cancellationToken);
}
