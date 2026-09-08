using Explore.Domain;
namespace Explore.Application.Contracts.Persistence;

public interface IAtprotoTransientAssertionReplayRepository
{
    Task<bool> TryClaimAsync(AtprotoTransientAssertionReplay replay, CancellationToken cancellationToken = default);
    Task<int> DeleteExpiredAsync(long expiresAtOrBeforeUnixMilliseconds, int batchSize, CancellationToken cancellationToken = default);
}
