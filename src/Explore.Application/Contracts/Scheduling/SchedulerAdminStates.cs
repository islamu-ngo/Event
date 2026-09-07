namespace Explore.Application.Contracts.Scheduling;

/// <summary>
/// Wire tokens for scheduler lifecycle and trigger state. They are lowercase and stable because clients branch on
/// them for iconography and affordances; renaming one is a breaking contract change, not a cosmetic edit.
/// </summary>
public static class SchedulerAdminStates
{
    // Scheduler lifecycle.
    public const string Running = "running";
    public const string Standby = "standby";
    public const string Shutdown = "shutdown";
    public const string Disabled = "disabled";

    // Job and trigger state.
    public const string Active = "active";
    public const string Paused = "paused";
    public const string Complete = "complete";
    public const string Error = "error";
    public const string Blocked = "blocked";
    public const string None = "none";

    /// <summary>A durable job that carries no trigger of its own and is fired on demand by runtime code.</summary>
    public const string OnDemand = "on-demand";
}
