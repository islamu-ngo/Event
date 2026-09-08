namespace Explore.Application.Contracts.Scheduling;

/// <summary>
/// Deployment policy for the scheduler administration surface. The HAL link policy and the command handlers both
/// consult it so an advertised affordance and an accepted action can never disagree: if a control is hidden
/// because the host is read-only, invoking it directly is refused for the same reason.
/// </summary>
public interface ISchedulerAdminPolicy
{
    /// <summary>True when the host exposes the scheduler administration API at all.</summary>
    bool IsEnabled { get; }

    /// <summary>True when the host accepts reads but refuses every mutating scheduler action.</summary>
    bool IsReadOnly { get; }
}
