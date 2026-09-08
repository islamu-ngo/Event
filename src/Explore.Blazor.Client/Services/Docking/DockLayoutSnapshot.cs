namespace Explore.Blazor.Client.Services.Docking;

public sealed record DockLayoutSnapshot(
    string LayoutKey,
    IReadOnlyList<DockPanelState> Panels,
    DateTimeOffset UpdatedAt);
