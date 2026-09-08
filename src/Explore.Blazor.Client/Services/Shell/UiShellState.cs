namespace Explore.Blazor.Client.Services.Shell;

using Explore.Blazor.Client.Clients;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;

public sealed class UiShellState : IDisposable
{
    private readonly NavigationManager _navigationManager;
    private readonly WorkspaceRouteClassifier _classifier;
    private readonly Dictionary<WorkspaceKey, string> _lastRoutes = [];
    private bool _disposed;

    public UiShellState(
        NavigationManager navigationManager,
        WorkspaceRouteClassifier classifier)
    {
        _navigationManager = navigationManager;
        _classifier = classifier;
        ActiveWorkspace = WorkspaceKey.Events;
        UpdateFromLocation(navigationManager.Uri, notify: false);
        navigationManager.LocationChanged += OnLocationChanged;
    }

    public event Action? Changed;

    public WorkspaceKey ActiveWorkspace { get; private set; }

    public bool IsPersonalSettingsOpen { get; private set; }

    public string? PersonalSettingsReturnRoute { get; private set; }

    public ManagedActorDto? ActiveActor { get; private set; }

    public Guid? ActiveActorId => ActiveActor?.ActorId;

    public string? GetLastRoute(WorkspaceKey workspace) =>
        _lastRoutes.GetValueOrDefault(workspace);

    public void NavigateToPersonalSettings(
        string destination = "/settings/personal",
        MouseEventArgs? mouseEvent = null)
    {
        var route = NormalizePersonalSettingsRoute(destination);
        if (mouseEvent is { Button: not 0 }
            || mouseEvent?.CtrlKey == true
            || mouseEvent?.MetaKey == true
            || mouseEvent?.ShiftKey == true
            || mouseEvent?.AltKey == true)
        {
            return;
        }

        if (IsContextualPersonalSettingsOrigin(ActiveWorkspace))
        {
            var originRoute = _lastRoutes.GetValueOrDefault(ActiveWorkspace);
            if (!string.IsNullOrWhiteSpace(originRoute))
            {
                IsPersonalSettingsOpen = true;
                PersonalSettingsReturnRoute ??= originRoute;
            }
        }

        _navigationManager.NavigateTo(route, forceLoad: false);
    }

    public void RestoreLastRoute(WorkspaceKey workspace, string route)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(route);

        if (_classifier.Classify(route) == workspace && !_lastRoutes.ContainsKey(workspace))
        {
            _lastRoutes[workspace] = route;
        }
    }

    public void ReconcileAvailability(Func<WorkspaceKey, bool> isAvailable)
    {
        ArgumentNullException.ThrowIfNull(isAvailable);

        var revoked = _lastRoutes.Keys.Where(key => !isAvailable(key)).ToList();
        foreach (var key in revoked)
        {
            _lastRoutes.Remove(key);
        }

        if (IsPersonalSettingsOpen && !isAvailable(WorkspaceKey.Settings))
        {
            var returnRoute = PersonalSettingsReturnRoute ?? _lastRoutes.GetValueOrDefault(WorkspaceKey.Events) ?? "/";
            ClearPersonalSettings();
            _navigationManager.NavigateTo(returnRoute, forceLoad: false);
            Changed?.Invoke();
            return;
        }

        if (!isAvailable(ActiveWorkspace))
        {
            ClearPersonalSettings();
            ActiveWorkspace = WorkspaceKey.Events;
            var fallbackRoute = _lastRoutes.GetValueOrDefault(WorkspaceKey.Events) ?? "/";
            _navigationManager.NavigateTo(fallbackRoute, forceLoad: false);
            Changed?.Invoke();
        }
    }

    public void ReconcileActiveActors(IEnumerable<ManagedActorDto>? authorizedActors, Guid? pinnedActorId)
    {
        var actors = NormalizeActors(authorizedActors);
        var previousActorId = ActiveActorId;
        ActiveActor = actors.FirstOrDefault(actor => actor.ActorId == pinnedActorId)
            ?? actors.FirstOrDefault(actor => actor.ActorId == previousActorId)
            ?? actors.FirstOrDefault();

        if (ActiveActorId != previousActorId)
        {
            Changed?.Invoke();
        }
    }

    public bool TrySetActiveActor(Guid actorId, IEnumerable<ManagedActorDto>? authorizedActors)
    {
        var actor = NormalizeActors(authorizedActors).FirstOrDefault(candidate => candidate.ActorId == actorId);
        if (actor is null)
        {
            return false;
        }

        var changed = ActiveActorId != actorId;
        ActiveActor = actor;
        if (changed)
        {
            Changed?.Invoke();
        }

        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _navigationManager.LocationChanged -= OnLocationChanged;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs args) =>
        UpdateFromLocation(args.Location, notify: true);

    private static IReadOnlyList<ManagedActorDto> NormalizeActors(IEnumerable<ManagedActorDto>? actors) =>
        actors?
            .Where(actor => actor.ActorId is { } actorId && actorId != Guid.Empty)
            .DistinctBy(actor => actor.ActorId)
            .ToList()
        ?? [];

    private void UpdateFromLocation(string location, bool notify)
    {
        var relative = _navigationManager.ToBaseRelativePath(location);
        var route = string.IsNullOrEmpty(relative) ? "/" : $"/{relative.TrimStart('/')}";

        if (IsPersonalSettingsRoute(route))
        {
            if (IsPersonalSettingsOpen)
            {
                if (notify)
                {
                    Changed?.Invoke();
                }

                return;
            }
        }
        else
        {
            ClearPersonalSettings();
        }

        ActiveWorkspace = _classifier.Classify(route);
        _lastRoutes[ActiveWorkspace] = route;

        if (notify)
        {
            Changed?.Invoke();
        }
    }

    private static bool IsContextualPersonalSettingsOrigin(WorkspaceKey workspace) =>
        workspace == WorkspaceKey.Events
        || workspace == WorkspaceKey.Studio
        || workspace == WorkspaceKey.Ai;

    private static bool IsPersonalSettingsRoute(string route)
    {
        var suffixIndex = route.IndexOfAny(['?', '#']);
        var path = suffixIndex >= 0 ? route[..suffixIndex] : route;
        var segments = path.Trim('/').Split('/', StringSplitOptions.None);
        if (segments.Length is not (2 or 3)
            || !segments[0].Equals("settings", StringComparison.OrdinalIgnoreCase)
            || !segments[1].Equals("personal", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (segments.Length == 2)
        {
            return true;
        }

        var section = Uri.UnescapeDataString(segments[2]);
        return section.Length > 0 && section.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');
    }

    private static string NormalizePersonalSettingsRoute(string destination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        var route = destination.Trim();
        if (route.StartsWith("//", StringComparison.Ordinal)
            || (!route.StartsWith('/') && Uri.TryCreate(route, UriKind.Absolute, out _)))
        {
            throw new ArgumentException("Personal Settings navigation requires an app-relative route.", nameof(destination));
        }

        var suffixIndex = route.IndexOfAny(['?', '#']);
        var path = (suffixIndex >= 0 ? route[..suffixIndex] : route).TrimEnd('/');
        var suffix = suffixIndex >= 0 ? route[suffixIndex..] : string.Empty;
        path = path.StartsWith('/') ? path : $"/{path}";
        if (!IsPersonalSettingsRoute(path) || HasReturnUrl(suffix))
        {
            throw new ArgumentException(
                "Only /settings/personal and /settings/personal/:section routes are supported.",
                nameof(destination));
        }

        return path.ToLowerInvariant() + suffix;
    }

    private static bool HasReturnUrl(string suffix)
    {
        var queryIndex = suffix.IndexOf('?');
        if (queryIndex < 0)
        {
            return false;
        }

        var fragmentIndex = suffix.IndexOf('#', queryIndex);
        var query = suffix[(queryIndex + 1)..(fragmentIndex >= 0 ? fragmentIndex : suffix.Length)];
        return query.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2)[0])
            .Any(name => Uri.UnescapeDataString(name).Equals("returnUrl", StringComparison.OrdinalIgnoreCase));
    }

    private void ClearPersonalSettings()
    {
        IsPersonalSettingsOpen = false;
        PersonalSettingsReturnRoute = null;
    }
}
