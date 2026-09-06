// ABOUTME: Defines instance-wide insert-only replay claims for transient-service assertions.
// ABOUTME: Exposes only claim and bounded expiry deletion operations, never plaintext identifiers.

using Explore.Domain;
namespace Explore.Application.Contracts.Persistence;

public interface IAtprotoTransientAssertionReplayRepository
{
    Task<bool> TryClaimAsync(AtprotoTransientAssertionReplay replay, CancellationToken cancellationToken = default);
    Task<int> DeleteExpiredAsync(long expiresAtOrBeforeUnixMilliseconds, int batchSize, CancellationToken cancellationToken = default);
}
