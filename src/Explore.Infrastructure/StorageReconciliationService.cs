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
    IStorageProducerOperationRepository producers,
    IOptions<StorageReconciliationSettings> settings,
    BusinessMetrics metrics,
    ILogger<StorageReconciliationService> logger,
    IEventResourceStorageCleanupService resourceCleanup) : IStorageReconciliationService
{
    private const string MissingBackingObjectReason = "backing_object_missing";
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
        var activeObjects = await storageObjectRepository.ListActiveForReconciliationAsync(
            quarantineBeforeUtc,
            _settings.BatchSize,
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

                storageObject.MarkQuarantined(null, MissingBackingObjectReason, utcNow);
                await storageObjectRepository.Update(storageObject);
                counts.QuarantinedMetadataCount++;
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
                var provider = await providerResolver.ResolveTargetAsync(
                    storageObject.StorageProviderBindingId, storageObject.Provider, cancellationToken);
                var deleted = await provider.DeleteAsync(
                    new FileStorageDeleteInput(storageObject.ObjectKey, storageObject.ProviderVersionId),
                    cancellationToken);
                if (deleted.Provider != storageObject.Provider || deleted.ObjectKey != storageObject.ObjectKey
                    || deleted.DeleteMarkerCreated)
                    throw new InvalidOperationException("storage_deletion_unconfirmed");

                storageObject.MarkDeleted(null, utcNow);
                await storageObjectRepository.Update(storageObject);
                counts.DeletedMetadataCount++;
                metrics.RecordStorageDelete(storageObject.Provider, "succeeded");
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
                    "Storage reconciliation failed while deleting quarantined metadata object for provider {Provider}.",
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
                return;
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

            var objectKeys = inventory
                .Select(item => item.ObjectKey)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var knownKeys = await producers.ListKnownObjectKeysAsync(
                target.Select(binding => binding.Id).ToArray(),
                objectKeys,
                cancellationToken);
            var knownKeySet = new HashSet<string>(knownKeys, StringComparer.Ordinal);

            foreach (var item in inventory)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (knownKeySet.Contains(item.ObjectKey))
                {
                    continue;
                }

                if (item.LastModifiedUtc is { } lastModifiedUtc && lastModifiedUtc > orphanBeforeUtc)
                {
                    counts.SkippedCount++;
                    continue;
                }

                counts.OrphanBackingObjectCount++;

                if (_settings.DryRun || !_settings.QuarantineOrphanLocalFiles)
                {
                    counts.SkippedCount++;
                    continue;
                }

                try
                {
                    var quarantineResult = await provider.QuarantineAsync(
                        new FileStorageQuarantineInput(item.ObjectKey, MissingMetadataRecordReason, utcNow),
                        cancellationToken);

                    if (quarantineResult.Quarantined)
                    {
                        counts.QuarantinedBackingObjectCount++;
                    }
                    else
                    {
                        counts.SkippedCount++;
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
