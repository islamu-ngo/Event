using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.StorageObjects.Requests.Queries;
using Explore.Application.Models.Storage;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.StorageObjects.Handlers.Queries;

public class GetPublicImageRequestHandler : IQueryHandler<GetPublicImageRequest, StorageObjectContentResult?>
{
    private readonly IStorageObjectContentReader _contentReader;
    private readonly IStorageObjectRepository _storageObjects;
    private readonly ITenantLifecycleAccessService _lifecycle;

    public GetPublicImageRequestHandler(
        IStorageObjectContentReader contentReader,
        IStorageObjectRepository storageObjects,
        ITenantLifecycleAccessService lifecycle)
    {
        _contentReader = contentReader;
        _storageObjects = storageObjects;
        _lifecycle = lifecycle;
    }

    public async Task<StorageObjectContentResult?> QueryAsync(
        GetPublicImageRequest request, CancellationToken cancellationToken)
    {
        var storageObject = await _storageObjects.GetById(request.StorageObjectId);
        if (storageObject is null || !await _lifecycle.IsPublicAsync(storageObject.TenantId, cancellationToken))
            return null;

        return await _contentReader.OpenAsync(
            request.StorageObjectId,
            publicImagesOnly: true,
            cancellationToken);
    }
}
