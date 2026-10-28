using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Persistence;
using Explore.Domain.Constants;

namespace Explore.Application.Services.Moderation;

/// <summary>Retries durable owner delivery separately from matching and erasure transactions.</summary>
public sealed class ModerationRecognitionRecoveryScheduler(
    IModerationRecognitionErasureRepository requests,
    IModerationRecognitionRecoveryRepository recovery,
    IModerationRecognitionRecoveryNotifications notifications,
    ISettingMutationLock settings, TimeProvider clock,
    IModerationRecognitionKeyLifecycleRepository lifecycle)
{
    public async Task ForgetExpiredReceiptsAsync(CancellationToken token)
    {
        Guid? after = null;
        while (true)
        {
            var removed = await lifecycle.ForgetExpiredReceiptsAsync(after, 100, token);
            if (removed.LastInspectedIntentId is null) break;
            after = removed.LastInspectedIntentId;
        }
    }

    public async Task PruneExpiredMaterialAsync(CancellationToken token)
    {
        Guid? after = null;
        while (true)
        {
            var pruned = await lifecycle.PruneAsync(after, 100, token);
            if (pruned.LastInspectedIntentId is null) break;
            after = pruned.LastInspectedIntentId;
        }
    }

    public async Task ReconcileAsync(CancellationToken token)
    {
        Task EscalateAsync(Guid intentId, Guid? actionId) =>
            settings.ExecuteOrderedGroupsAsync([[GovernanceSettingKeys.Email.DeliveryEnabled]], async ct =>
            {
                await recovery.EscalateAsync(intentId, actionId, notifications, ct);
                return true;
            }, token);

        Guid? after = null;
        while (true)
        {
            var pending = await requests.ReadReceivedAfterAsync(after, 100, token);
            if (pending.Count == 0) break;
            foreach (var request in pending)
            {
                if (clock.GetUtcNow() >= request.EscalateAt)
                    await EscalateAsync(request.IntentId, null);
                after = request.IntentId;
            }
        }
        after = null;
        while (true)
        {
            var pending = await requests.ReadActionRecoveryAfterAsync(after, 100, token);
            if (pending.Count == 0) break;
            foreach (var action in pending)
            {
                await EscalateAsync(action.OriginalIntentId, action.Id);
                after = action.Id;
            }
        }
    }
}
