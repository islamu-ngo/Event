namespace Explore.Blazor.Client.Services.Docking;

public enum DockLayoutChangeReason
{
    None = 0,
    Registration,
    UserAction,
    ViewportPolicy,
    SnapshotRestore,
    Reset,
    Refresh
}
