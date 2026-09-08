using Explore.Application.Models.Storage;

namespace Explore.Application.Contracts.Services;

public interface IStorageObjectContentReader
{
    Task<StorageObjectContentResult?> OpenAsync(
        Guid storageObjectId,
        bool publicImagesOnly,
        CancellationToken cancellationToken);
}
