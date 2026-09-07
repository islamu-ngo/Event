namespace Explore.Blazor.Client.Contracts.Services.Shell;

/// <summary>
/// Marker contract for Blazor components that provide contextual navigation
/// content for a workspace. The <see cref="Components.Shell.WorkspaceNavigationHost"/>
/// renders the component type stored in
/// <see cref="Services.Shell.WorkspaceDescriptor.NavigationProviderType"/>.
/// </summary>
public interface IWorkspaceNavigationProvider
{
    /// <summary>
    /// Accessible name for the navigation landmark rendered by this provider.
    /// </summary>
    string AriaLabel { get; }
}
