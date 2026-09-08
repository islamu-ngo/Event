using Explore.Application.Contracts.Infrastructure;
using Microsoft.AspNetCore.OutputCaching;

namespace Explore.API.Services;

public sealed class EventReportingOutputCacheInvalidator(IOutputCacheStore outputCacheStore)
    : IEventReportingOutputCacheInvalidator
{
    public async Task InvalidateAsync(CancellationToken cancellationToken)
    {
        await outputCacheStore.EvictByTagAsync("detail-data", cancellationToken);
        await outputCacheStore.EvictByTagAsync("list-data", cancellationToken);
    }
}
