using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Secrets;

namespace Explore.Infrastructure.Services.Moderation;

/// <summary>
/// Resolves the dedicated instance ring through the existing approved secret contract.
/// ISecretResolver owns authority selection, environment restrictions, binding provenance,
/// bounded provider failures and cache invalidation. This adapter adds no source or cache.
/// Catalogue, authority adapter composition and registration remain native parent integration.
/// </summary>
public sealed class ModerationRecognitionKeyRingProvider(
    ISecretResolver secretResolver, TimeProvider timeProvider, IModerationRecognitionKeyLifecycleGuard lifecycle)
    : IModerationRecognitionKeyRingProvider, IModerationRecognitionKeyMaterialSource
{
    public async Task<ModerationRecognitionKeyRing> ResolveAsync(
        Guid trustDomainId, CancellationToken cancellationToken)
    {
        var ring = await ReadAsync(trustDomainId, cancellationToken).ConfigureAwait(false);
        try
        {
            await lifecycle.ValidateAsync(ring, cancellationToken).ConfigureAwait(false);
            return ring;
        }
        catch
        {
            ring.Dispose();
            throw;
        }
    }

    public async Task<ModerationRecognitionKeyRing> ObserveAsync(
        Guid trustDomainId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await secretResolver.InvalidateAsync(ModerationRecognitionKeyRing.SecretSettingKey,
            Explore.Domain.Enums.SecretScope.Instance, null, cancellationToken).ConfigureAwait(false);
        return await ReadAsync(trustDomainId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ModerationRecognitionKeyRing> ReadAsync(
        Guid trustDomainId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await secretResolver.ResolveAsync(
            ModerationRecognitionKeyRing.SecretSettingKey, tenantId: null, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!result.IsResolved || result.Secret is null)
            throw new InvalidOperationException("moderation_recognition_ring_unavailable");
        return ModerationRecognitionKeyRing.Parse(result.Secret, trustDomainId, timeProvider);
    }
}
