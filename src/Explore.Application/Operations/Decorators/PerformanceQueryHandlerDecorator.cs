using Explore.Application.Contracts.Operations;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Operations.Decorators;

internal sealed class PerformanceQueryHandlerDecorator<TQuery, TResult>(
    IQueryHandler<TQuery, TResult> inner,
    TimeProvider clock,
    ILogger<PerformanceQueryHandlerDecorator<TQuery, TResult>> logger) : IQueryHandler<TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    public async Task<TResult> QueryAsync(TQuery query, CancellationToken cancellationToken)
    {
        var started = clock.GetTimestamp();
        var result = await inner.QueryAsync(query, cancellationToken);
        var elapsedMilliseconds = (long)clock.GetElapsedTime(started).TotalMilliseconds;
        if (elapsedMilliseconds > 500)
            logger.LogWarning("Long Running Request: {RequestType} ({ElapsedMilliseconds}ms)", typeof(TQuery).Name, elapsedMilliseconds);
        return result;
    }
}
