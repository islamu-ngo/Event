using Explore.Application.Caching;
using Explore.Application.Services;
using Explore.Domain;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Infrastructure.Messaging;

public sealed class PrivacyErasureCacheInvalidationDispatcher(HybridCache cache)
{
    public async Task DispatchAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.EventType != PrivacyErasureCacheInvalidationOutboxMessageFactory.EventType
            || message.AggregateType != nameof(User)
            || message.AggregateId == Guid.Empty
            || message.Payload is not null)
        {
            throw new InvalidOperationException("Privacy-erasure cache work failed closed-schema validation.");
        }

        await cache.RemoveAsync($"user:detail:{message.AggregateId}", cancellationToken);
        await cache.RemoveByTagAsync(CacheTags.Events, cancellationToken);
        await cache.RemoveByTagAsync(CacheTags.EventLists, cancellationToken);
        await cache.RemoveByTagAsync(CacheTags.EventDetails, cancellationToken);
        await cache.RemoveByTagAsync(CacheTags.EventLocations, cancellationToken);
    }
}
