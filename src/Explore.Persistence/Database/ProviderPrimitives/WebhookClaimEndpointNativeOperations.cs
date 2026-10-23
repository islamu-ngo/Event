using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal sealed class WebhookClaimEndpointNativeOperations(ExploreDbContext context)
{
    internal static WebhookClaimEndpointNativeOperations? For(ExploreDbContext context) =>
        context.Database.IsNpgsql() ? new(context) : null;

    internal Task<WebhookEndpointFailureState?> ReadAsync(
        Guid tenantId, Guid targetId, Guid leaseToken, long deliveryFence,
        DateTimeOffset observedAtUtc, CancellationToken cancellationToken) =>
        PostgresWebhookClaimEndpointStateContract.InvokeAsync(
            context, tenantId, targetId, leaseToken, deliveryFence, null,
            observedAtUtc, 0, null, 0, cancellationToken);

    internal async Task<bool> MarkSuccessAsync(
        Guid tenantId, Guid endpointId, Guid targetId, Guid leaseToken, long deliveryFence,
        DateTimeOffset observedAtUtc, CancellationToken cancellationToken) =>
        await PostgresWebhookClaimEndpointStateContract.InvokeAsync(
            context, tenantId, targetId, leaseToken, deliveryFence, endpointId,
            observedAtUtc, 1, null, 0, cancellationToken) is not null;

    internal Task<WebhookEndpointFailureState?> RecordFailureAsync(
        Guid tenantId, Guid endpointId, Guid targetId, Guid leaseToken, long deliveryFence,
        DateTimeOffset failedAtUtc, string normalizedFailureCategory, int autoPauseThreshold,
        CancellationToken cancellationToken) =>
        PostgresWebhookClaimEndpointStateContract.InvokeAsync(
            context, tenantId, targetId, leaseToken, deliveryFence, endpointId,
            failedAtUtc, 2, normalizedFailureCategory, autoPauseThreshold, cancellationToken);
}
