namespace Explore.Application.Contracts.Persistence;

/// <summary>Bounded persisted work identities only; discovery confers no business mutation authority.</summary>
public interface IInventoryHoldExpiryDiscovery
{
    Task<IReadOnlyList<InventoryHoldExpiryTarget>> GetTargetsAsync(
        DateTime utcNow, int batchSize, CancellationToken cancellationToken);
}

/// <summary>A null hold identifies an interrupted lifecycle recovery, not a hold to expire.</summary>
public sealed record InventoryHoldExpiryTarget(Guid TenantId, Guid OrderId, Guid? HoldId);

public interface IInventoryHoldExpiryExecutionScopeFactory
{
    ValueTask<IInventoryHoldExpiryExecutionScope> CreateAsync(Guid tenantId, CancellationToken cancellationToken);
}

/// <summary>Owns one immutable Runtime tenant lease and all dependencies resolved from it.</summary>
public interface IInventoryHoldExpiryExecutionScope : IAsyncDisposable
{
    IServiceProvider Services { get; }
}
