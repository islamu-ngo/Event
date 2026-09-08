using Explore.Application.Models;

namespace Explore.Application.Contracts.Services;

public interface IIdempotencyCleanupService
{
    Task<IdempotencyCleanupResult> CleanupExpiredAsync(DateTime utcNow, CancellationToken cancellationToken = default);
}
