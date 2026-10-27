using System.Data.Common;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Domain;

namespace Explore.Application.Services.Moderation;

/// <summary>
/// Commits receipt material before reacquiring the identity gate for erasure. A native append
/// lease keeps canonical/source locks alive until the retained authority commit has returned.
/// Co-located SQLite commits each stage through its existing shared identity transaction.
/// </summary>
public sealed class ModerationRecognitionErasure(
    IModerationRecognitionErasureRepository repository,
    IPrivacyIdentityFenceAuthority identity,
    IPrivacyErasureAuthority authority,
    ISettingMutationLock settings) : IModerationRecognitionErasure
{
    public async Task<PrivacyErasureIntent?> AppendAsync(PrivacyErasureRequest request, byte[] receiptHash,
        DateTimeOffset receiptExpiresAt, CancellationToken token)
    {
        try { return await AppendOnceAsync(request, receiptHash, receiptExpiresAt, token); }
        catch (Exception exception) when (!token.IsCancellationRequested
            && exception is TimeoutException or IOException or OperationCanceledException)
        {
            // The first complete capture remains immutable. A fresh gate and native transaction
            // reread it and the authority's exact idempotent request, never trust a lost acknowledgement.
            return await AppendOnceAsync(request, receiptHash, receiptExpiresAt, token);
        }
    }

    private Task<PrivacyErasureIntent?> AppendOnceAsync(PrivacyErasureRequest request, byte[] receiptHash,
        DateTimeOffset receiptExpiresAt, CancellationToken token) =>
        settings.ExecuteOrderedGroupsAsync<PrivacyErasureIntent?>(
            [[ModerationRecognitionKeyRing.SecretSettingKey]], async cancellation =>
            {
                var received = await identity.ExecuteSerializedAsync(
                    ct => repository.ReceiveAsync(request, receiptHash, receiptExpiresAt, ct), cancellation);
                try
                {
                    await identity.ExecuteSerializedAsync(async ct =>
                    {
                        await repository.ComputeAsync(request, ct);
                        return true;
                    }, cancellation);
                }
                catch (Exception exception) when (received is not null && !cancellation.IsCancellationRequested
                    && exception is DbException or IOException)
                {
                    await repository.RecordFailureAsync(request, ModerationRecognitionPendingFailure.StoreUnavailable, cancellation);
                    return null;
                }
                IModerationRecognitionErasureLease? lease = null;
                try
                {
                    var fact = await identity.ExecuteSerializedAsync<PrivacyErasureIntent?>(async ct =>
                    {
                        lease = await repository.AcquireAppendAsync(request, ct);
                        if (!lease.CanAppend) return null;
                        // The retained authority owns exact request idempotency. Its ambiguous
                        // acknowledgement is retried only through a new full fenced acquisition.
                        var appended = await authority.AppendAsync(request, ct);
                        await lease.BindAsync(appended, ct);
                        return appended;
                    }, cancellation);
                    if (lease is not null) await lease.CommitAsync(cancellation);
                    return fact;
                }
                finally
                {
                    if (lease is not null) await lease.DisposeAsync();
                }
            }, token);

    public Task<T> ExecutePurgeAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken token) =>
        settings.ExecuteOrderedGroupsAsync([[ModerationRecognitionKeyRing.SecretSettingKey]],
            ct => identity.ExecuteSerializedAsync(operation, ct), token);

    public Task VerifyPurgeAsync(PrivacyErasureIntent fact, CancellationToken token) =>
        repository.VerifyPurgeAsync(fact, token);
    public Task<ModerationRecognitionPending?> FindAsync(Guid intentId, CancellationToken token) =>
        repository.FindAsync(intentId, token);
    public Task<ModerationRecognitionPending?> FindReceiptAsync(byte[] hash, CancellationToken token) =>
        repository.FindReceiptAsync(hash, token);

    public async Task ReconcileReceivedAsync(CancellationToken token)
    {
        Guid? after = null;
        while (true)
        {
            var requests = await repository.ReadReceivedAfterAsync(after, 100, token);
            if (requests.Count == 0) break;
            foreach (var pending in requests)
            {
                var request = PrivacyErasureRequest.Create(pending.IntentId, PrivacyErasureSubjectKind.User,
                    pending.SubjectId, pending.ReasonCode, pending.PolicyVersion);
                await AppendAsync(request, pending.ReceiptHash, pending.ReceiptExpiresAt, token);
                after = pending.IntentId;
            }
        }
        // A single bounded batch also runs when no received request remains. Exact
        // interval expiry must not depend on the erased subject returning to match.
        await repository.ExpireMatchingEligibilityAsync(100, token);
        after = null;
        while (true)
        {
            var recoveries = await repository.ReadActionRecoveryAfterAsync(after, 100, token);
            if (recoveries.Count == 0) break;
            foreach (var recovery in recoveries)
            {
                await settings.ExecuteOrderedGroupsAsync([[ModerationRecognitionKeyRing.SecretSettingKey]],
                    async ct =>
                    {
                        await repository.RetryActionRecoveryAsync(recovery.Id, ct);
                        return true;
                    }, token);
                after = recovery.Id;
            }
        }
    }
}
