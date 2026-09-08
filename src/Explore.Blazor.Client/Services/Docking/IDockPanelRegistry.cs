using Microsoft.AspNetCore.Components;

namespace Explore.Blazor.Client.Services.Docking;

public interface IDockPanelRegistry
{
    void Register(DockPanelDescriptor descriptor, RenderFragment content);

    void Unregister(DockPanelId id);

    IReadOnlyList<DockPanelEntry> GetPanels(DockScope scope, DockSide side);
}
