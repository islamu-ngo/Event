using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Models.Storage;
using Explore.Application.Telemetry;
using Explore.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure;

public sealed class StorageReconciliationService(
    IStorageObjectRepository storageObjectRepository,
    IStorageProviderBindingService providerResolver,
    IStorageProviderBindingRepository bindings,
    IStorageReconciliationOwner reconciliationOwner,
    ILocalStorageQuarantineOwner quarantineOwner,
    IOptions<StorageReconciliationSettings> settings,
    BusinessMetrics metrics,
    ILogger<StorageReconciliationService> logger,
    IEventResourceStorageCleanupService resourceCleanup,
    IEventResourceStorageLifecycleRepository lifecycle,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IStorageReconciliationService
{
    private const string MissingMetadataRecordReason = "metadata_record_missing";

    private readonly StorageReconciliationSettings _settings = settings.Value;

    public async Task<StorageReconciliationResult> ReconcileAsync(
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var mode = ResolveMode();
        var counts = new ReconciliationCounts();

        try
        {
            var resourceResult = await resourceCleanup.ProcessDueAsync(
                Math.Min(_settings.BatchSize, 1000), _settings.DryRun, cancellationToken);
            logger.LogInformation(
                "Resource deletion reconciliation scanned {ScannedCount}, confirmed {DeletedCount}, failed {FailedCount}.",
                resourceResult.ScannedCount, resourceResult.DeletedCount, resourceResult.FailedCount);
            await ReconcileMetadataAsync(utcNow, counts, cancellationToken);
            await ReconcileBackingObjectsAsync(utcNow, counts, cancellationToken);

            var result = counts.ToResult(utcNow, _settings.DryRun);
            RecordResultMetrics(result);
            metrics.RecordStorageReconciliationRun(mode, "succeeded");

            logger.LogInformation(
                "Storage reconciliation completed in {Mode} mode. Metadata scanned {ScannedMetadataCount}, missing {MissingBackingObjectCount}, metadata quarantined {QuarantinedMetadataCount}, delete eligible {DeleteEligibleMetadataCount}, metadata deleted {DeletedMetadataCount}, backing scanned {ScannedBackingObjectCount}, orphans {OrphanBackingObjectCount}, backing quarantined {QuarantinedBackingObjectCount}, skipped {SkippedCount}, failed {FailedCount}.",
                mode,
                result.ScannedMetadataCount,
                result.MissingBackingObjectCount,
                result.QuarantinedMetadataCount,
                result.DeleteEligibleMetadataCount,
                result.DeletedMetadataCount,
                result.ScannedBackingObjectCount,
                result.OrphanBackingObjectCount,
                result.QuarantinedBackingObjectCount,
                result.SkippedCount,
                result.FailedCount);

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            metrics.RecordStorageReconciliationRun(mode, "failed", "reconciliation_failed");
            throw;
        }
    }

    private async Task ReconcileMetadataAsync(
        DateTime utcNow,
        ReconciliationCounts counts,
        CancellationToken cancellationToken)
    {
        var quarantineBeforeUtc = utcNow.AddHours(-_settings.MissingObjectQuarantineGraceHours);
        var activeObjects = await reconciliationOwner.ListMissingBackingCandidatesAsync(
            quarantineBeforeUtc,
            Math.Min(_settings.BatchSize, IStorageReconciliationOwner.MaximumCandidates),
            cancellationToken);

        counts.ScannedMetadataCount += activeObjects.Count;

        foreach (var storageObject in activeObjects)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(storageObject.ObjectKey))
            {
                counts.SkippedCount++;
                continue;
            }

            try
            {
                var provider = await providerResolver.ResolveTargetAsync(
                    storageObject.StorageProviderBindingId, storageObject.Provider, cancellationToken);
                var exists = await provider.ExistsAsync(
                    new FileStorageExistsInput(storageObject.ObjectKey, storageObject.ProviderVersionId),
                    cancellationToken);

                if (exists)
                {
                    continue;
                }

                counts.MissingBackingObjectCount++;
                if (_settings.DryRun || !_settings.QuarantineMissingObjects)
                {
                    counts.SkippedCount++;
                    continue;
                }

                if (await reconciliationOwner.TryQuarantineMissingBackingAsync(
                    storageObject, utcNow, cancellationToken))
                    counts.QuarantinedMetadataCount++;
                else
                    counts.SkippedCount++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException or SystemException)
            {
                counts.FailedCount++;
                metrics.RecordStorageReconciliationObjects(1, storageObject.Provider, "metadata", "missing", "failed", CategorizeProviderFailure(ex));
                logger.LogWarning(
                    "Storage reconciliation failed while checking metadata object for provider {Provider}.",
                    NormalizeProviderForLog(storageObject.Provider));
            }
        }

        var deleteBeforeUtc = utcNow.AddHours(-_settings.DeleteGraceHours);
        var deleteEligibleObjects = await storageObjectRepository.ListDeleteEligibleForReconciliationAsync(
            deleteBeforeUtc,
            _settings.BatchSize,
            cancellationToken);

        counts.DeleteEligibleMetadataCount += deleteEligibleObjects.Count;

        foreach (var storageObject in deleteEligibleObjects)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_settings.DryRun || !_settings.DeleteQuarantinedObjects)
            {
                counts.SkippedCount++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(storageObject.ObjectKey))
            {
                counts.FailedCount++;
                continue;
            }

            try
            {
                // Each transaction declares one complete source set through admission's fence.
                // Pending means durable custody transfer; only the tombstone worker deletes bytes.
                var admission = await unitOfWork.ExecuteSerializableAsync(
                    ct => lifecycle.TryQueueRetirementAsync(storageObject.TenantId, storageObject.Id, utcNow, ct),
                    cancellationToken);
                switch (admission)
                {
                    case StorageRetirementAdmission.Pending:
                        counts.DeletedMetadataCount++;
                        break;
                    case StorageRetirementAdmission.NotFound:
                    case StorageRetirementAdmission.InUse:
                    case StorageRetirementAdmission.RetentionBlocked:
                        counts.SkippedCount++;
                        break;
                    case StorageRetirementAdmission.InvalidTarget:
                        counts.FailedCount++;
                        break;
                    default:
                        throw new InvalidOperationException("Unknown storage retirement acknowledgement.");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException or SystemException)
            {
                counts.FailedCount++;
                metrics.RecordStorageDelete(storageObject.Provider, "failed", CategorizeProviderFailure(ex));
                logger.LogWarning(
                    "Storage reconciliation failed while admitting quarantined metadata retirement for provider {Provider}.",
                    NormalizeProviderForLog(storageObject.Provider));
            }
        }
    }

    private async Task ReconcileBackingObjectsAsync(
        DateTime utcNow,
        ReconciliationCounts counts,
        CancellationToken cancellationToken)
    {
        var orphanBeforeUtc = utcNow.AddHours(-_settings.OrphanFileQuarantineGraceHours);

        var captured = await bindings.ListLocalAsync(cancellationToken);
        foreach (var target in captured.GroupBy(binding => binding.LocalRootPath, StringComparer.Ordinal))
        {
            var remaining = _settings.BatchSize - counts.ScannedBackingObjectCount;
            if (remaining <= 0)
            {
                break;
            }

            var inventory = new List<FileStorageInventoryObject>();
            IFileStorageInventoryProvider provider;
            try
            {
                provider = (IFileStorageInventoryProvider)await providerResolver.ResolveTargetAsync(
                    target.First().Id, StorageProviders.Local, cancellationToken);
                await foreach (var item in provider.ListObjectsAsync(remaining, cancellationToken))
                {
                    inventory.Add(item);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SystemException)
            {
                counts.FailedCount++;
                metrics.RecordStorageReconciliationObjects(1, StorageProviders.Local, "backing_object", "scan", "failed", "inventory_unavailable");
                logger.LogWarning(
                    "Storage reconciliation inventory scan failed for provider {Provider}.",
                    StorageProviders.Local);
                continue;
            }

            counts.ScannedBackingObjectCount += inventory.Count;
            if (inventory.Count == 0)
            {
                continue;
            }

            foreach (var item in inventory)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (item.LastModifiedUtc is not { } lastModifiedUtc || lastModifiedUtc > orphanBeforeUtc)
                {
                    counts.SkippedCount++;
                    continue;
                }

                try
                {
                    var bindingId = target.First().Id;
                    if (_settings.DryRun || !_settings.QuarantineOrphanLocalFiles)
                    {
                        if (await quarantineOwner.IsLocalOrphanAsync(bindingId, item.ObjectKey, cancellationToken))
                        {
                            counts.OrphanBackingObjectCount++;
                            counts.SkippedCount++;
                        }
                    }
                    else
                    {
                        var admitted = await quarantineOwner.AdmitLocalQuarantineAsync(
                            bindingId, item.ObjectKey, item.SizeBytes, lastModifiedUtc,
                            orphanBeforeUtc, MissingMetadataRecordReason, cancellationToken);
                        if (admitted is not null)
                            counts.OrphanBackingObjectCount++;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException or SystemException)
                {
                    counts.FailedCount++;
                    metrics.RecordStorageReconciliationObjects(1, provider.Provider, "backing_object", "quarantine", "failed", "quarantine_failed");
                    logger.LogWarning(
                        "Storage reconciliation failed while quarantining orphan backing object for provider {Provider}.",
                        NormalizeProviderForLog(provider.Provider));
                }
            }
        }
        if (!_settings.DryRun && _settings.QuarantineOrphanLocalFiles)
            await DrainLocalQuarantinesAsync(counts, cancellationToken);
    }

    private async Task DrainLocalQuarantinesAsync(
        ReconciliationCounts counts, CancellationToken cancellationToken)
    {
        var claims = await quarantineOwner.ClaimLocalQuarantineAsync(
            Math.Min(_settings.BatchSize, ILocalStorageQuarantineOwner.MaximumClaims),
            TimeSpan.FromMinutes(5), cancellationToken);
        foreach (var claim in claims)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (claim.LeaseExpiresAtUtc is not { } expires || expires <= clock.GetUtcNow().UtcDateTime)
                {
                    counts.SkippedCount++;
                    continue;
                }
                var input = new FileStorageQuarantineInput(
                    claim.ObjectKey, claim.Id, claim.ObservedSize, claim.ObservedLastModifiedUtc,
                    claim.ProviderBindingId, claim.ConcurrencyStamp, expires,
                    claim.CanonicalPhysicalPath, claim.PhysicalPathHash);
                var provider = (IFileStorageInventoryProvider)await providerResolver.ResolveTargetAsync(
                    claim.ProviderBindingId, StorageProviders.Local, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (input.LeaseExpiresAtUtc <= clock.GetUtcNow().UtcDateTime
                    || !await quarantineOwner.IsLocalQuarantineClaimCurrentAsync(input, cancellationToken)
                    || input.LeaseExpiresAtUtc <= clock.GetUtcNow().UtcDateTime)
                {
                    counts.SkippedCount++;
                    continue;
                }
                cancellationToken.ThrowIfCancellationRequested();
                var effect = await provider.QuarantineAsync(input, quarantineOwner, clock, cancellationToken);
                if (effect.Outcome == FileStorageQuarantineOutcome.Blocked)
                {
                    await quarantineOwner.BlockLocalQuarantineAsync(
                        claim.Id, claim.ConcurrencyStamp, cancellationToken);
                    counts.FailedCount++;
                    continue;
                }
                var outcome = effect.Outcome switch
                {
                    FileStorageQuarantineOutcome.Moved => LocalStorageQuarantineOutcome.Moved,
                    FileStorageQuarantineOutcome.AlreadyQuarantined => LocalStorageQuarantineOutcome.AlreadyQuarantined,
                    FileStorageQuarantineOutcome.AlreadyAbsent => LocalStorageQuarantineOutcome.AlreadyAbsent,
                    _ => throw new InvalidOperationException("Unknown local quarantine outcome.")
                };
                bool settled = await quarantineOwner.CompleteLocalQuarantineAsync(
                    claim.Id, claim.ConcurrencyStamp, outcome, cancellationToken);
                if (settled && effect.Quarantined)
                    counts.QuarantinedBackingObjectCount++;
                else
                    counts.SkippedCount++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // An uncertain effect retains this exact claim for expiry recovery.
                throw;
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException
                or IOException or UnauthorizedAccessException or SystemException)
            {
                await quarantineOwner.RetryLocalQuarantineAsync(
                    claim.Id, claim.ConcurrencyStamp, DateTime.UtcNow.AddMinutes(5), cancellationToken);
                counts.FailedCount++;
                metrics.RecordStorageReconciliationObjects(1, StorageProviders.Local,
                    "backing_object", "quarantine", "failed", "quarantine_failed");
                logger.LogWarning("Storage reconciliation could not settle local quarantine work.");
            }
        }
    }

    private void RecordResultMetrics(StorageReconciliationResult result)
    {
        metrics.RecordStorageReconciliationObjects(result.ScannedMetadataCount, null, "metadata", "scan", "succeeded");
        metrics.RecordStorageReconciliationObjects(result.MissingBackingObjectCount, null, "metadata", "missing", "succeeded");
        metrics.RecordStorageReconciliationObjects(result.QuarantinedMetadataCount, null, "metadata", "quarantine", "succeeded");
        metrics.RecordStorageReconciliationObjects(result.DeleteEligibleMetadataCount, null, "metadata", "delete", "skipped");
        metrics.RecordStorageReconciliationObjects(result.DeletedMetadataCount, null, "metadata", "delete", "succeeded");
        metrics.RecordStorageReconciliationObjects(result.ScannedBackingObjectCount, StorageProviders.Local, "backing_object", "scan", "succeeded");
        metrics.RecordStorageReconciliationObjects(result.OrphanBackingObjectCount, StorageProviders.Local, "backing_object", "orphan", "succeeded");
        metrics.RecordStorageReconciliationObjects(result.QuarantinedBackingObjectCount, StorageProviders.Local, "backing_object", "quarantine", "succeeded");
        metrics.RecordStorageReconciliationObjects(result.SkippedCount, null, "metadata", "skip", "skipped");
        metrics.RecordStorageReconciliationObjects(result.FailedCount, null, "metadata", "scan", "failed", "reconciliation_failed");
    }

    private string ResolveMode()
    {
        if (_settings.DryRun)
        {
            return "dry_run";
        }

        var mutatingActions = 0;
        if (_settings.QuarantineMissingObjects || _settings.QuarantineOrphanLocalFiles)
        {
            mutatingActions++;
        }

        if (_settings.DeleteQuarantinedObjects)
        {
            mutatingActions++;
        }

        return mutatingActions switch
        {
            0 => "report",
            1 when _settings.DeleteQuarantinedObjects => "delete",
            1 => "quarantine",
            _ => "mixed"
        };
    }

    private static string CategorizeProviderFailure(Exception exception)
        => exception switch
        {
            InvalidOperationException => "provider_unavailable",
            ArgumentException => "validation_failed",
            UnauthorizedAccessException => "access_denied",
            IOException => "delete_failed",
            _ => "reconciliation_failed"
        };

    private static string NormalizeProviderForLog(string? provider)
        => provider switch
        {
            StorageProviders.Local => StorageProviders.Local,
            StorageProviders.S3Compatible => StorageProviders.S3Compatible,
            StorageProviders.LegacyExternal => StorageProviders.LegacyExternal,
            _ => "unknown"
        };

    private sealed class ReconciliationCounts
    {
        public int ScannedMetadataCount { get; set; }
        public int MissingBackingObjectCount { get; set; }
        public int QuarantinedMetadataCount { get; set; }
        public int DeleteEligibleMetadataCount { get; set; }
        public int DeletedMetadataCount { get; set; }
        public int ScannedBackingObjectCount { get; set; }
        public int OrphanBackingObjectCount { get; set; }
        public int QuarantinedBackingObjectCount { get; set; }
        public int SkippedCount { get; set; }
        public int FailedCount { get; set; }

        public StorageReconciliationResult ToResult(DateTime utcNow, bool dryRun)
            => new(
                utcNow,
                dryRun,
                ScannedMetadataCount,
                MissingBackingObjectCount,
                QuarantinedMetadataCount,
                DeleteEligibleMetadataCount,
                DeletedMetadataCount,
                ScannedBackingObjectCount,
                OrphanBackingObjectCount,
                QuarantinedBackingObjectCount,
                SkippedCount,
                FailedCount);
    }
}
