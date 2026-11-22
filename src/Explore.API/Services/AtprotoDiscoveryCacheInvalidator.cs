using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Caching;
using Microsoft.AspNetCore.OutputCaching;

namespace Explore.API.Services;

public sealed class AtprotoDiscoveryCacheInvalidator(IOutputCacheStore outputCacheStore)
    : IAtprotoDiscoveryCacheInvalidator
{
    public async ValueTask InvalidateAsync(CancellationToken cancellationToken = default)
    {
        await outputCacheStore.EvictByTagAsync(CacheTags.TenantPublicReads, cancellationToken);
        await outputCacheStore.EvictByTagAsync("event-discovery", cancellationToken);
        await outputCacheStore.EvictByTagAsync("public-home-discovery", cancellationToken);
        await outputCacheStore.EvictByTagAsync("list-data", cancellationToken);
        await outputCacheStore.EvictByTagAsync("detail-data", cancellationToken);
        await outputCacheStore.EvictByTagAsync("seo-sitemap", cancellationToken);
    }
}
