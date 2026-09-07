using System.Text.Json;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Contracts.Services.Shell;
using Microsoft.AspNetCore.Components.Authorization;

namespace Explore.Blazor.Client.Services.Shell;

public sealed class ShellPreferencesService(
    IUserSettingsService settingsService,
    AuthenticationStateProvider authenticationStateProvider,
    ILogger<ShellPreferencesService> logger) : IShellPreferencesService
{
    public const string PreferencesCategory = "UiShellPreferences";
    public const string LastWorkspaceKey = "ui_shell_preferences.last_workspace";
    public const string LastActorKey = "ui_shell_preferences.last_actor";
    public const string LastSettingsScopeKey = "ui.settings.last_scope.v1";

    private string? _savedWorkspace;
    private Guid? _savedActorId;
    private string? _savedSettingsScope;

    public async Task<ShellPreferenceState> LoadAsync(
        UiShellContextDto context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var fallbackWorkspace = ResolveOrganizerDefaultWorkspace(context);
        if (!await IsAuthenticatedAsync())
        {
            return new ShellPreferenceState(fallbackWorkspace, null, "/settings/personal");
        }

        var response = await settingsService.GetSettingsAsync(PreferencesCategory, cancellationToken);
        var values = response?.Settings?.ToDictionary(setting => setting.Key, setting => setting.Value)
            ?? [];
        var storedWorkspace = ReadString(values.GetValueOrDefault(LastWorkspaceKey));
        var storedActor = ReadGuid(values.GetValueOrDefault(LastActorKey));
        var storedScope = ReadString(values.GetValueOrDefault(LastSettingsScopeKey));

        var workspace = IsWorkspaceAvailable(storedWorkspace, context)
            ? storedWorkspace!
            : fallbackWorkspace;
        var actorId = context.ManagedActors?.Any(actor => actor.ActorId == storedActor) == true
            ? storedActor
            : null;
        var scopeHref = ResolveSettingsScopeHref(storedScope, context.SettingsScopes ?? []);

        await ResetInvalidPreferenceAsync(
            LastWorkspaceKey,
            storedWorkspace,
            storedWorkspace is not null && !IsWorkspaceAvailable(storedWorkspace, context),
            cancellationToken);
        await ResetInvalidPreferenceAsync(
            LastActorKey,
            storedActor?.ToString(),
            storedActor.HasValue && !actorId.HasValue,
            cancellationToken);
        await ResetInvalidPreferenceAsync(
            LastSettingsScopeKey,
            storedScope,
            storedScope is not null && scopeHref == "/settings/personal",
            cancellationToken);

        _savedWorkspace = IsWorkspaceAvailable(storedWorkspace, context) ? storedWorkspace : null;
        _savedActorId = actorId;
        _savedSettingsScope = scopeHref == "/settings/personal" ? null : storedScope;

        return new ShellPreferenceState(workspace, actorId, scopeHref);
    }

    public async Task SaveSelectionAsync(
        string workspace,
        Guid? actorId,
        string currentRoute,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspace);
        ArgumentNullException.ThrowIfNull(currentRoute);

        if (!await IsAuthenticatedAsync())
        {
            return;
        }

        var values = new Dictionary<string, string>();
        if (!string.Equals(_savedWorkspace, workspace, StringComparison.OrdinalIgnoreCase))
        {
            values[LastWorkspaceKey] = workspace;
        }

        if (actorId.HasValue && actorId != _savedActorId)
        {
            values[LastActorKey] = actorId.Value.ToString();
        }

        var settingsScope = NormalizeSettingsScope(currentRoute);
        if (settingsScope is not null
            && !string.Equals(_savedSettingsScope, settingsScope, StringComparison.OrdinalIgnoreCase))
        {
            values[LastSettingsScopeKey] = settingsScope;
        }

        if (values.Count > 0)
        {
            var result = await settingsService.UpdateSettingsBatchAsync(
                PreferencesCategory,
                values,
                cancellationToken);
            if (result?.Success == true)
            {
                _savedWorkspace = values.GetValueOrDefault(LastWorkspaceKey) ?? _savedWorkspace;
                _savedActorId = values.ContainsKey(LastActorKey) ? actorId : _savedActorId;
                _savedSettingsScope = values.GetValueOrDefault(LastSettingsScopeKey) ?? _savedSettingsScope;
            }
        }

        if (!actorId.HasValue && _savedActorId.HasValue
            && await settingsService.ResetSettingAsync(LastActorKey, cancellationToken))
        {
            _savedActorId = null;
        }
    }

    internal static string? NormalizeSettingsScope(string route)
    {
        var suffix = route.IndexOfAny(['?', '#']);
        var path = (suffix >= 0 ? route[..suffix] : route).TrimEnd('/');
        if (path.Equals("/settings/admin", StringComparison.OrdinalIgnoreCase))
        {
            return "tenant";
        }

        if (path.Equals("/settings/instance", StringComparison.OrdinalIgnoreCase))
        {
            return "instance";
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 3
            && segments[0].Equals("settings", StringComparison.OrdinalIgnoreCase)
            && (segments[1].Equals("organization", StringComparison.OrdinalIgnoreCase)
                || segments[1].Equals("group", StringComparison.OrdinalIgnoreCase))
            && Guid.TryParse(segments[2], out var scopeId))
        {
            return $"{segments[1].ToLowerInvariant()}:{scopeId}";
        }

        return null;
    }

    internal static string ResolveSettingsScopeHref(
        string? storedScope,
        IEnumerable<SettingsScopeDto> authorizedScopes)
    {
        if (string.Equals(storedScope, "tenant", StringComparison.OrdinalIgnoreCase)
            && authorizedScopes.Any(scope => IsScope(scope, "Tenant")))
        {
            return "/settings/admin";
        }

        if (string.Equals(storedScope, "instance", StringComparison.OrdinalIgnoreCase)
            && authorizedScopes.Any(scope => IsScope(scope, "Instance")))
        {
            return "/settings/instance";
        }

        var parts = storedScope?.Split(':', 2);
        if (parts is { Length: 2 }
            && Guid.TryParse(parts[1], out var scopeId)
            && authorizedScopes.Any(scope => scope.ScopeId == scopeId && IsScope(scope, parts[0])))
        {
            return $"/settings/{parts[0].ToLowerInvariant()}/{scopeId}";
        }

        return "/settings/personal";
    }

    private static string ResolveOrganizerDefaultWorkspace(UiShellContextDto context) =>
        string.Equals(
            context.NavigationDefaults?.OrganizerDefaultWorkspace,
            "Studio",
            StringComparison.OrdinalIgnoreCase)
        && context.Workspaces?.Studio == true
            ? WorkspaceKey.Studio.Value
            : WorkspaceKey.Events.Value;

    private static bool IsWorkspaceAvailable(string? workspace, UiShellContextDto context) =>
        workspace?.ToLowerInvariant() switch
        {
            "events" => true,
            "studio" => context.Workspaces?.Studio == true,
            "ai" => context.Workspaces?.Ai == true,
            "settings" => true,
            _ => false
        };

    private static bool IsScope(SettingsScopeDto scope, string kind) =>
        string.Equals(scope.Scope, kind, StringComparison.OrdinalIgnoreCase);

    private async Task ResetInvalidPreferenceAsync(
        string key,
        string? value,
        bool invalid,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(value) && invalid)
        {
            await settingsService.ResetSettingAsync(key, cancellationToken);
        }
    }

    private async Task<bool> IsAuthenticatedAsync()
    {
        try
        {
            AuthenticationState state =
                await authenticationStateProvider.GetAuthenticationStateAsync();
            return state.User.Identity?.IsAuthenticated == true;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not resolve authentication state for shell preferences.");
            return false;
        }
    }

    private static string? ReadString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "null")
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<string>(value);
        }
        catch (JsonException)
        {
            return value.Trim().Trim('"');
        }
    }

    private static Guid? ReadGuid(string? value) =>
        Guid.TryParse(ReadString(value), out var parsed) ? parsed : null;
}
