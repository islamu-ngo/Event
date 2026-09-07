using Explore.Application.Contracts.Services;
using Explore.Application.Features.StorageObjects.Requests.Queries;
using Explore.Application.Models.Storage;
using MediatR;

namespace Explore.Application.Features.StorageObjects.Handlers.Queries;

public sealed class GetStorageObjectContentRequestHandler
    : IRequestHandler<GetStorageObjectContentRequest, StorageObjectContentResult?>
{
    private readonly IStorageObjectContentReader _contentReader;

    public GetStorageObjectContentRequestHandler(IStorageObjectContentReader contentReader)
    {
        _contentReader = contentReader;
    }

    public async Task<StorageObjectContentResult?> Handle(
        GetStorageObjectContentRequest request,
        CancellationToken cancellationToken)
    {
        return await _contentReader.OpenAsync(
            request.StorageObjectId,
            publicImagesOnly: false,
            cancellationToken);
    }
}
