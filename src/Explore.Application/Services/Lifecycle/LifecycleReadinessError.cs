namespace Explore.Application.Services.Lifecycle;

/// <summary>
/// A single readiness diagnostic produced by the lifecycle readiness evaluator.
/// </summary>
/// <param name="Code">Stable machine-readable error code (e.g. <c>title_required</c>).</param>
/// <param name="FieldKey">Product-concept field key identifying the missing or invalid field.</param>
/// <param name="FieldPath">Dotted field path for UI/API binding (e.g. <c>title</c>, <c>schedule.sessions</c>).</param>
/// <param name="Message">Human-readable explanation suitable for ProblemDetails or UI display.</param>
/// <param name="Severity">Error severity; only <see cref="ReadinessErrorSeverity.Error"/> blocks readiness.</param>
/// <param name="Source">Origin layer of the rule (hard invariant, domain rule, policy, command profile).</param>
/// <param name="Profile">Validation profile that triggered this error, if applicable.</param>
public sealed record LifecycleReadinessError(
    string Code,
    Enum FieldKey,
    string FieldPath,
    string Message,
    ReadinessErrorSeverity Severity = ReadinessErrorSeverity.Error,
    ReadinessErrorSource Source = ReadinessErrorSource.HardInvariant,
    ValidationProfile? Profile = null);
