using Explore.Application.Models;

namespace Explore.Application.Contracts.Services;

public interface IAiRetentionCleanupService
{
    Task<AiRetentionCleanupRunResult> CleanupAllTenantsAsync(
        DateTime utcNow,
        CancellationToken cancellationToken = default);
}
