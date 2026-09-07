using Explore.Blazor.Client.Components.Shell;
using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Contracts.Services.Shell;
using Explore.Blazor.Client.Services.Docking;
using Microsoft.AspNetCore.Components.Authorization;

namespace Explore.Blazor.Client.Services.Interop;

public sealed class ServerBackedDockLayoutPersistence(
    IDockLayoutPersistence localPersistence,
    IUserSettingsService settingsService,
    AuthenticationStateProvider authenticationStateProvider,
    IUiShellContextService shellContextService,
    ILogger<ServerBackedDockLayoutPersistence> logger) : IDockLayoutPersistence
{
    public const string PreferencesCategory = "UiShellPreferences";
    public const string LayoutPreferenceKey = "ui_shell_preferences.layout.v1";
    private const string ShellLayoutKey = "shell";

    public async Task<DockLayoutSnapshot?> LoadAsync(
        string layoutKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layoutKey);

        if (!IsShellLayout(layoutKey) || !await IsAuthenticatedAsync())
        {
            return await localPersistence.LoadAsync(layoutKey, cancellationToken);
        }

        var settings = await settingsService.GetSettingsAsync(PreferencesCategory, cancellationToken);
        var value = settings?.Settings?.FirstOrDefault(setting => setting.Key == LayoutPreferenceKey)?.Value;
        var serverSnapshot = LocalStorageDockLayoutPersistence.Deserialize(layoutKey, value, logger);
        if (serverSnapshot is not null)
        {
            return serverSnapshot;
        }

        var localSnapshot = await localPersistence.LoadAsync(layoutKey, cancellationToken);
        if (localSnapshot is null)
        {
            return null;
        }

        if (await SaveToServerAsync(localSnapshot, cancellationToken))
        {
            await localPersistence.DeleteAsync(layoutKey, cancellationToken);
        }

        return localSnapshot;
    }

    public async Task<bool> SaveAsync(
        DockLayoutSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!IsShellLayout(snapshot.LayoutKey) || !await IsAuthenticatedAsync())
        {
            return await localPersistence.SaveAsync(snapshot, cancellationToken);
        }

        var saved = await SaveToServerAsync(snapshot, cancellationToken);
        return saved || await localPersistence.SaveAsync(snapshot, cancellationToken);
    }

    public async Task<bool> DeleteAsync(
        string layoutKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layoutKey);

        if (!IsShellLayout(layoutKey) || !await IsAuthenticatedAsync())
        {
            return await localPersistence.DeleteAsync(layoutKey, cancellationToken);
        }

        var serverDeleted = await settingsService.ResetSettingAsync(LayoutPreferenceKey, cancellationToken);
        var localDeleted = await localPersistence.DeleteAsync(layoutKey, cancellationToken);
        return serverDeleted || localDeleted;
    }

    private async Task<bool> SaveToServerAsync(
        DockLayoutSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var context = await shellContextService.GetCachedContextAsync(cancellationToken);
        var effectiveSnapshot = context?.NavigationDefaults?.AllowUserOverride == false
            ? snapshot with
            {
                Panels = snapshot.Panels
                    .Where(panel => panel.Id != ShellDockPanels.WorkspaceNavId)
                    .ToList()
            }
            : snapshot;
        var response = await settingsService.UpdateSettingsBatchAsync(
            PreferencesCategory,
            new Dictionary<string, string>
            {
                [LayoutPreferenceKey] = LocalStorageDockLayoutPersistence.Serialize(effectiveSnapshot)
            },
            cancellationToken);

        return response?.Success == true
            && response.Results.Any(result => result.Key == LayoutPreferenceKey && result.Applied == true);
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
            logger.LogDebug(ex, "Could not resolve authentication state for shell layout persistence.");
            return false;
        }
    }

    private static bool IsShellLayout(string layoutKey) =>
        string.Equals(layoutKey, ShellLayoutKey, StringComparison.Ordinal);
}
