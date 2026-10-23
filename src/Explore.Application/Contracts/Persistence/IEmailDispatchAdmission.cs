using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Owns atomic cross-tenant admission and stale recovery. Claimed content is loaded
/// under exact ordinary tenant authority, never returned by the Global database capability.
/// </summary>
public interface IEmailDispatchAdmission
{
    Task<IReadOnlyList<EmailDispatchOutbox>> ClaimPendingBatchAsync(
        EmailDispatchBatchClaimRequest request, CancellationToken cancellationToken);

    Task<EmailDispatchOutbox?> TryClaimSpecificAsync(
        EmailDispatchSpecificClaimRequest request, CancellationToken cancellationToken);

    Task<EmailDispatchStaleRecoveryResult> RecoverStaleProcessing(
        EmailDispatchStaleRecoveryRequest request, CancellationToken cancellationToken);
}
