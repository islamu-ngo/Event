using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class WebhookLocalDeliveryHealthQuery
{
    internal static async Task<WebhookLocalDeliveryHealth> ReadAsync(
        ExploreDbContext context, DateTimeOffset observedAtUtc, CancellationToken cancellationToken)
    {
        if (context.Database.IsNpgsql())
            return await PostgresWebhookLocalDeliveryHealthContract.ObserveAsync(context, observedAtUtc, cancellationToken);

        // The bypass belongs only to this independent Global lease. Named soft-delete
        // filters remain active; portable providers retain the existing queue predicates.
        var targets = context.WebhookLocalTargetSnapshots
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookLocalDeliveryHealth)
            .AsNoTracking();
        int due = await targets.CountAsync(target =>
            (target.DeliveryStatusId == (int)WebhookLocalDeliveryStatus.Pending ||
             target.DeliveryStatusId == (int)WebhookLocalDeliveryStatus.RetryDue) &&
            target.NextActionAtUtc <= observedAtUtc &&
            target.WebhookEndpoint.StatusId == (int)WebhookEndpointStatus.Active, cancellationToken);
        int stale = await targets.CountAsync(target =>
            target.DeliveryStatusId == (int)WebhookLocalDeliveryStatus.Delivering &&
            target.ProcessingLeaseExpiresAtUtc != null &&
            target.ProcessingLeaseExpiresAtUtc <= observedAtUtc, cancellationToken);
        return new(due, stale);
    }
}
