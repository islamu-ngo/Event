using Explore.Domain;

namespace Explore.Application.Contracts.PrivacyErasure;

/// <summary>Serializes binding capture and enrollment through its application commit.</summary>
public interface IPrivacyIdentityFenceAuthority
{
    Task<T> ExecuteSerializedAsync<T>(
        Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken);

    Task ValidateKeyAsync(string keyId, string verificationTag, CancellationToken cancellationToken);

    Task<PrivacyErasureIntent?> FindAsync(
        PrivacyIdentityFingerprint fingerprint, CancellationToken cancellationToken);

    Task<bool> IsSubjectFencedAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>
/// Starts an original enrollment gate and refuses an inherited gate. Human physical
/// settlement retains it through disposal rather than borrowing a worker invocation's scope.
/// </summary>
public interface IOriginalPrivacyIdentityFenceAuthority : IPrivacyIdentityFenceAuthority
{
    Task<T> ExecuteOriginalEnrollmentAsync<T>(
        Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken);
}
