using Explore.Domain;

namespace Explore.Application.Contracts.PrivacyErasure;

/// <summary>Serializes binding capture and enrollment through its application commit.</summary>
public interface IPrivacyIdentityFenceAuthority
{
    Task<T> ExecuteSerializedAsync<T>(
        Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken);

    /// <summary>
    /// Enrollment supplies validation for the final primary commit when the selected
    /// authority adopts that transaction. Separate-primary callers must also validate
    /// at their own native commit boundary; returning from a callback is not a commit.
    /// </summary>
    Task<T> ExecuteEnrollmentSerializedAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        Func<CancellationToken, Task> beforePrimaryCommit,
        CancellationToken cancellationToken);

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

    /// <summary>Retains primary-owner continuity through a separately owning privacy gate's actual commit.</summary>
    Task<T> ExecuteOriginalEnrollmentAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        Func<CancellationToken, Task> beforePrimaryCommit,
        Action<DateTimeOffset> requireCurrent,
        CancellationToken cancellationToken);
}
