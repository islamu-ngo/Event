using Explore.Blazor.Client.Services.Docking;
using Explore.Blazor.Client.Services.Shell;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Explore.Blazor.Client.Components.Shell;

public partial class WorkspaceNavigationHost : IDisposable
{
    [Inject]
    private IWorkspaceRegistry Registry { get; set; } = null!;

    [Inject]
    private UiShellState ShellState { get; set; } = null!;

    [CascadingParameter]
    private DockPanelEntry? DockPanelEntry { get; set; }

    private Type? _activeProviderType;
    private bool _disposed;

    private string OverlayBrandAriaLabel => string.IsNullOrWhiteSpace(BrandDisplayName)
        ? "Home"
        : BrandDisplayName;

    private bool ShouldRenderOverlayHeader => OnCloseRequested.HasDelegate
        && DockPanelEntry?.State.Mode is DockMode.Overlay or DockMode.Temporary or DockMode.Inspector;

    protected override void OnInitialized()
    {
        ShellState.Changed += OnShellStateChanged;
        UpdateActiveProvider();
    }

    private void OnShellStateChanged()
    {
        UpdateActiveProvider();
        StateHasChanged();
    }

    private void UpdateActiveProvider()
    {
        var descriptor = Registry.Workspaces.FirstOrDefault(w => w.Key == ShellState.ActiveWorkspace);
        _activeProviderType = descriptor?.NavigationProviderType;
    }

    private RenderFragment RenderActiveProvider() => builder =>
    {
        if (_activeProviderType is null)
        {
            return;
        }

        builder.OpenComponent(0, _activeProviderType);
        builder.CloseComponent();
    };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        ShellState.Changed -= OnShellStateChanged;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
