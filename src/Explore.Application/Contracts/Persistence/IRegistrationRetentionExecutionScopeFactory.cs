namespace Explore.Application.Contracts.Persistence;

/// <summary>Selects ordinary tenant authority before constructing cleanup dependencies.</summary>
public interface IRegistrationRetentionExecutionScopeFactory
{
    Task<IRegistrationRetentionExecutionScope> CreateAsync(Guid tenantId, CancellationToken cancellationToken);
}

/// <summary>The tenant is fixed for this scope; cleanup cannot substitute another owner.</summary>
public interface IRegistrationRetentionExecutionScope : IAsyncDisposable
{
    Task<RegistrationRetentionCleanupResult> CleanupAsync(
        DateTime utcNow, int batchSize, CancellationToken cancellationToken);
}
