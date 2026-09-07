using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;

namespace Explore.API.BackgroundServices;

public static class PrivacyErasureStartupGate
{
    public static async Task RunAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);

        await using AsyncServiceScope scope = services.CreateAsyncScope();
        try
        {
            await scope.ServiceProvider
                .GetRequiredService<IPrivacyErasureReplayService>()
                .ReplayAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PrivacyErasureReplayException exception)
        {
            string reasonCode = exception switch
            {
                StaleRestoreBelowRetainedFloorException => "stale_restore_below_retained_floor",
                PrivacyErasureSequenceGapException => "sequence_gap_detected",
                PrivacyErasureCheckpointAheadException => "checkpoint_ahead_of_authority",
                _ => "privacy_erasure_replay_failed"
            };
            throw new InvalidOperationException(
                $"Privacy-erasure replay failed ({reasonCode}); API startup is blocked.");
        }
        catch (Exception)
        {
            throw new InvalidOperationException(
                "Privacy-erasure replay failed (privacy_erasure_replay_failed); API startup is blocked.");
        }
    }
}
