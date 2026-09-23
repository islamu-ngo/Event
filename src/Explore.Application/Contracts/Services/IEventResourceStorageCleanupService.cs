using Explore.Application.Models.Storage;

namespace Explore.Application.Contracts.Services;

public interface IEventResourceStorageCleanupService
{
    Task<StorageObjectDeletionResult> ProcessDueAsync(int limit, bool dryRun, CancellationToken cancellationToken);
}
