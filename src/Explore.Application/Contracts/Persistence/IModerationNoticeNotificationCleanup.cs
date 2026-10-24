using System.Collections.Immutable;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Exact governed native pointers, never original text, hashes, evidence or delivery authority.</summary>
public sealed record ModerationNoticeNotificationCleanupTarget(
    Guid NoticeId, Guid IntentId, Guid RecipientId, int Audience);

public sealed record ModerationNoticeNotificationCleanupRequest(
    Guid TransactionId, Guid HostBindingId, Guid TrustDomainId, Guid TenantId, Guid EffectId,
    DateTimeOffset RetainUntil, ImmutableArray<ModerationNoticeNotificationCleanupTarget> Targets);

/// <summary>
/// Implement on the existing native notification graph owner, sharing the caller's actual
/// primary context and serializable transaction. Never open or commit another transaction.
/// Acquire the graph's normal sorted delivery/dispatch fences and keep them through commit.
/// Validate each exact tenant/intent/recipient, moderation category/template, notice pointer
/// and effect/audience deduplication key. Reject missing or substituted graph ownership.
/// Also discover original moderation graphs by the exact effect's native deduplication prefix;
/// Targets may be empty when an occurrence is missing, and is never a complete-set assertion.
///
/// Include soft-deleted copies by ignoring only their SoftDelete filter, never the tenant filter.
/// Stage hard removal of purpose-expired Notification, delivery, intent and any governed
/// dispatch/delegation copies in their existing FK order, after cancelling queued work under
/// its actual dispatch fence. A sent/unknown external acceptance is not undone or called a
/// successful notification. An independently governed dispatch receipt may survive only under
/// its own approved authority with no original payload/pointer reconstruction capability.
///
/// The caller stages occurrence removal before invoking StageAsync, then flushes the shared
/// context. VerifyRemovedAsync independently rereads the native graph and queued work after
/// that flush, and again at final commit qualification. Missing implementation is unavailable,
/// not a no-op success. Existing dispatch/read/repair owners must reject absent original
/// notice sources; this port cannot make a previously loaded external send retractable.
/// </summary>
public interface IModerationNoticeNotificationCleanup
{
    Task<bool> StageAsync(
        ModerationNoticeNotificationCleanupRequest request, CancellationToken cancellationToken);

    Task<bool> VerifyRemovedAsync(
        ModerationNoticeNotificationCleanupRequest request, CancellationToken cancellationToken);
}
