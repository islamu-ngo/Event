namespace Explore.Blazor.Client.Services.Docking;

public sealed record DockPanelState(
    DockPanelId Id,
    bool IsOpen,
    DockMode Mode,
    int Width,
    int Order,
    bool IsActive);
