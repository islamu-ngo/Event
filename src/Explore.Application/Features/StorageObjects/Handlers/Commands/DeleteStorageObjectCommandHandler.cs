using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.StorageObjects.Requests.Commands;
using Explore.Application.Models.Storage;
using Explore.Application.Telemetry;
using MediatR;

namespace Explore.Application.Features.StorageObjects.Handlers.Commands;

public class DeleteStorageObjectCommandHandler : IRequestHandler<DeleteStorageObjectCommand, bool>
{
    private readonly IStorageObjectRepository _storageObjectRepository;
    private readonly IFileStorageProviderResolver _providerResolver;
    private readonly BusinessMetrics _metrics;

    public DeleteStorageObjectCommandHandler(
        IStorageObjectRepository storageObjectRepository,
        IFileStorageProviderResolver providerResolver,
        BusinessMetrics metrics)
    {
        _storageObjectRepository = storageObjectRepository;
        _providerResolver = providerResolver;
        _metrics = metrics;
    }

    public async Task<bool> Handle(DeleteStorageObjectCommand request, CancellationToken cancellationToken)
    {
        var entity = await _storageObjectRepository.GetById(request.Id);

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
            var provider = _providerResolver.GetRequired(entity.Provider);
            await provider.DeleteAsync(new FileStorageDeleteInput(entity.ObjectKey), cancellationToken);
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
