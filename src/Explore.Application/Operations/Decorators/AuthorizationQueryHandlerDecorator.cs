using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Operations.Decorators;

internal sealed class AuthorizationQueryHandlerDecorator<TQuery, TResult>(
    IQueryHandler<TQuery, TResult> inner,
    RequestAuthorization<TQuery> authorization,
    ILogger<RequestAuthorization<TQuery>> logger) : IQueryHandler<TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    public async Task<TResult> QueryAsync(TQuery query, CancellationToken cancellationToken)
    {
        await authorization.AuthorizeAsync(query, logger, cancellationToken);
        return await inner.QueryAsync(query, cancellationToken);
    }
}
