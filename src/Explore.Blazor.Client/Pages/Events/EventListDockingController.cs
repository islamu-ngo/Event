using Explore.Blazor.Client.Services.Docking;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace Explore.Blazor.Client.Pages.Events;

internal sealed class EventListDockingController : IAsyncDisposable
{
    private const string WorkspaceDockLayoutKey = "events";
    private static readonly TimeSpan DockLayoutAutosaveDelay = TimeSpan.FromMilliseconds(500);

    private readonly DockLayoutState _dockLayoutState;
    private readonly IDockLayoutPersistence _dockLayoutPersistence;
    private readonly ILogger _logger;

    private bool _workspaceDockLayoutHydrated;
    private bool _suppressWorkspaceDockLayoutAutosave;
    private DockLayoutSnapshot? _lastPersistedWorkspaceDockLayoutSnapshot;
    private CancellationTokenSource? _workspaceDockLayoutAutosaveCts;

    public EventListDockingController(
        DockLayoutState dockLayoutState,
        IDockLayoutPersistence dockLayoutPersistence,
        ILogger logger)
    {
        _dockLayoutState = dockLayoutState ?? throw new ArgumentNullException(nameof(dockLayoutState));
        _dockLayoutPersistence = dockLayoutPersistence ?? throw new ArgumentNullException(nameof(dockLayoutPersistence));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void RegisterPanels(RenderFragment customizeView, RenderFragment eventPreview)
    {
        ArgumentNullException.ThrowIfNull(customizeView);
        ArgumentNullException.ThrowIfNull(eventPreview);

        UnregisterPanelIfRegistered(EventDockPanels.CustomizeViewId);
        UnregisterPanelIfRegistered(EventDockPanels.EventPreviewId);

        _dockLayoutState.Register(EventDockPanels.CustomizeView, customizeView);
        _dockLayoutState.Register(EventDockPanels.EventPreview, eventPreview);
    }

    public async Task HydrateWorkspaceDockLayoutAsync()
    {
        _suppressWorkspaceDockLayoutAutosave = true;

        try
        {
            var snapshot = await _dockLayoutPersistence.LoadAsync(WorkspaceDockLayoutKey);
            if (snapshot is not null)
            {
                _dockLayoutState.RestoreSnapshot(snapshot, WorkspaceDockLayoutKey, DockScope.Workspace);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to hydrate event workspace dock layout.");
        }
        finally
        {
            _lastPersistedWorkspaceDockLayoutSnapshot = CreateWorkspaceDockLayoutSnapshot();
            _workspaceDockLayoutHydrated = true;
            _suppressWorkspaceDockLayoutAutosave = false;
        }
    }

    public EventListDockingChange SynchronizeAfterDockLayoutChanged(
        bool customizationDrawerOpen,
        bool detailDrawerOpen)
    {
        var nextCustomizationDrawerOpen = customizationDrawerOpen;
        var nextDetailDrawerOpen = detailDrawerOpen;
        var shouldClearDetailPreview = false;
        var shouldRender = false;

        var customizeOpen = IsPanelOpen(EventDockPanels.CustomizeViewId);
        var previewOpen = IsPanelOpen(EventDockPanels.EventPreviewId);

        if (!nextCustomizationDrawerOpen && customizeOpen)
        {
            nextCustomizationDrawerOpen = true;
            shouldRender = true;
        }

        if (nextCustomizationDrawerOpen && !customizeOpen)
        {
            nextCustomizationDrawerOpen = false;
            shouldRender = true;
        }

        if (nextDetailDrawerOpen && !previewOpen)
        {
            nextDetailDrawerOpen = false;
            shouldClearDetailPreview = true;
            shouldRender = true;
        }

        if (ShouldAutosaveWorkspaceDockLayout())
        {
            ScheduleWorkspaceDockLayoutAutosave();
        }

        return new EventListDockingChange(
            nextCustomizationDrawerOpen,
            nextDetailDrawerOpen,
            shouldClearDetailPreview,
            shouldRender);
    }

    public void OpenCustomizationDrawer()
    {
        _dockLayoutState.Open(EventDockPanels.CustomizeViewId);
    }

    public void CloseCustomizationDrawer()
    {
        ClosePanelIfRegistered(EventDockPanels.CustomizeViewId);
    }

    public void OpenEventPreview()
    {
        _dockLayoutState.Open(EventDockPanels.EventPreviewId);
    }

    public void OpenEventPreviewIfRegistered()
    {
        if (_dockLayoutState.GetPanel(EventDockPanels.EventPreviewId) is not null)
        {
            OpenEventPreview();
        }
    }

    public void CloseEventPreviewIfRegistered()
    {
        ClosePanelIfRegistered(EventDockPanels.EventPreviewId);
    }

    public async ValueTask DisposeAsync()
    {
        _workspaceDockLayoutAutosaveCts?.Cancel();
        _workspaceDockLayoutAutosaveCts?.Dispose();
        _workspaceDockLayoutAutosaveCts = null;

        UnregisterPanelIfRegistered(EventDockPanels.CustomizeViewId);
        UnregisterPanelIfRegistered(EventDockPanels.EventPreviewId);

        await ValueTask.CompletedTask;
    }

    private bool IsPanelOpen(DockPanelId panelId)
    {
        return _dockLayoutState.GetPanel(panelId)?.State.IsOpen == true;
    }

    private bool ShouldAutosaveWorkspaceDockLayout()
    {
        return _dockLayoutState.LastChangeReason is DockLayoutChangeReason.UserAction or DockLayoutChangeReason.Reset;
    }

    private void ScheduleWorkspaceDockLayoutAutosave()
    {
        if (!_workspaceDockLayoutHydrated || _suppressWorkspaceDockLayoutAutosave || !HasWorkspaceDockLayoutChanged())
        {
            return;
        }

        _workspaceDockLayoutAutosaveCts?.Cancel();
        _workspaceDockLayoutAutosaveCts?.Dispose();

        var autosaveCts = new CancellationTokenSource();
        _workspaceDockLayoutAutosaveCts = autosaveCts;
        _ = PersistWorkspaceDockLayoutAfterDelayAsync(autosaveCts);
    }

    private async Task PersistWorkspaceDockLayoutAfterDelayAsync(CancellationTokenSource autosaveCts)
    {
        try
        {
            await Task.Delay(DockLayoutAutosaveDelay, autosaveCts.Token);
            var snapshot = CreateWorkspaceDockLayoutSnapshot();
            if (SnapshotPanelsEqual(_lastPersistedWorkspaceDockLayoutSnapshot, snapshot))
            {
                return;
            }

            await _dockLayoutPersistence.SaveAsync(snapshot, autosaveCts.Token);
            _lastPersistedWorkspaceDockLayoutSnapshot = snapshot;
        }
        catch (OperationCanceledException) when (autosaveCts.IsCancellationRequested)
        {
            // A newer dock layout change superseded this pending autosave.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save event workspace dock layout.");
        }
    }

    private DockLayoutSnapshot CreateWorkspaceDockLayoutSnapshot()
    {
        return _dockLayoutState.CreateSnapshot(WorkspaceDockLayoutKey, DockScope.Workspace);
    }

    private bool HasWorkspaceDockLayoutChanged()
    {
        return !SnapshotPanelsEqual(_lastPersistedWorkspaceDockLayoutSnapshot, CreateWorkspaceDockLayoutSnapshot());
    }

    private static bool SnapshotPanelsEqual(DockLayoutSnapshot? previous, DockLayoutSnapshot current)
    {
        return previous is not null && previous.Panels.SequenceEqual(current.Panels);
    }

    private void ClosePanelIfRegistered(DockPanelId panelId)
    {
        if (_dockLayoutState.GetPanel(panelId) is not null)
        {
            _dockLayoutState.Close(panelId);
        }
    }

    private void UnregisterPanelIfRegistered(DockPanelId panelId)
    {
        if (_dockLayoutState.GetPanel(panelId) is not null)
        {
            _dockLayoutState.Unregister(panelId);
        }
    }
}

internal sealed record EventListDockingChange(
    bool CustomizationDrawerOpen,
    bool DetailDrawerOpen,
    bool ShouldClearDetailPreview,
    bool ShouldRender);
