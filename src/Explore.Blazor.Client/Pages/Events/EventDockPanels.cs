using Explore.Blazor.Client.Services.Docking;

namespace Explore.Blazor.Client.Pages.Events;

public static class EventDockPanels
{
    public static readonly DockPanelId CustomizeViewId = new("events.customize-view");
    public static readonly DockPanelId EventPreviewId = new("events.event-preview");

    public static DockPanelDescriptor CustomizeView { get; } = new DockPanelDescriptor(
        CustomizeViewId,
        DockScope.Workspace,
        DockSide.End,
        DockMode.Docked,
        Title: "Customize View",
        AriaLabel: "Customize event list",
        DefaultWidth: 320,
        MinWidth: 280,
        MaxWidth: 480,
        Order: 10,
        IsResizable: true,
        CanClose: true,
        PersistState: true,
        StackStrategy: DockPanelStackStrategy.Split,
        ResponsivePriority: 20).Validate();

    public static DockPanelDescriptor EventPreview { get; } = new DockPanelDescriptor(
        EventPreviewId,
        DockScope.Workspace,
        DockSide.End,
        DockMode.Inspector,
        Title: "Event Preview",
        AriaLabel: "Event preview",
        DefaultWidth: 440,
        MinWidth: 360,
        MaxWidth: 560,
        Order: 20,
        IsResizable: false,
        CanClose: true,
        PersistState: false,
        StackStrategy: DockPanelStackStrategy.Split,
        ResponsivePriority: 30).Validate();
}
