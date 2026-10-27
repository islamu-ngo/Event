using Explore.Application.Contracts.PrivacyErasure;
using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Native request-subject capture authority; it cannot borrow an administrator acquisition.
/// Receive and Compute must finish their owning identity transaction before AcquireAppend.
/// The append lease retains native source fences until the external authority commit returns.
/// </summary>
public interface IModerationRecognitionErasureRepository
{
    Task<ModerationRecognitionPending?> ReceiveAsync(PrivacyErasureRequest request,
        byte[] receiptHash, DateTimeOffset receiptExpiresAt, CancellationToken token);
    Task ComputeAsync(PrivacyErasureRequest request, CancellationToken token);
    Task<IModerationRecognitionErasureLease> AcquireAppendAsync(PrivacyErasureRequest request, CancellationToken token);
    Task VerifyPurgeAsync(PrivacyErasureIntent fact, CancellationToken token);
    Task<ModerationRecognitionPending?> FindAsync(Guid intentId, CancellationToken token);
    Task<ModerationRecognitionPending?> FindReceiptAsync(byte[] hash, CancellationToken token);
    Task<IReadOnlyList<ModerationRecognitionPending>> ReadReceivedAfterAsync(Guid? afterId, int limit, CancellationToken token);
    Task RecordFailureAsync(PrivacyErasureRequest request, ModerationRecognitionPendingFailure failure, CancellationToken token);
    /// <summary>
    /// One bounded native recovery pass drops only matching eligibility at the original
    /// sanction end or retention ceiling. It preserves the complete receipt material.
    /// This is neither record deletion nor proof of readable-key retirement safety.
    /// </summary>
    Task<int> ExpireMatchingEligibilityAsync(int limit, CancellationToken token);
    Task<IReadOnlyList<ModerationRecognitionActionRecovery>> ReadActionRecoveryAfterAsync(
        Guid? afterId, int limit, CancellationToken token);
    /// <summary>
    /// Retries only the pinned live login's original action uncertainty. Admission
    /// eligibility is never reconsidered for an already enrolled subject.
    /// </summary>
    Task RetryActionRecoveryAsync(Guid recoveryId, CancellationToken token);
}

public interface IModerationRecognitionErasureLease : IAsyncDisposable
{
    bool CanAppend { get; }
    Task BindAsync(PrivacyErasureIntent fact, CancellationToken token);
    Task CommitAsync(CancellationToken token);
}
