namespace Explore.Application.Contracts.Identity;

/// <summary>Retains exact credential receipt evidence before the original Global completion transaction.</summary>
public interface ILocalBootstrapReceiptSource
{
    Task<ILocalBootstrapReceiptLease> AcquireAsync(
        LocalCredentialProvisioningSnapshot expected, CancellationToken cancellationToken);

    /// <summary>Accepts only this issuer's active lease and its original immutable receipt.</summary>
    Task RequireCurrentAsync(ILocalBootstrapReceiptLease lease,
        LocalCredentialProvisioningSnapshot expected, CancellationToken cancellationToken);

    /// <summary>Rechecks issuer, retained custody and cancellation without I/O immediately before commit.</summary>
    void RequireHeld(ILocalBootstrapReceiptLease lease, LocalCredentialProvisioningSnapshot expected);
}

/// <summary>A retained source lifetime, not caller authorization or a transferable receipt grant.</summary>
public interface ILocalBootstrapReceiptLease : IAsyncDisposable;
