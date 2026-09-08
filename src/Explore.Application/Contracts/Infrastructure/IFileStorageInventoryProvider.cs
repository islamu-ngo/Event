using Explore.Application.Models.Storage;

namespace Explore.Application.Contracts.Infrastructure;

public interface IFileStorageInventoryProvider : IFileStorageProvider
{
    IAsyncEnumerable<FileStorageInventoryObject> ListObjectsAsync(
        int limit,
        CancellationToken cancellationToken);

    Task<FileStorageQuarantineResult> QuarantineAsync(
        FileStorageQuarantineInput input,
        CancellationToken cancellationToken);
}
