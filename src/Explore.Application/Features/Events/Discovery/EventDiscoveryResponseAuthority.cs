using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;

namespace Explore.Application.Features.Events.Discovery;

/// <summary>Revalidates prepared payload and HAL evidence at the final response authorization boundary.</summary>
public sealed class EventDiscoveryResponseAuthority(
    IUnitOfWork unitOfWork,
    IEventDiscoveryDisclosureRepository disclosure,
    IEventDiscoveryIdentityRepository identities,
    ITenantContext tenant,
    TimeProvider clock,
    IEventDiscoveryResponseBoundaryRepository boundaries)
{
    public async Task<EventDiscoveryReadStamp> CaptureAsync(
        CancellationToken cancellationToken, bool includeUtcDateBoundary = false)
    {
        var observedAt = clock.GetUtcNow();
        var current = await identities.GetRevisionAsync(tenant.TenantId, cancellationToken)
            ?? await unitOfWork.ExecuteReadCommittedAsync(
                token => disclosure.AcquireCurrentAsync(tenant.TenantId, token), cancellationToken);
        var validUntil = await boundaries.GetNextAsync(tenant.TenantId, observedAt, cancellationToken)
            ?? DateTimeOffset.MaxValue;
        if (includeUtcDateBoundary)
        {
            var nextDate = new DateTimeOffset(observedAt.UtcDateTime.Date.AddDays(1));
            if (nextDate < validUntil)
                validUntil = nextDate;
        }
        return new(tenant.TenantId, current.IdentityEpoch, current.DisclosureEpoch, validUntil);
    }

    public async Task ValidateAsync(EventDiscoveryReadStamp stamp, CancellationToken cancellationToken)
    {
        if (stamp.TenantId != tenant.TenantId)
            throw new EventDiscoveryUnavailableException();

        await unitOfWork.ExecuteReadCommittedAsync(async token =>
        {
            var current = await disclosure.AcquireCurrentAsync(stamp.TenantId, token);
            if (current.IdentityEpoch != stamp.IdentityEpoch || current.DisclosureEpoch != stamp.DisclosureEpoch)
                throw new EventDiscoveryRestartRequiredException();
            if (clock.GetUtcNow() >= stamp.ValidUntilUtc)
                throw new EventDiscoveryCursorExpiredException();
            return true;
        }, cancellationToken);
    }
}
