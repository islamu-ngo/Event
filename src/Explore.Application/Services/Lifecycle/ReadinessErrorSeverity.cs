namespace Explore.Application.Services.Lifecycle;

/// <summary>
/// Severity of a readiness error. Only <see cref="Error"/> blocks the readiness result.
/// </summary>
public enum ReadinessErrorSeverity
{
    /// <summary>Blocks readiness — the command cannot proceed.</summary>
    Error,

    /// <summary>Advisory — the command may proceed but the field should be addressed.</summary>
    Warning,

    /// <summary>Informational note — no action required.</summary>
    Info
}
