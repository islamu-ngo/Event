using Explore.Application.Contracts.PrivacyErasure;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Owns one Serializable attempt without replaying a prepared authorization decision.
/// Rollback-known conflicts must restart preparation outside all database leases.
/// </summary>
public interface ISerializableAttemptOwner
{
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default);

    /// <summary>Retains the original privacy gate through this owner's physical settlement without replay.</summary>
    Task<T> ExecuteWithOriginalPrivacyAsync<T>(IOriginalPrivacyIdentityFenceAuthority privacy,
        Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default);
}
