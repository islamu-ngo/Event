namespace Explore.Application.Services.Lifecycle;

/// <summary>
/// Identifies the source layer that produced a readiness error.
/// This lets callers distinguish non-negotiable failures from configurable policy requirements.
/// </summary>
public enum ReadinessErrorSource
{
    /// <summary>
    /// Non-negotiable structural invariant (e.g. tenant required, status required).
    /// These can never be relaxed by policy.
    /// </summary>
    HardInvariant,

    /// <summary>
    /// Domain business rule (e.g. cancelled events cannot be published).
    /// These follow from aggregate state semantics, not configuration.
    /// </summary>
    DomainRule,

    /// <summary>
    /// Instance-level configurable policy (hosted vs self-hosted strictness).
    /// </summary>
    InstancePolicy,

    /// <summary>
    /// Tenant-level configurable policy override.
    /// </summary>
    TenantPolicy,

    /// <summary>
    /// Command-profile-specific rule (e.g. import requires provenance).
    /// </summary>
    CommandProfile
}
