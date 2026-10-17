using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Application.Operations;

internal sealed class HumanScopeQueryHandler<TQuery, TResult>(
    IServiceProvider provider, Type implementation) : IQueryHandler<TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    public async Task<TResult> QueryAsync(TQuery query, CancellationToken cancellationToken)
    {
        if (FooterOperationScope.Handles(typeof(TQuery)))
        {
            var selection = FooterOperationScope.Select(query, provider.GetService<ITenantContextAccessor>());
            await using var scope = await provider.GetRequiredService<IFooterExecutionScopeFactory>()
                .CreateAsync(selection, cancellationToken);
            return await scope.Services.GetRequiredService<IFooterOperationExecution>()
                .ExecuteAsync(query, token => QueryCoreAsync(scope.Services, query, token), cancellationToken);
        }
        await provider.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(cancellationToken);
        return await QueryCoreAsync(provider, query, cancellationToken);
    }

    private async Task<TResult> QueryCoreAsync(IServiceProvider services, TQuery query, CancellationToken cancellationToken)
    {
        var guard = services.GetRequiredService<OperationConstructionGuard>();
        var contract = typeof(IQueryHandler<TQuery, TResult>);
        guard.Enter(contract);
        IQueryHandler<TQuery, TResult> handler;
        try
        {
            handler = (IQueryHandler<TQuery, TResult>)services.GetRequiredService(implementation);
        }
        finally
        {
            guard.Exit(contract);
        }
        return await handler.QueryAsync(query, cancellationToken);
    }
}
