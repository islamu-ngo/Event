using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Models.Storage;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Services;

/// <summary>One bounded pass over irrevocably retired objects; the existing storage scheduler owns execution.</summary>
public sealed class EventResourceStorageCleanupService(
    IStorageObjectDeletionTombstoneRepository tombstones,
    IStorageProviderBindingService bindings,
    TimeProvider clock,
    ILogger<EventResourceStorageCleanupService> logger,
    IEventResourceStorageLifecycleRepository lifecycle,
    IUnitOfWork unitOfWork) : IEventResourceStorageCleanupService
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(5);

    public async Task<StorageObjectDeletionResult> ProcessDueAsync(
        int limit, bool dryRun, CancellationToken cancellationToken)
    {
        int expired = 0, failed = 0;
        if (!dryRun)
        {
            try
            {
                expired = await unitOfWork.ExecuteSerializableAsync(
                    ct => lifecycle.RetireExpiredUploadsAsync(clock.GetUtcNow().UtcDateTime, limit, ct), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                failed++;
                logger.LogWarning("Resource upload expiry remains pending.");
            }
        }
        var transfers = await tombstones.ListWithSourcesAsync(limit, cancellationToken);
        var due = await tombstones.ListDueAsync(clock.GetUtcNow().UtcDateTime, limit, cancellationToken);
        int scanned = expired + transfers.Concat(due).Select(work => work.Id).Distinct().Count();
        if (dryRun) return new(scanned, 0, 0, 0);
        int deleted = 0;
        foreach (var candidate in transfers)
        {
            try
            {
                await unitOfWork.ExecuteSerializableAsync(async ct =>
                {
                    await lifecycle.RemoveTransferredSourcesAsync(candidate.TenantId, [], [candidate.Id], ct);
                    return true;
                }, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                failed++;
                logger.LogWarning("Resource storage source transfer remains pending.");
            }
        }
        foreach (var candidate in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var now = clock.GetUtcNow().UtcDateTime;
            var work = await tombstones.TryClaimAsync(candidate.Id, candidate.ConcurrencyStamp,
                now, now.Add(LeaseDuration), cancellationToken);
            if (work is null) continue;
            using var leaseTimeout = new CancellationTokenSource(LeaseDuration, clock);
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, leaseTimeout.Token);
            try
            {
                var provider = await bindings.ResolveAsync(work.ProviderBindingId, operation.Token);
                if (!string.Equals(provider.Provider, work.Provider, StringComparison.Ordinal))
                    throw new InvalidOperationException("storage_provider_binding_mismatch");
                var result = await provider.DeleteAsync(new(work.ObjectKey, work.ProviderObjectVersion), operation.Token);
                if (result.DeleteMarkerCreated || !string.Equals(result.Provider, work.Provider, StringComparison.Ordinal)
                    || !string.Equals(result.ObjectKey, work.ObjectKey, StringComparison.Ordinal)
                    || await provider.ExistsAsync(new(work.ObjectKey, work.ProviderObjectVersion), operation.Token))
                    throw new InvalidOperationException("storage_deletion_unconfirmed");
                if (await tombstones.TryRecordAbsenceAsync(work.Id, work.ConcurrencyStamp,
                    clock.GetUtcNow().UtcDateTime, cancellationToken))
                    deleted++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                failed++;
                var retryAt = clock.GetUtcNow().UtcDateTime;
                await tombstones.TryScheduleRetryAsync(work.Id, work.ConcurrencyStamp,
                    retryAt, retryAt.Add(RetryDelay), cancellationToken);
                logger.LogWarning(
                    "Resource storage cleanup remains pending. FailureCategory={FailureCategory}",
                    exception is UnauthorizedAccessException ? "access_denied"
                        : exception is OperationCanceledException ? "provider_timeout" : "provider_failure");
            }
        }
        return new(scanned, deleted, 0, failed);
    }
}
