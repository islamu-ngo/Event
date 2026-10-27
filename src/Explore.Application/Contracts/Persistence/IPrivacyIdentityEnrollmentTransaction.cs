namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// The native primary transaction owner saves recognition and binding writes together,
/// then validates after disclosure flush immediately before commit. When the retained
/// authority adopts the primary transaction, its final owner validates at that commit.
/// </summary>
public interface IPrivacyIdentityEnrollmentTransaction
{
    Task<T> ExecuteRecognitionEnrollmentAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        Func<CancellationToken, Task> beforeCommit,
        CancellationToken ct = default);
}

public static class PrivacyIdentityEnrollmentTransaction
{
    public static Task<T> ExecuteRecognitionEnrollmentAsync<T>(this IUnitOfWork owner,
        Func<CancellationToken, Task<T>> operation,
        Func<CancellationToken, Task> beforeCommit,
        CancellationToken ct = default) =>
        owner is IPrivacyIdentityEnrollmentTransaction enrollment
            ? enrollment.ExecuteRecognitionEnrollmentAsync(operation, beforeCommit, ct)
            : throw new InvalidOperationException("Identity enrollment requires a native pre-commit transaction owner.");
}
