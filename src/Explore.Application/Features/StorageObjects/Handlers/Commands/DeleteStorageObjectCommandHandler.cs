using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.StorageObjects.Requests.Commands;
using Explore.Application.Models.Storage;
using Explore.Application.Telemetry;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.StorageObjects.Handlers.Commands;

public class DeleteStorageObjectCommandHandler : ICommandHandler<DeleteStorageObjectCommand, bool>
{
    private readonly IStorageObjectRepository _storageObjectRepository;
    private readonly IStorageProviderBindingService _providerResolver;
    private readonly BusinessMetrics _metrics;

    public DeleteStorageObjectCommandHandler(
        IStorageObjectRepository storageObjectRepository,
        IStorageProviderBindingService providerResolver,
        BusinessMetrics metrics)
    {
        _storageObjectRepository = storageObjectRepository;
        _providerResolver = providerResolver;
        _metrics = metrics;
    }

    public async Task<bool> ExecuteAsync(DeleteStorageObjectCommand request, CancellationToken cancellationToken)
    {
        var entity = await _storageObjectRepository.GetForGenericAccessAsync(request.Id, cancellationToken);

        if (entity == null)
        {
            _metrics.RecordStorageDelete(null, "failed", "metadata_not_found");
            return false;
        }

        if (await _storageObjectRepository.IsRetainedEvidenceAsync(entity.Id, cancellationToken))
        {
            _metrics.RecordStorageDelete(entity.Provider, "failed", "retained_evidence");
            return false;
        }

        if (string.IsNullOrWhiteSpace(entity.ObjectKey))
        {
            _metrics.RecordStorageDelete(entity.Provider, "failed", "missing_object_key");
            return false;
        }

        try
        {
            var provider = await _providerResolver.ResolveTargetAsync(
                entity.StorageProviderBindingId, entity.Provider, cancellationToken);
            var deleted = await provider.DeleteAsync(
                new FileStorageDeleteInput(entity.ObjectKey, entity.ProviderVersionId), cancellationToken);
            if (deleted.Provider != entity.Provider || deleted.ObjectKey != entity.ObjectKey || deleted.DeleteMarkerCreated)
                throw new InvalidOperationException("storage_deletion_unconfirmed");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _metrics.RecordStorageDelete(entity.Provider, "failed", CategorizeDeleteFailure(ex));
            return false;
        }

        await _storageObjectRepository.Delete(entity);

        _metrics.RecordStorageDelete(entity.Provider, "succeeded");

        return true;
    }

    private static string CategorizeDeleteFailure(Exception exception)
        => exception switch
        {
            InvalidOperationException => "provider_unavailable",
            ArgumentException => "delete_failed",
            IOException => "delete_failed",
            UnauthorizedAccessException => "access_denied",
            _ => "delete_failed"
        };
}
