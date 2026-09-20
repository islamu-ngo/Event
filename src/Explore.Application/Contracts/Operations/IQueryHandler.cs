namespace Explore.Application.Contracts.Operations;

public interface IQueryHandler<in TQuery, TResult> where TQuery : IQuery<TResult>
{
    Task<TResult> QueryAsync(TQuery query, CancellationToken cancellationToken = default);
}
