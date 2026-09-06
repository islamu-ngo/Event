// ABOUTME: Sweeps expired ATProto authentication transients and assertion replay claims.
// ABOUTME: Retains replay claims across replica clock drift and bounds each store to five batches of five hundred rows.

using Explore.Application.Contracts.Persistence;

namespace Explore.Infrastructure;

public sealed class AtprotoTransientCleanupService(
    IAtprotoTransientStoreRepository transientRepository,
    IAtprotoTransientAssertionReplayRepository replayRepository,
    TimeProvider timeProvider)
{
    // Hosts stay within five seconds of trusted UTC, so replicas can differ by ten seconds.
    private const long MaximumReplicaClockDifferenceMilliseconds = 10_000;

    public async Task<(int TransientRows, int ReplayRows)> CleanupExpiredAsync(CancellationToken cancellationToken = default)
    {
        const int batchSize = 500;
        const int batchesPerStore = 5;
        long cutoff = timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        long replayCutoff = cutoff - MaximumReplicaClockDifferenceMilliseconds;
        int transientRows = 0;
        int replayRows = 0;
        for (int batch = 0; batch < batchesPerStore; batch++)
        {
            int deleted = await transientRepository.DeleteExpiredAsync(cutoff, batchSize, cancellationToken).ConfigureAwait(false);
            transientRows += deleted;
            if (deleted < batchSize) break;
        }
        for (int batch = 0; batch < batchesPerStore; batch++)
        {
            int deleted = await replayRepository.DeleteExpiredAsync(replayCutoff, batchSize, cancellationToken).ConfigureAwait(false);
            replayRows += deleted;
            if (deleted < batchSize) break;
        }
        return (transientRows, replayRows);
    }
}
