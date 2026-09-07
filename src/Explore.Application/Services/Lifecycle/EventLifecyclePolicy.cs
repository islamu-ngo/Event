using System;
using System.Collections.Generic;

namespace Explore.Application.Services.Lifecycle;

/// <summary>
/// Describes the effective required-field policy for a given lifecycle validation profile.
/// Composed centrally by <see cref="IEventLifecyclePolicyProvider"/> from hard invariants
/// and tenant/instance overrides.
/// </summary>
public sealed record EventLifecyclePolicy
{
    /// <summary>
    /// The validation profile this policy applies to.
    /// </summary>
    public required ValidationProfile Profile { get; init; }

    /// <summary>
    /// Event-level field keys that must be present for this profile.
    /// Uses <see cref="EventFieldKey"/> values.
    /// </summary>
    public required IReadOnlySet<Enum> RequiredEventFields { get; init; }

    /// <summary>
    /// Session-level field keys that must be present for this profile.
    /// Uses <see cref="EventSessionFieldKey"/> values.
    /// </summary>
    public required IReadOnlySet<Enum> RequiredSessionFields { get; init; }

    /// <summary>
    /// Whether direct publication requires a separate approval decision.
    /// This is policy metadata; lifecycle readiness does not interpret it as a field requirement.
    /// </summary>
    public bool RequiresApproval { get; init; }

    /// <summary>
    /// Human-readable description of the policy source (e.g., "default", "tenant-override").
    /// </summary>
    public string Source { get; init; } = "default";
}
