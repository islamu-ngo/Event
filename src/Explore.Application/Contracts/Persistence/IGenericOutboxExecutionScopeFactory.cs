namespace Explore.Application.Contracts.Persistence;

public interface IGenericOutboxExecutionScopeFactory
{
    ValueTask<IGenericOutboxExecutionScope> CreateAsync(
        GenericOutboxClaim claim, CancellationToken cancellationToken);
}

public interface IGenericOutboxExecutionScope : IAsyncDisposable
{
    IServiceProvider Services { get; }
}
