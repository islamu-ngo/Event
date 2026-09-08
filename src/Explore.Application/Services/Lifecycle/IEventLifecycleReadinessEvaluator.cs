using Explore.Domain;

namespace Explore.Application.Services.Lifecycle;

/// <summary>
/// Evaluates whether an <see cref="Event"/> satisfies the required fields and hard invariants
/// for a given <see cref="ValidationProfile"/> under an effective <see cref="EventLifecyclePolicy"/>.
/// </summary>
public interface IEventLifecycleReadinessEvaluator
{
    /// <summary>
    /// Evaluates readiness of the supplied event against the profile and policy.
    /// </summary>
    /// <param name="event">The event aggregate to evaluate.</param>
    /// <param name="profile">The validation profile describing the command context (draft, publish, import, etc.).</param>
    /// <param name="policy">The effective lifecycle policy containing required field sets.</param>
    /// <returns>A <see cref="LifecycleReadinessResult"/> with machine-readable errors if not ready.</returns>
    LifecycleReadinessResult Evaluate(Event @event, ValidationProfile profile, EventLifecyclePolicy policy);

    LifecycleReadinessResult Evaluate(EventSession session, Event? parentEvent, ValidationProfile profile, EventLifecyclePolicy policy);
}
