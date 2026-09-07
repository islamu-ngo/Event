using Explore.Application.Models.Storage;

namespace Explore.Application.Contracts.Services;

public interface IStorageReconciliationService
{
    Task<StorageReconciliationResult> ReconcileAsync(
        DateTime utcNow,
        CancellationToken cancellationToken = default);
}
