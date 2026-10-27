using Explore.Domain;

namespace Explore.Application.Contracts.PrivacyErasure;

/// <summary>
/// Implemented by the same retained authority instance that owns
/// <see cref="IPrivacyIdentityFenceAuthority"/>. Call only inside that owner's
/// existing serialized callback, acquired before any primary transaction.
/// This port never opens a late writer gate or supplies measured recovery proof.
/// </summary>
public interface IPrivacyErasureRecoveryAuthority
{
    /// <summary>
    /// Acquires the exact committed tuple while retaining its native writer gate.
    /// Fails if the store was initialized or mutated in the active transaction.
    /// The value is not an authority lease after the callback ends; reacquire
    /// inside the same callback for a terminal check before primary commit.
    /// </summary>
    Task<PrivacyErasureRecoveryState> AcquireRecoveryStateAsync(CancellationToken cancellationToken);
}
