using Explore.Application.Contracts.Services;
using Explore.Application.Features.StorageObjects.Requests.Queries;
using Explore.Application.Models.Storage;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.StorageObjects.Handlers.Queries;

public class GetPublicImageRequestHandler : IQueryHandler<GetPublicImageRequest, StorageObjectContentResult?>
{
    private readonly IStorageObjectContentReader _contentReader;

    public GetPublicImageRequestHandler(IStorageObjectContentReader contentReader)
    {
        _contentReader = contentReader;
    }

    public async Task<StorageObjectContentResult?> QueryAsync(
        GetPublicImageRequest request, CancellationToken cancellationToken)
    {
        return await _contentReader.OpenAsync(
            request.StorageObjectId,
            publicImagesOnly: true,
            cancellationToken);
    }
}
