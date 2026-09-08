using Microsoft.AspNetCore.Components;

namespace Explore.Blazor.Client.Services.Docking;

public sealed record DockPanelEntry(
    DockPanelDescriptor Descriptor,
    RenderFragment Content,
    DockPanelState State);
