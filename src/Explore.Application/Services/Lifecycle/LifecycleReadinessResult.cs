namespace Explore.Application.Services.Lifecycle;

/// <summary>
/// Result of evaluating lifecycle readiness against a validation profile and effective policy.
/// </summary>
/// <param name="IsReady">True only when no errors with <see cref="ReadinessErrorSeverity.Error"/> are present.</param>
/// <param name="Errors">All diagnostics, including warnings and informational notes.</param>
/// <param name="Profile">Validation profile that was evaluated.</param>
public sealed record LifecycleReadinessResult(
    bool IsReady,
    IReadOnlyList<LifecycleReadinessError> Errors,
    ValidationProfile Profile)
{
    /// <summary>
    /// Returns only the blocking errors (severity = Error).
    /// </summary>
    public IReadOnlyList<LifecycleReadinessError> BlockingErrors
        => Errors.Where(e => e.Severity == ReadinessErrorSeverity.Error).ToList();

    /// <summary>
    /// Returns only the advisory errors (severity = Warning or Info).
    /// </summary>
    public IReadOnlyList<LifecycleReadinessError> AdvisoryErrors
        => Errors.Where(e => e.Severity != ReadinessErrorSeverity.Error).ToList();

    /// <summary>
    /// Creates a successful result with no errors for the given profile.
    /// </summary>
    public static LifecycleReadinessResult Success(ValidationProfile profile)
        => new(true, [], profile);

    /// <summary>
    /// Creates a failed result with the specified errors for the given profile.
    /// </summary>
    public static LifecycleReadinessResult Failure(ValidationProfile profile, IReadOnlyList<LifecycleReadinessError> errors)
        => new(errors.All(e => e.Severity != ReadinessErrorSeverity.Error), errors, profile);
}
