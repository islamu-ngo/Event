using Explore.Domain;

namespace Explore.Application.Contracts.Moderation;

/// <summary>
/// Materializes a generic operational pointer on the caller-owned native transaction.
/// A returned identifier is independently verified against the persisted native graph.
/// </summary>
public interface IModerationRecognitionRecoveryNotifications
{
    Task<Guid?> DeliverAsync(ModerationRecognitionEscalation escalation, Guid tenantId,
        CancellationToken token);
}
