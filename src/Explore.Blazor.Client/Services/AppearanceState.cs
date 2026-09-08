using Explore.Blazor.Client.Clients;
using MudBlazor;

namespace Explore.Blazor.Client.Services;

public interface IAppearanceThemeService
{
    AppearanceState Current { get; }
    event EventHandler<AppearanceStateChangedEventArgs>? Changed;
    Task InitializeAsync(MudThemeProvider themeProvider, CancellationToken cancellationToken = default);
    Task SetActiveProfileAsync(Guid profileId, CancellationToken cancellationToken = default);
    Task ClonePresetAndActivateAsync(Guid presetId, CancellationToken cancellationToken = default);
    Task SetThemeModeAsync(string mode, CancellationToken cancellationToken = default);
    Task SetDirectionAsync(string direction, CancellationToken cancellationToken = default);
    Task UpdateCurrentProfileAsync(UpdateAppearanceProfileRequestDto request, CancellationToken cancellationToken = default);
    Task<UserAppearanceProfileDto?> CreateCustomProfileAsync(CreateCustomProfileRequestDto request, CancellationToken cancellationToken = default);
    UiThemePaletteDto GeneratePalettePreview(string naturalColor, string brandColor, bool isDark);
    Task<UiThemePaletteDto?> GeneratePalettePreviewAsync(string naturalColor, string brandColor, bool isDark, CancellationToken cancellationToken = default);
    MudTheme CreateTheme(string appbarHeight);
    Task<bool> ResolveEffectiveDarkModeAsync(MudThemeProvider themeProvider);
    Task ArchiveProfileAsync(Guid profileId, CancellationToken cancellationToken = default);
    Task<UserAppearanceProfileDto?> DuplicateProfileAsync(Guid profileId, string? name, CancellationToken cancellationToken = default);
    Task RefreshProfilesAsync(CancellationToken cancellationToken = default);
    Task<ICollection<UiThemeListItemDto>> GetThemeCatalogAsync(bool isPlatformCatalog, bool activeOnly, CancellationToken cancellationToken = default);
    Task<UiThemeDetailsDto> GetThemeDetailsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid> CreateThemeAsync(CreateUiThemeDto request, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid> UpdateThemeAsync(Guid id, UpdateUiThemeDto request, CancellationToken cancellationToken = default);
    Task DeleteThemeAsync(Guid id, CancellationToken cancellationToken = default);
}

public class AppearanceState
{
    public ResolvedAppearanceDto? ResolvedAppearance { get; set; }
    public IReadOnlyList<AvailablePresetDto> AvailablePresets { get; set; } = Array.Empty<AvailablePresetDto>();
    public IReadOnlyList<UserAppearanceProfileDto> UserProfiles { get; set; } = Array.Empty<UserAppearanceProfileDto>();
    public string ThemeMode { get; set; } = "system";
    public bool? ServerEffectiveDarkMode { get; set; }
    public string Direction { get; set; } = "auto";
    public string Language { get; set; } = "en";
    public bool IsInitialized { get; set; }
}

public class AppearanceStateChangedEventArgs : EventArgs
{
    public AppearanceState State { get; init; } = new();
}
