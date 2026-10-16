namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Excludes changes to global role-permission membership and persisted authorization-route
/// keys, including absent rows. Acquires only inside the selected business transaction,
/// before reading its policy sources. It neither makes nor retains an authorization decision.
/// </summary>
public interface IPolicySourceSetFence
{
    Task<IPolicySourceSetLease> AcquireAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// A transaction-bound source-set fence, not a permission or a provider response.
/// Locks end with the original transaction, never with an application-level disposal.
/// The owner must validate after its last blocking finalization and before commit.
/// </summary>
public interface IPolicySourceSetLease
{
    Task ValidateAsync(CancellationToken cancellationToken = default);
}
