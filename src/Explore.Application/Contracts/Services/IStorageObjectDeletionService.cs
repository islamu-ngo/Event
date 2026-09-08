using Explore.Application.Models.Storage;

namespace Explore.Application.Contracts.Services;

public interface IStorageObjectDeletionService
{
    Task<StorageObjectDeletionResult> DeleteRequestedForResourceAsync(
        Guid tenantId,
        string owningResourceKind,
        Guid owningResourceId,
        Guid? deletedBy,
        int limit,
        CancellationToken cancellationToken);
}
