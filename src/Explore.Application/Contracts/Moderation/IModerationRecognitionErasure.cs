using Explore.Application.Contracts.PrivacyErasure;
using Explore.Domain;

namespace Explore.Application.Contracts.Moderation;

public interface IModerationRecognitionErasure
{
    Task<PrivacyErasureIntent?> AppendAsync(PrivacyErasureRequest request, byte[] receiptHash,
        DateTimeOffset receiptExpiresAt, CancellationToken token);
    Task<T> ExecutePurgeAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken token);
    Task VerifyPurgeAsync(PrivacyErasureIntent fact, CancellationToken token);
    Task<ModerationRecognitionPending?> FindAsync(Guid intentId, CancellationToken token);
    Task<ModerationRecognitionPending?> FindReceiptAsync(byte[] hash, CancellationToken token);
    Task ReconcileReceivedAsync(CancellationToken token);
}
