using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;

namespace Explore.Persistence.Services;

public sealed class InventoryHoldExpiryDiscovery(IGlobalExploreDbContextFactory globalFactory)
    : IInventoryHoldExpiryDiscovery
{
    public const int MaximumBatchSize = 100;

    public async Task<IReadOnlyList<InventoryHoldExpiryTarget>> GetTargetsAsync(
        DateTime utcNow, int batchSize, CancellationToken cancellationToken)
    {
        if (utcNow.Kind != DateTimeKind.Utc || batchSize is < 1 or > MaximumBatchSize)
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        await using var context = await globalFactory.CreateDbContextAsync(cancellationToken);
        if (context.TenantFilterTenantId is not null || context.IsTenantFilterBypassed)
            throw new InvalidOperationException("hold_expiry_unbound_global_discovery_required");
        return await InventoryHoldExpiryQuery.ReadAsync(context, utcNow, batchSize, cancellationToken);
    }
}
