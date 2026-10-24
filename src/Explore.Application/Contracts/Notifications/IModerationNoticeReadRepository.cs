using System.Collections.Immutable;
using Explore.Domain;

namespace Explore.Application.Contracts.Notifications;

public enum ModerationNoticeReadOutcome { Allowed = 1, Invalid, Unauthenticated, NotFound, Unavailable }

/// <summary>Server-resolved coordinates; the repository independently checks the current caller.</summary>
public sealed record ModerationNoticeReadRequest(
    Guid TenantId, Guid RecipientId, Guid? Id, Guid? AfterId, int Limit);

public sealed record ModerationNoticeReadEntry(
    ModerationNotice Notice, ModerationNoticeSource Source, ModerationNoticeNotificationReceipt? Notification = null);

public interface IModerationNoticeReadRepository
{
    /// <summary>Retains erasure authority around acquisition, representation and actual delivery; never replay the terminal consumer.</summary>
    Task<T> ExecuteSerializedAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken);

    Task<ModerationNoticeReadAcquisition> AcquireAsync(
        ModerationNoticeReadRequest request, CancellationToken cancellationToken);
}

public sealed record ModerationNoticeReadAcquisition(
    ModerationNoticeReadOutcome Outcome, IModerationNoticeReadLease? Lease = null);

/// <summary>
/// Owns the native serializable read and all recipient, source and reviewer fences through the
/// terminal consumer. No caller may deliver a prepared representation before RevalidateAsync.
/// Disposal rolls back this read-only transaction and restores the context's authority frame.
/// </summary>
public interface IModerationNoticeReadLease : IAsyncDisposable
{
    ImmutableArray<ModerationNoticeReadEntry> Entries { get; }
    Task<ModerationNoticeReadOutcome> RevalidateAsync(CancellationToken cancellationToken);
}
