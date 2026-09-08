using Microsoft.AspNetCore.Components;

namespace Explore.Blazor.Client.Services.Docking;

internal readonly record struct ActivationGroupKey(DockScope Scope, DockSide? Side);

public sealed class DockLayoutState : IDockPanelRegistry
{
    private const int DefaultMinimumContentWidth = 375;

    private readonly Dictionary<DockPanelId, DockPanelEntry> _entries = [];
    private readonly Dictionary<DockScope, int> _minimumContentWidths = [];
    private readonly HashSet<DockPanelId> _userActivatedInOverlay = [];
    private int _viewportWidth;
    private bool _isMobileViewport;

    public event Action? Changed;

    public DockLayoutChangeReason LastChangeReason { get; private set; } = DockLayoutChangeReason.None;

    public bool IsMobileViewport => _isMobileViewport;

    public void Register(DockPanelDescriptor descriptor, RenderFragment content)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(content);

        descriptor = descriptor.Validate();

        if (_entries.ContainsKey(descriptor.Id))
        {
            throw new InvalidOperationException($"Dock panel '{descriptor.Id}' is already registered.");
        }

        var state = new DockPanelState(
            descriptor.Id,
            IsOpen: false,
            descriptor.DefaultMode,
            descriptor.DefaultWidth,
            descriptor.Order,
            IsActive: false);

        _entries.Add(descriptor.Id, new DockPanelEntry(descriptor, content, state));
        NotifyChanged(DockLayoutChangeReason.Registration);
    }

    public void Unregister(DockPanelId id)
    {
        _userActivatedInOverlay.Remove(id);
        if (_entries.Remove(id))
        {
            NotifyChanged(DockLayoutChangeReason.Registration);
        }
    }

    public IReadOnlyList<DockPanelEntry> GetPanels(DockScope scope, DockSide side)
    {
        return _entries.Values
            .Where(entry => entry.Descriptor.Scope == scope && entry.Descriptor.Side == side)
            .OrderBy(entry => entry.State.Order)
            .ThenBy(entry => entry.Descriptor.Title, StringComparer.Ordinal)
            .ToArray();
    }

    public DockPanelEntry? GetPanel(DockPanelId id)
    {
        return _entries.GetValueOrDefault(id);
    }

    public bool ShouldRenderDockedPanelAsOverlay(DockPanelEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.State is not { IsOpen: true, Mode: DockMode.Docked })
        {
            return false;
        }

        if (_isMobileViewport)
        {
            return true;
        }

        if (_viewportWidth <= 0 || entry.Descriptor.Side != DockSide.Start)
        {
            return false;
        }

        var dockedEndWidth = _entries.Values
            .Where(candidate => candidate.State is { IsOpen: true, Mode: DockMode.Docked }
                && candidate.Descriptor.Scope == entry.Descriptor.Scope
                && candidate.Descriptor.Side == DockSide.End)
            .Sum(candidate => candidate.State.Width);
        var availableContentWidth = _viewportWidth - dockedEndWidth - entry.State.Width;

        var minimumContentWidth = _minimumContentWidths.GetValueOrDefault(
            entry.Descriptor.Scope,
            DefaultMinimumContentWidth);

        return availableContentWidth < minimumContentWidth;
    }

    public void Refresh()
    {
        NotifyChanged(DockLayoutChangeReason.Refresh);
    }

    public void Open(DockPanelId id, DockLayoutChangeReason reason = DockLayoutChangeReason.UserAction)
    {
        var activeEntry = RequireEntry(id);
        var changed = false;

        if (reason is DockLayoutChangeReason.UserAction)
        {
            foreach (var entry in _entries.Values)
            {
                if (IsActivationGroupEntry(entry, activeEntry))
                {
                    _userActivatedInOverlay.Remove(entry.Descriptor.Id);
                }
            }

            if (ShouldRenderDockedPanelAsOverlay(activeEntry with
            {
                State = activeEntry.State with { IsOpen = true }
            }))
            {
                _userActivatedInOverlay.Add(id);
            }
        }

        foreach (var entry in _entries.Values.ToArray())
        {
            if (!IsActivationGroupEntry(entry, activeEntry))
            {
                continue;
            }

            var shouldItemBeOpen = entry.Descriptor.Id == id || entry.State.IsOpen;
            var shouldItemBeActive = entry.Descriptor.Id == id;
            var nextState = entry.State with
            {
                IsOpen = shouldItemBeOpen,
                IsActive = shouldItemBeActive
            };

            if (entry.State != nextState)
            {
                SetEntryState(entry.Descriptor.Id, nextState);
                changed = true;
            }
        }

        SanitizeOverlayActiveStates();

        if (changed)
        {
            NotifyChanged(reason);
        }
    }

    public void UpdateViewport(int viewportWidth, bool isMobile)
    {
        UpdateViewportCore(viewportWidth, isMobile, projectionChanged: false);
    }

    public void UpdateViewport(
        int viewportWidth,
        bool isMobile,
        DockScope scope,
        int minimumContentWidth)
    {
        var normalizedMinimum = Math.Max(0, minimumContentWidth);
        var projectionChanged = !_minimumContentWidths.TryGetValue(scope, out var currentMinimum)
            || currentMinimum != normalizedMinimum;
        _minimumContentWidths[scope] = normalizedMinimum;
        UpdateViewportCore(viewportWidth, isMobile, projectionChanged);
    }

    private void UpdateViewportCore(int viewportWidth, bool isMobile, bool projectionChanged)
    {
        var normalizedWidth = Math.Max(0, viewportWidth);
        var viewportChanged = projectionChanged
            || _viewportWidth != normalizedWidth
            || _isMobileViewport != isMobile;

        _viewportWidth = normalizedWidth;
        _isMobileViewport = isMobile;

        SanitizeOverlayActiveStates();

        if (viewportChanged)
        {
            NotifyChanged(DockLayoutChangeReason.ViewportPolicy);
        }
    }

    private void SanitizeOverlayActiveStates()
    {
        foreach (var entry in _entries.Values.ToArray())
        {
            if (entry.State.Mode != DockMode.Docked)
            {
                continue;
            }

            var isOverlay = ShouldRenderDockedPanelAsOverlay(entry);
            if (!isOverlay)
            {
                _userActivatedInOverlay.Remove(entry.Descriptor.Id);
            }

            var expectedActive = isOverlay
                ? entry.State.IsOpen && _userActivatedInOverlay.Contains(entry.Descriptor.Id)
                : entry.State.IsActive;

            if (entry.State.IsActive != expectedActive)
            {
                SetEntryState(entry.Descriptor.Id, entry.State with { IsActive = expectedActive });
            }
        }
    }

    public void Close(DockPanelId id, DockLayoutChangeReason reason = DockLayoutChangeReason.UserAction)
    {
        var entry = RequireEntry(id);

        if (!entry.Descriptor.CanClose)
        {
            throw new InvalidOperationException($"Dock panel '{id}' cannot be closed.");
        }

        _userActivatedInOverlay.Remove(id);
        UpdateState(id, state => state with { IsOpen = false, IsActive = false }, reason);
        SanitizeOverlayActiveStates();
    }

    public void Toggle(DockPanelId id)
    {
        var entry = RequireEntry(id);

        if (ShouldRenderDockedPanelAsOverlay(entry))
        {
            if (entry.State.IsOpen && entry.State.IsActive)
            {
                Close(id);
            }
            else
            {
                Open(id, DockLayoutChangeReason.UserAction);
            }
            return;
        }

        if (entry.State.IsOpen)
        {
            Close(id);
            return;
        }

        Open(id);
    }

    public void SetMode(
        DockPanelId id,
        DockMode mode,
        DockLayoutChangeReason reason = DockLayoutChangeReason.UserAction)
    {
        UpdateState(id, state => state with { Mode = mode }, reason);
    }

    public void Resize(DockPanelId id, int width)
    {
        var entry = RequireEntry(id);

        if (!entry.Descriptor.IsResizable)
        {
            throw new InvalidOperationException($"Dock panel '{id}' cannot be resized.");
        }

        var clampedWidth = Math.Clamp(width, entry.Descriptor.MinWidth, entry.Descriptor.MaxWidth);

        UpdateState(id, state => state with { Width = clampedWidth });
    }

    public void Activate(DockPanelId id)
    {
        var activeEntry = RequireEntry(id);
        var changed = false;

        foreach (var entry in _entries.Values.ToArray())
        {
            if (!IsActivationGroupEntry(entry, activeEntry))
            {
                continue;
            }

            var shouldBeActive = entry.Descriptor.Id == id;
            var nextState = entry.State with { IsActive = shouldBeActive };

            if (entry.State == nextState)
            {
                continue;
            }

            SetEntryState(entry.Descriptor.Id, nextState);
            changed = true;
        }

        if (changed)
        {
            NotifyChanged(DockLayoutChangeReason.UserAction);
        }
    }

    public DockLayoutSnapshot CreateSnapshot(string layoutKey, DockScope scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layoutKey);

        var panels = _entries.Values
            .Where(entry => entry.Descriptor.Scope == scope && entry.Descriptor.PersistState)
            .OrderBy(entry => entry.Descriptor.Scope)
            .ThenBy(entry => entry.Descriptor.Side)
            .ThenBy(entry => entry.State.Order)
            .Select(entry => entry.State)
            .ToArray();

        return new DockLayoutSnapshot(layoutKey, panels, DateTimeOffset.UtcNow);
    }

    public void ResetToDefaults()
    {
        var changed = false;

        foreach (var entry in _entries.Values.ToArray())
        {
            var defaultState = CreateDefaultState(entry.Descriptor);

            if (entry.State == defaultState)
            {
                continue;
            }

            SetEntryState(entry.Descriptor.Id, defaultState);
            changed = true;
        }

        if (changed)
        {
            NotifyChanged(DockLayoutChangeReason.Reset);
        }
    }

    public void RestoreSnapshot(DockLayoutSnapshot snapshot, string layoutKey, DockScope scope)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(layoutKey);

        if (!string.Equals(snapshot.LayoutKey, layoutKey, StringComparison.Ordinal))
        {
            return;
        }

        var restoredAny = false;
        var nextStates = _entries.ToDictionary(entry => entry.Key, entry => entry.Value.State);

        foreach (var panelState in snapshot.Panels)
        {
            if (!_entries.TryGetValue(panelState.Id, out var entry)
                || entry.Descriptor.Scope != scope
                || !entry.Descriptor.PersistState)
            {
                continue;
            }

            var currentState = nextStates[panelState.Id];
            var width = entry.Descriptor.IsResizable
                ? Math.Clamp(panelState.Width, entry.Descriptor.MinWidth, entry.Descriptor.MaxWidth)
                : currentState.Width;
            var isOpen = panelState.IsOpen || (!entry.Descriptor.CanClose && currentState.IsOpen);

            nextStates[panelState.Id] = panelState with
            {
                IsOpen = isOpen,
                Width = width,
                IsActive = isOpen && panelState.IsActive
            };
            restoredAny = true;
        }

        if (!restoredAny)
        {
            return;
        }

        NormalizeActiveStates(nextStates);

        var changed = false;

        foreach (var entry in _entries.Values.ToArray())
        {
            var nextState = nextStates[entry.Descriptor.Id];

            if (entry.State == nextState)
            {
                continue;
            }

            SetEntryState(entry.Descriptor.Id, nextState);
            changed = true;
        }

        if (changed)
        {
            NotifyChanged(DockLayoutChangeReason.SnapshotRestore);
        }
    }

    private void NormalizeActiveStates(Dictionary<DockPanelId, DockPanelState> nextStates)
    {
        foreach (var id in nextStates.Keys.ToArray())
        {
            var state = nextStates[id];

            if (state is { IsOpen: false, IsActive: true })
            {
                nextStates[id] = state with { IsActive = false };
            }
        }

        foreach (var group in _entries.Values.GroupBy(GetActivationGroupKey))
        {
            var openEntries = group
                .Where(entry => nextStates[entry.Descriptor.Id].IsOpen)
                .OrderBy(entry => nextStates[entry.Descriptor.Id].Order)
                .ThenBy(entry => entry.Descriptor.Title, StringComparer.Ordinal)
                .ToArray();

            var activeEntries = openEntries
                .Where(entry => nextStates[entry.Descriptor.Id].IsActive)
                .ToArray();

            if (activeEntries.Length == 0 && openEntries.Length > 0)
            {
                var firstOpenEntry = openEntries[0];
                var state = nextStates[firstOpenEntry.Descriptor.Id];
                nextStates[firstOpenEntry.Descriptor.Id] = state with { IsActive = true };
                continue;
            }

            foreach (var entry in activeEntries.Skip(1))
            {
                var state = nextStates[entry.Descriptor.Id];
                nextStates[entry.Descriptor.Id] = state with { IsActive = false };
            }
        }
    }

    private bool IsActivationGroupEntry(DockPanelEntry entry, DockPanelEntry activeEntry)
    {
        return GetActivationGroupKey(entry) == GetActivationGroupKey(activeEntry);
    }

    private ActivationGroupKey GetActivationGroupKey(DockPanelEntry entry)
    {
        return _isMobileViewport
            ? new ActivationGroupKey(entry.Descriptor.Scope, Side: null)
            : new ActivationGroupKey(entry.Descriptor.Scope, entry.Descriptor.Side);
    }

    private DockPanelEntry RequireEntry(DockPanelId id)
    {
        return _entries.TryGetValue(id, out var entry)
            ? entry
            : throw new KeyNotFoundException($"Dock panel '{id}' is not registered.");
    }

    private void UpdateState(
        DockPanelId id,
        Func<DockPanelState, DockPanelState> update,
        DockLayoutChangeReason reason = DockLayoutChangeReason.UserAction)
    {
        var entry = RequireEntry(id);
        var nextState = update(entry.State);

        if (entry.State == nextState)
        {
            return;
        }

        SetEntryState(id, nextState);
        NotifyChanged(reason);
    }

    private void SetEntryState(DockPanelId id, DockPanelState state)
    {
        var entry = RequireEntry(id);
        _entries[id] = entry with { State = state };
    }

    private static DockPanelState CreateDefaultState(DockPanelDescriptor descriptor)
    {
        return new DockPanelState(
            descriptor.Id,
            IsOpen: false,
            descriptor.DefaultMode,
            descriptor.DefaultWidth,
            descriptor.Order,
            IsActive: false);
    }

    private void NotifyChanged(DockLayoutChangeReason reason)
    {
        LastChangeReason = reason;
        Changed?.Invoke();
    }
}
