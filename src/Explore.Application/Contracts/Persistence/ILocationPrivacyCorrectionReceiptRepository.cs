using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Returns retained source ownership only for the exact persisted live original claim.</summary>
public interface ILocationPrivacyCorrectionReceiptRepository
{
    Task<LocationPrivacyCorrectionReceipt?> GetClaimedAsync(
        OutboxMessage message, bool deadLetterReconciliation, CancellationToken cancellationToken);
}
