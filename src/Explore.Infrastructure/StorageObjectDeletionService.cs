using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Models.Storage;
using Explore.Application.Telemetry;
using Explore.Domain;
using Microsoft.Extensions.Logging;

namespace Explore.Infrastructure;

public sealed class StorageObjectDeletionService(
    IStorageObjectRepository storageObjectRepository,
    IFileStorageProviderResolver providerResolver,
    BusinessMetrics metrics,
    ILogger<StorageObjectDeletionService> logger) : IStorageObjectDeletionService
{
    public async Task<StorageObjectDeletionResult> DeleteRequestedForResourceAsync(
        Guid tenantId,
        string owningResourceKind,
        Guid owningResourceId,
        Guid? deletedBy,
        int limit,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A tenant id is required.", nameof(tenantId));
        }

        if (owningResourceId == Guid.Empty)
        {
            throw new ArgumentException("An owning resource id is required.", nameof(owningResourceId));
        }

        if (string.IsNullOrWhiteSpace(owningResourceKind))
        {
            throw new ArgumentException("An owning resource kind is required.", nameof(owningResourceKind));
        }

        if (limit <= 0)
        {
            return new StorageObjectDeletionResult(0, 0, 0, 0);
        }

        var storageObjects = await storageObjectRepository.ListDeleteRequestedForResourceAsync(
            tenantId,
            owningResourceKind,
            owningResourceId,
            limit,
            cancellationToken);

        var utcNow = DateTime.UtcNow;
        var deletedCount = 0;
        var missingKeyDeletedCount = 0;
        var failedCount = 0;

        foreach (var storageObject in storageObjects)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(storageObject.ObjectKey))
            {
                storageObject.MarkDeleted(deletedBy, utcNow);
                await storageObjectRepository.Update(storageObject);
                missingKeyDeletedCount++;
                continue;
            }

            try
            {
                var provider = providerResolver.GetRequired(storageObject.Provider);
                await provider.DeleteAsync(
                    new FileStorageDeleteInput(storageObject.ObjectKey),
                    cancellationToken);

                storageObject.MarkDeleted(deletedBy, utcNow);
                await storageObjectRepository.Update(storageObject);
                deletedCount++;
                metrics.RecordStorageDelete(storageObject.Provider, "succeeded");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException or SystemException)
            {
                failedCount++;
                var failureCategory = CategorizeProviderFailure(ex);
                metrics.RecordStorageDelete(storageObject.Provider, "failed", failureCategory);
                logger.LogWarning(
                    "Storage object delete request failed for provider {Provider}, tenant {TenantId}, resource kind {OwningResourceKind}, resource id {OwningResourceId}, storage object {StorageObjectId}, failure category {FailureCategory}.",
                    NormalizeProviderForLog(storageObject.Provider),
                    storageObject.TenantId,
                    owningResourceKind,
                    owningResourceId,
                    storageObject.Id,
                    failureCategory);
            }
        }

        return new StorageObjectDeletionResult(storageObjects.Count, deletedCount, missingKeyDeletedCount, failedCount);
    }

    private static string CategorizeProviderFailure(Exception exception)
        => exception switch
        {
            InvalidOperationException => "provider_unavailable",
            ArgumentException => "validation_failed",
            UnauthorizedAccessException => "access_denied",
            IOException => "delete_failed",
            _ => "delete_failed"
        };

    private static string NormalizeProviderForLog(string? provider)
        => provider switch
        {
            StorageProviders.Local => StorageProviders.Local,
            StorageProviders.S3Compatible => StorageProviders.S3Compatible,
            StorageProviders.LegacyExternal => StorageProviders.LegacyExternal,
            _ => "unknown"
        };
}
