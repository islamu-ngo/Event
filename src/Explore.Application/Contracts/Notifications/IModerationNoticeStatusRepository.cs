using Explore.Domain;

namespace Explore.Application.Contracts.Notifications;

public enum ModerationNoticeNotificationProof { Proven = 1, Unproven, Unavailable, PurposePurged }

public sealed record ModerationNoticeStatusRequest(Guid TenantId, Guid SubjectId, Guid DecisionId, Guid EffectId);

/// <summary>No retained notification facts can be inferred from an original-write cleanup marker.</summary>
public sealed record ModerationNoticeStatusEntry(
    ModerationEffectIntent Effect, ModerationNoticeNotificationProof Proof,
    ModerationNoticeNotificationReceipt? Notification,
    int? OriginalContestationCalendarMonths, DateTimeOffset? OriginalRetainUntil);

public interface IModerationNoticeStatusRepository
{
    Task<T> ExecuteSerializedAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken);

    Task<ModerationNoticeStatusAcquisition> AcquireAsync(
        ModerationNoticeStatusRequest request, CancellationToken cancellationToken);
}

public sealed record ModerationNoticeStatusAcquisition(
    ModerationNoticeReadOutcome Outcome, IModerationNoticeStatusLease? Lease = null);

/// <summary>Same native primary transaction and subject fences must survive the final output write.</summary>
public interface IModerationNoticeStatusLease : IAsyncDisposable
{
    ModerationNoticeStatusEntry Entry { get; }
    Task<ModerationNoticeReadOutcome> RevalidateAsync(CancellationToken cancellationToken);
}
