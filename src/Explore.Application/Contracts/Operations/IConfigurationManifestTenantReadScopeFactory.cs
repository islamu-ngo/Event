namespace Explore.Application.Contracts.Operations;

/// <summary>Acquires a fresh immutable ordinary lease for an authorized, server-selected export tenant.</summary>
public interface IConfigurationManifestTenantReadScopeFactory
{
    ValueTask<IConfigurationManifestTenantReadScope> CreateAsync(Guid tenantId, CancellationToken cancellationToken);
}

public interface IConfigurationManifestTenantReadScope : IAsyncDisposable
{
    IServiceProvider Services { get; }
}
