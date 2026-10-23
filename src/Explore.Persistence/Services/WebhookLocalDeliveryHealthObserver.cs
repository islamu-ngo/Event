using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;

namespace Explore.Persistence.Services;

public sealed class WebhookLocalDeliveryHealthObserver(
    IGlobalExploreDbContextFactory globalFactory) : IWebhookLocalDeliveryHealthObserver
{
    public async Task<WebhookLocalDeliveryHealth> ObserveAsync(
        DateTimeOffset observedAtUtc, CancellationToken cancellationToken)
    {
        if (observedAtUtc == default || observedAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Local webhook health requires a non-default UTC observation.", nameof(observedAtUtc));

        await using var context = await globalFactory.CreateDbContextAsync(cancellationToken);
        if (context.BindTenantLease() is not null || context.IsTenantFilterBypassed ||
            context.CurrentUserService is not null)
            throw new InvalidOperationException("webhook_health_requires_unbound_global_lease");
        return await WebhookLocalDeliveryHealthQuery.ReadAsync(context, observedAtUtc, cancellationToken);
    }
}
