namespace Explore.Application.Contracts.Identity;

/// <summary>Selects the configured identity-writing purpose before constructing a Local lifecycle graph.</summary>
public interface ILocalIdentityExecutionScopeFactory
{
    ValueTask<ILocalIdentityExecutionScope> CreateAsync(CancellationToken cancellationToken);
}

/// <summary>Owns construction services for one original Local lifecycle; it does not authorize the caller.</summary>
public interface ILocalIdentityExecutionScope : IAsyncDisposable
{
    IServiceProvider Services { get; }
}
