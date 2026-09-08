using Explore.Application.Configuration;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Exceptions;
using Explore.Application.Services;
using Explore.Domain;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Explore.API.HealthChecks;

public sealed class PrivacyErasureReadinessHealthCheck(
    IOptions<PrivacyErasureDurabilityOptions> durabilityOptions,
    IPrivacyErasureReplayCheckpointRepository checkpointRepository,
    IPrivacyErasureProviderWorkRepository providerWorkRepository,
    IOutboxRepository outboxRepository,
    IPrivacyErasureAuthority authority,
    TimeProvider timeProvider) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            long localSequence = (await checkpointRepository.GetLatestAsync(cancellationToken))
                ?.AuthoritySequence ?? 0;
            PrivacyErasureAuthorityState state = await authority.GetStateAsync(cancellationToken);
            string replayReasonCode;
            bool replayCaughtUp;
            if (localSequence < state.RetainedFloorSequence)
            {
                replayReasonCode = "stale_restore_below_retained_floor";
                replayCaughtUp = false;
            }
            else if (localSequence > state.HighWaterSequence)
            {
                replayReasonCode = "checkpoint_ahead_of_authority";
                replayCaughtUp = false;
            }
            else if (localSequence == state.HighWaterSequence)
            {
                replayReasonCode = "privacy_erasure_ready";
                replayCaughtUp = true;
            }
            else
            {
                IReadOnlyList<PrivacyErasureIntent> next = await authority.ReadAfterAsync(
                    localSequence,
                    1,
                    cancellationToken);
                bool gap = next.Count == 0 || next[0].AuthoritySequence != localSequence + 1;
                replayReasonCode = gap ? "sequence_gap_detected" : "replay_pending";
                replayCaughtUp = false;
            }
            DateTime nowUtc = timeProvider.GetUtcNow().UtcDateTime;
            int due = await providerWorkRepository.CountDueAsync(nowUtc, cancellationToken);
            int unknown = await providerWorkRepository.CountUnknownAsync(cancellationToken);
            int deadLettered = await providerWorkRepository.CountDeadLetteredAsync(cancellationToken);
            int cacheConvergenceIncomplete = await outboxRepository.CountIncompleteByEventTypeAsync(
                PrivacyErasureCacheInvalidationOutboxMessageFactory.EventType,
                cancellationToken);
            int cacheConvergenceDeadLettered = await outboxRepository.CountDeadLetteredByEventTypeAsync(
                PrivacyErasureCacheInvalidationOutboxMessageFactory.EventType,
                cancellationToken);
            var data = new Dictionary<string, object>
            {
                ["topology"] = durabilityOptions.Value.Topology.ToString(),
                ["restoreReplayProtection"] = durabilityOptions.Value.RestoreReplayProtection,
                ["authorityHighWater"] = state.HighWaterSequence,
                ["authorityRetainedFloor"] = state.RetainedFloorSequence,
                ["replayCaughtUp"] = replayCaughtUp,
                ["replayReasonCode"] = replayReasonCode,
                ["providerDue"] = due,
                ["providerUnknown"] = unknown,
                ["providerDeadLettered"] = deadLettered,
                ["cacheConvergenceIncomplete"] = cacheConvergenceIncomplete,
                ["cacheConvergenceDeadLettered"] = cacheConvergenceDeadLettered
            };

            if (replayReasonCode is "stale_restore_below_retained_floor"
                or "checkpoint_ahead_of_authority"
                or "sequence_gap_detected")
            {
                return HealthCheckResult.Unhealthy(replayReasonCode, data: data);
            }

            return replayCaughtUp
                && unknown == 0
                && deadLettered == 0
                && cacheConvergenceIncomplete == 0
                && cacheConvergenceDeadLettered == 0
                ? HealthCheckResult.Healthy("privacy_erasure_ready", data)
                : HealthCheckResult.Degraded("privacy_erasure_attention_required", data: data);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy("privacy_erasure_authority_unavailable");
        }
    }
}
