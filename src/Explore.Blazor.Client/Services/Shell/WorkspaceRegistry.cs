namespace Explore.Blazor.Client.Services.Shell;

using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Components.Shell;
using Explore.Blazor.Client.Components.Shell.Workspaces;
using MudBlazor;

public sealed class WorkspaceRegistry : IWorkspaceRegistry
{
    private static readonly IReadOnlyList<WorkspaceDescriptor> RegisteredWorkspaces =
    [
        new(
            WorkspaceKey.Events,
            "workspace.events",
            Icons.Material.Filled.Explore,
            "/",
            RequiresAuthentication: false,
            AvailabilityPolicy: null,
            NavigationProviderType: typeof(EventsWorkspaceNavigation)),
        new(
            WorkspaceKey.Studio,
            "workspace.studio",
            Icons.Material.Filled.Edit,
            "/studio",
            RequiresAuthentication: true,
            AvailabilityPolicy: IsStudioAvailable,
            NavigationProviderType: typeof(StudioWorkspaceNavigation)),
        new(
            WorkspaceKey.Ai,
            "workspace.ai",
            Icons.Material.Filled.AutoAwesome,
            "/ai",
            RequiresAuthentication: true,
            AvailabilityPolicy: IsAiAvailable,
            NavigationProviderType: typeof(AiWorkspaceNavigation)),
        new(
            WorkspaceKey.Settings,
            "workspace.settings",
            Icons.Material.Filled.Settings,
            "/settings",
            RequiresAuthentication: true,
            AvailabilityPolicy: null,
            NavigationProviderType: null)
    ];

    public IReadOnlyList<WorkspaceDescriptor> Workspaces => RegisteredWorkspaces;

    private static bool IsStudioAvailable(WorkspaceAvailabilityDto? availability) =>
        availability?.Studio == true;

    private static bool IsAiAvailable(WorkspaceAvailabilityDto? availability) =>
        availability?.Ai == true;
}
