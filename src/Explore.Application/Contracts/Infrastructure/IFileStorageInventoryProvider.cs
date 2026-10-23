using Explore.Application.Models.Storage;
using Explore.Application.Contracts.Persistence;

namespace Explore.Application.Contracts.Infrastructure;

public interface IFileStorageInventoryProvider : IFileStorageProvider
{
    IAsyncEnumerable<FileStorageInventoryObject> ListObjectsAsync(
        int limit,
        CancellationToken cancellationToken);

    Task<FileStorageQuarantineResult> QuarantineAsync(
        FileStorageQuarantineInput input,
        ILocalStorageQuarantineOwner custody,
        TimeProvider clock,
        CancellationToken cancellationToken);
}
