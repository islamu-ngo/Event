using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Reads account-bound intake receipts inside the native serializable intake transaction.</summary>
public interface IModerationCaseTargetReceiptRepository
{
    /// <summary>
    /// Authenticates the presented opaque token against its digest and fences the actual account,
    /// tenant, installed host, epoch and receipt. The returned entity is not a target-access permit:
    /// the intake writer must independently requalify its relationship and exact material sources
    /// and retain all fences through case, consumption and acknowledgement commit.
    /// </summary>
    Task<ModerationCaseTargetReceipt?> ReadForCurrentCallerAsync(
        Guid receiptId, ReadOnlyMemory<byte> presentedToken, CancellationToken cancellationToken);
}
