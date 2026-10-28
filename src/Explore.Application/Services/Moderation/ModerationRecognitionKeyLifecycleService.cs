using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;

namespace Explore.Application.Services.Moderation;

/// <summary>
/// Native maintenance orchestration. Provisioning stages only material actually obtained from the
/// selected resolver; acknowledgement independently refreshes that source after durable staging.
/// Changing secret material remains the selected authority's own operator workflow.
/// </summary>
public sealed class ModerationRecognitionKeyLifecycleService(
    IModerationRecognitionKeyMaterialSource material, IModerationRecognitionKeyLifecycleRepository repository,
    ISettingMutationLock settings, TimeProvider clock)
{
    public Task<ModerationRecognitionKeyLifecycle> InitializeAsync(Guid trustDomainId, CancellationToken token) =>
        ObserveUnderLeaseAsync(trustDomainId, (ring, verify, ct) => repository.InitializeAsync(ring, verify, ct), token);

    public Task<ModerationRecognitionKeyLifecycle> StageAsync(Guid trustDomainId, long expectedRevision, CancellationToken token) =>
        ObserveUnderLeaseAsync(trustDomainId, (ring, verify, ct) => repository.StageAsync(ring, expectedRevision, verify, ct), token);

    public Task<KeyReplica> AcknowledgeAsync(Guid trustDomainId, long expectedRevision, CancellationToken token) =>
        ObserveUnderLeaseAsync(trustDomainId, (ring, verify, ct) => repository.AcknowledgeAsync(ring, expectedRevision, verify, ct), token);

    public Task<ModerationRecognitionKeyLifecycle> SwitchAsync(Guid trustDomainId, string keyId,
        long expectedRevision, CancellationToken token) =>
        ObserveUnderLeaseAsync(trustDomainId, (ring, verify, ct) =>
            repository.SwitchAsync(ring, keyId, expectedRevision, verify, ct), token);

    public Task<ModerationRecognitionKeyLifecycle> RetireAsync(Guid trustDomainId, string keyId,
        long expectedRevision, CancellationToken token) =>
        ObserveUnderLeaseAsync(trustDomainId, (ring, verify, ct) =>
            repository.RetireAsync(ring, keyId, expectedRevision, verify, ct), token);

    public Task<ModerationRecognitionKeyLifecycle> CompromiseAsync(Guid trustDomainId, string keyId,
        long expectedRevision, CancellationToken token) =>
        settings.ExecuteOrderedGroupsAsync([[ModerationRecognitionKeyRing.SecretSettingKey]],
            ct => repository.CompromiseAsync(trustDomainId, keyId, expectedRevision, ct), token);

    private Task<T> ObserveUnderLeaseAsync<T>(Guid trustDomainId,
        Func<ModerationRecognitionKeyRing, Func<CancellationToken, Task>, CancellationToken, Task<T>> operation,
        CancellationToken token) =>
        settings.ExecuteOrderedGroupsAsync([[ModerationRecognitionKeyRing.SecretSettingKey]], async ct =>
        {
            using var ring = await material.ObserveAsync(trustDomainId, ct).ConfigureAwait(false);
            return await operation(ring, async terminalToken =>
            {
                // Binding locks alone cannot fence an external secret edit. Re-read actual
                // selected bytes at both original primary and selected-authority commit boundaries.
                using var current = await material.ObserveAsync(trustDomainId, terminalToken).ConfigureAwait(false);
                var now = clock.GetUtcNow();
                if (now < ring.ObservedAt || now >= ring.CurrentUntil || now >= current.CurrentUntil
                    || current.TrustDomainId != ring.TrustDomainId
                    || current.Source != ring.Source || current.BindingRevision != ring.BindingRevision
                    || current.RingGeneration != ring.RingGeneration || current.Keys.Length != ring.Keys.Length
                    || !current.Keys.OrderBy(row => row.KeyGeneration).SequenceEqual(ring.Keys.OrderBy(row => row.KeyGeneration)))
                    throw new InvalidOperationException("moderation_recognition_selected_source_changed");
            }, ct).ConfigureAwait(false);
        }, token);
}
