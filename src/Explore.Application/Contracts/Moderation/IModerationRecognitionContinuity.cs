using Explore.Domain;

namespace Explore.Application.Contracts.Moderation;

/// <summary>
/// Recognition only, never authentication, activation or terminal enforcement permission.
/// The trusted caller supplies the exact freshly authenticated native ProviderAccountKey.
/// </summary>
public interface IModerationRecognitionContinuity
{
    /// <summary>
    /// Outermost boundary: call BEFORE entering PrivacyIdentityFenceOperation and BEFORE
    /// opening the primary transaction. Keep it through commit. Do not nest it inside the
    /// erasure coordinator's setting lock; that coordinator already owns the same boundary.
    /// </summary>
    Task<T> ExecuteWithRecognitionLockAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken token);

    /// <summary>
    /// Call inside the retained identity gate and original serializable primary transaction,
    /// before other subject/authority row locks. Bindings are staged, not independently saved.
    /// On closed results abort the affected operation; caller owns SaveChanges and commit.
    /// Commit before CurrentUntil, or reacquire and revalidate the entire operation.
    /// </summary>
    Task<ModerationRecognitionContinuityResult> MatchAndBindAsync(
        ModerationRecognitionContinuityRequest request, CancellationToken token);

    /// <summary>
    /// Keyless existing UUID guard, including Local. Requires the identity gate and serializable
    /// primary transaction. It neither proves provider matching nor acquires the outer setting lock.
    /// </summary>
    Task<ModerationRecognitionContinuityFailure> GuardExistingSubjectAsync(Guid subjectId, CancellationToken token);

}
