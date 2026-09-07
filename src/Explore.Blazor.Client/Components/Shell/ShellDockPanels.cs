using Explore.Blazor.Client.Services.Docking;

namespace Explore.Blazor.Client.Components.Shell;

public static class ShellDockPanels
{
    public static readonly DockPanelId WorkspaceNavId = new("shell.workspace-nav");
    public static readonly DockPanelId AiAssistantId = new("shell.ai-assistant");

    public static DockPanelDescriptor WorkspaceNav { get; } = new DockPanelDescriptor(
        WorkspaceNavId,
        DockScope.Shell,
        DockSide.Start,
        DockMode.Docked,
        Title: "Navigation",
        AriaLabel: "Sidebar navigation",
        DefaultWidth: 280,
        MinWidth: 240,
        MaxWidth: 360,
        Order: 10,
        IsResizable: true,
        CanClose: true,
        PersistState: true,
        StackStrategy: DockPanelStackStrategy.Tabbed,
        ResponsivePriority: 10).Validate();

    public static DockPanelDescriptor AiAssistant { get; } = new DockPanelDescriptor(
        AiAssistantId,
        DockScope.Shell,
        DockSide.End,
        DockMode.Docked,
        Title: "AI Assistant",
        AriaLabel: "AI Assistant",
        DefaultWidth: 360,
        MinWidth: 320,
        MaxWidth: 520,
        Order: 20,
        IsResizable: true,
        CanClose: true,
        PersistState: true,
        StackStrategy: DockPanelStackStrategy.Split,
        ResponsivePriority: 20).Validate();
}
