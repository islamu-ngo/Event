namespace Explore.Blazor.Client.Contracts.Scheduling;

/// <summary>
/// Link relations the scheduler administration surface emits. Affordances are gated on these being present in a
/// resource's <c>_links</c>, so they are the client's only authority on which controls to render.
/// </summary>
public static class SchedulerAdminLinkRelations
{
    public const string Self = "self";
    public const string Jobs = "jobs";
    public const string Pause = "pause";
    public const string Resume = "resume";
    public const string Trigger = "trigger";
    public const string ResetError = "reset-error";
    public const string Interrupt = "interrupt";
}

/// <summary>
/// Normalized scheduler, job, and trigger state tokens. These mirror the server's wire contract; the client
/// branches on them for status presentation only, never to decide whether an action is permitted.
/// </summary>
public static class SchedulerAdminStates
{
    public const string Running = "running";
    public const string Standby = "standby";
    public const string Shutdown = "shutdown";
    public const string Disabled = "disabled";

    public const string Active = "active";
    public const string Paused = "paused";
    public const string Complete = "complete";
    public const string Error = "error";
    public const string Blocked = "blocked";
    public const string None = "none";
    public const string OnDemand = "on-demand";
}
