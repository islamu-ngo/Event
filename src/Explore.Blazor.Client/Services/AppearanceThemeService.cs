using Explore.Blazor.Client.Clients;
using MudBlazor;
using MudBlazor.Utilities;

namespace Explore.Blazor.Client.Services;

public sealed class AppearanceThemeService : IAppearanceThemeService
{
    private readonly IUserAppearanceClient _api;
    private readonly IUiThemeAdminClient _themeAdminClient;
    private readonly ILogger<AppearanceThemeService> _logger;

    private AppearanceState _current = new();
    private bool _isInitialized;

    private static readonly PaletteLight BuiltInLight = new()
    {
        Primary = "#18181B",
        PrimaryContrastText = "#FFFFFF",
        Secondary = "#52525B",
        SecondaryContrastText = "#FFFFFF",
        Black = "#09090B",
        AppbarText = "#18181B",
        AppbarBackground = "#FFFFFF",
        Background = "#F5F5F7",
        Surface = "#FFFFFF",
        DrawerBackground = "#FFFFFF",
        DrawerText = "#18181B",
        DrawerIcon = "#52525B",
        GrayLight = "#D4D4D8",
        GrayLighter = "#FAFAFA",
        ActionDefault = "#52525B",
        ActionDisabled = "#52525B",
        ActionDisabledBackground = "#E4E4E7",
        TextPrimary = "#18181B",
        TextSecondary = "#404040",
        TextDisabled = "#52525B",
        Info = "#52525B",
        Success = "#047857",
        Warning = "#B45309",
        Error = "#DC2626",
        LinesDefault = "#A1A1AA",
        LinesInputs = "#A1A1AA",
        TableLines = "#D4D4D8",
        Divider = "#E4E4E7",
        OverlayLight = "rgba(250,250,250,0.8)"
    };

    private static readonly PaletteDark BuiltInDark = new()
    {
        Primary = "#FAFAFA",
        PrimaryContrastText = "#1A1A1A",
        Secondary = "#A1A1AA",
        SecondaryContrastText = "#1A1A1A",
        Surface = "#242424",
        Background = "#1A1A1A",
        BackgroundGray = "#242424",
        AppbarText = "#FAFAFA",
        AppbarBackground = "rgba(26,26,26,0.92)",
        DrawerBackground = "#1A1A1A",
        ActionDefault = "#A1A1AA",
        ActionDisabled = "#3F3F46",
        ActionDisabledBackground = "#27272A",
        TextPrimary = "#FAFAFA",
        TextSecondary = "#A1A1AA",
        TextDisabled = "#52525B",
        DrawerIcon = "#A1A1AA",
        DrawerText = "#FAFAFA",
        GrayLight = "#3F3F46",
        GrayLighter = "#27272A",
        Info = "#A1A1AA",
        Success = "#34D399",
        Warning = "#FBBF24",
        Error = "#F87171",
        LinesDefault = "#3F3F46",
        LinesInputs = "#3F3F46",
        TableLines = "#3F3F46",
        Divider = "#2E2E2E",
        OverlayLight = "rgba(0,0,0,0.7)"
    };

    private static readonly Typography Typography = new()
    {
        Default = new DefaultTypography
        {
            FontFamily = ["Inter", "system-ui", "-apple-system", "sans-serif"],
            FontSize = ".9375rem",
            FontWeight = "400",
            LineHeight = "1.5",
            LetterSpacing = "-.011em"
        },
        H1 = new H1Typography { FontSize = "clamp(1.875rem, 1.5rem + 1.04vw, 2.5rem)", FontWeight = "700", LineHeight = "1.2", LetterSpacing = "-.022em" },
        H2 = new H2Typography { FontSize = "clamp(1.625rem, 1.375rem + 0.625vw, 2rem)", FontWeight = "600", LineHeight = "1.3", LetterSpacing = "-.017em" },
        H3 = new H3Typography { FontSize = "clamp(1.5rem, 1.333rem + 0.42vw, 1.75rem)", FontWeight = "600", LineHeight = "1.3", LetterSpacing = "-.014em" },
        H4 = new H4Typography { FontSize = "clamp(1.25rem, 1.083rem + 0.42vw, 1.5rem)", FontWeight = "600", LineHeight = "1.4" },
        H5 = new H5Typography { FontSize = "clamp(1.125rem, 1.042rem + 0.21vw, 1.25rem)", FontWeight = "600", LineHeight = "1.5" },
        H6 = new H6Typography { FontSize = "1.125rem", FontWeight = "600", LineHeight = "1.6" },
        Body1 = new Body1Typography { FontSize = ".9375rem", LineHeight = "1.6", LetterSpacing = "-.011em" },
        Body2 = new Body2Typography { FontSize = ".875rem", LineHeight = "1.5" },
        Button = new ButtonTypography { FontSize = ".875rem", FontWeight = "500", TextTransform = "none", LetterSpacing = "-.011em" },
        Caption = new CaptionTypography { FontSize = ".8125rem", LineHeight = "1.5" },
        Overline = new OverlineTypography { FontSize = ".75rem", FontWeight = "500", TextTransform = "uppercase", LetterSpacing = ".08em" }
    };

    public AppearanceState Current => _current;
    public event EventHandler<AppearanceStateChangedEventArgs>? Changed;

    public AppearanceThemeService(
        IUserAppearanceClient api,
        IUiThemeAdminClient themeAdminClient,
        ILogger<AppearanceThemeService> logger)
    {
        _api = api;
        _themeAdminClient = themeAdminClient;
        _logger = logger;
    }

    public async Task InitializeAsync(MudThemeProvider themeProvider, CancellationToken cancellationToken = default)
    {
        if (_isInitialized) return;

        try
        {
            var response = await ReadResolvedAppearanceAsync(cancellationToken);
            if (response is not null)
            {
                _current.ResolvedAppearance = response;
                _current.ThemeMode = response.ThemeMode;
                _current.ServerEffectiveDarkMode = response.ServerEffectiveDarkMode;
                _current.Direction = response.Direction;
                _current.Language = response.Language;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not load resolved appearance from BFF.");
        }

        try
        {
            _current.AvailablePresets = (await _api.GetAvailableThemesAsync(cancellationToken: cancellationToken)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error loading available presets from BFF.");
            _current.AvailablePresets = Array.Empty<AvailablePresetDto>();
        }

        try
        {
            var profiles = await ReadProfilesAsync(cancellationToken);
            _current.UserProfiles = profiles ?? Array.Empty<UserAppearanceProfileDto>();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not load user appearance profiles from BFF.");
            _current.UserProfiles = Array.Empty<UserAppearanceProfileDto>();
        }

        _current.IsInitialized = true;
        _isInitialized = true;
        Changed?.Invoke(this, new AppearanceStateChangedEventArgs { State = _current });
    }

    public async Task SetActiveProfileAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        var previousMode = _current.ThemeMode;
        var previousDirection = _current.Direction;
        var previousLanguage = _current.Language;

        try
        {
            await _api.SetActiveAppearanceProfileAsync(
                new SetActiveProfileRequestDto { ProfileId = profileId },
                cancellationToken: cancellationToken);

            _current.ResolvedAppearance = await ReadResolvedAppearanceAsync(cancellationToken);
            Changed?.Invoke(this, new AppearanceStateChangedEventArgs { State = _current });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error setting active profile.");
            _current.ThemeMode = previousMode;
            _current.Direction = previousDirection;
            _current.Language = previousLanguage;
            Changed?.Invoke(this, new AppearanceStateChangedEventArgs { State = _current });
        }
    }

    public async Task ClonePresetAndActivateAsync(Guid presetId, CancellationToken cancellationToken = default)
    {
        var previousMode = _current.ThemeMode;
        var previousDirection = _current.Direction;
        var previousLanguage = _current.Language;

        try
        {
            var profileDto = await _api.ClonePresetToProfileAsync(
                presetId,
                body: new ClonePresetRequestDto(),
                cancellationToken: cancellationToken);

            if (profileDto is not null)
            {
                await _api.SetActiveAppearanceProfileAsync(
                    new SetActiveProfileRequestDto { ProfileId = profileDto.Id },
                    cancellationToken: cancellationToken);

                _current.ResolvedAppearance = await ReadResolvedAppearanceAsync(cancellationToken);
            }

            Changed?.Invoke(this, new AppearanceStateChangedEventArgs { State = _current });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error cloning preset.");
            _current.ThemeMode = previousMode;
            _current.Direction = previousDirection;
            _current.Language = previousLanguage;
            Changed?.Invoke(this, new AppearanceStateChangedEventArgs { State = _current });
        }
    }

    public async Task SetThemeModeAsync(string mode, CancellationToken cancellationToken = default)
    {
        var previousMode = _current.ThemeMode;

        try
        {
            _current.ThemeMode = mode;
            Changed?.Invoke(this, new AppearanceStateChangedEventArgs { State = _current });

            await _api.SetAppearanceThemeModeAsync(
                new SetThemeModeRequestDto { ThemeMode = mode },
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error setting theme mode.");
            _current.ThemeMode = previousMode;
            Changed?.Invoke(this, new AppearanceStateChangedEventArgs { State = _current });
        }
    }

    public async Task SetDirectionAsync(string direction, CancellationToken cancellationToken = default)
    {
        var previousDirection = _current.Direction;

        try
        {
            _current.Direction = direction;
            Changed?.Invoke(this, new AppearanceStateChangedEventArgs { State = _current });

            await _api.UpdateCurrentUserAppearancePreferencesAsync(
                new UpdateUserAppearancePreferencesDto
                {
                    Localization = new UpdateAppearanceLocalizationDto { Direction = direction }
                },
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error setting text direction.");
            _current.Direction = previousDirection;
            Changed?.Invoke(this, new AppearanceStateChangedEventArgs { State = _current });
            throw;
        }
    }

    public async Task UpdateCurrentProfileAsync(UpdateAppearanceProfileRequestDto request, CancellationToken cancellationToken = default)
    {
        var activeProfileId = _current.ResolvedAppearance?.ActiveProfileId;
        if (activeProfileId is null) return;

        try
        {
            await _api.UpdateAppearanceProfileAsync(
                activeProfileId.Value,
                request,
                cancellationToken: cancellationToken);

            _current.ResolvedAppearance = await ReadResolvedAppearanceAsync(cancellationToken);
            Changed?.Invoke(this, new AppearanceStateChangedEventArgs { State = _current });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error updating current profile.");
        }
    }

    public async Task<UserAppearanceProfileDto?> CreateCustomProfileAsync(CreateCustomProfileRequestDto request, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _api.CreateCustomAppearanceProfileAsync(request, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error creating custom profile.");
            return null;
        }
    }

    public UiThemePaletteDto GeneratePalettePreview(string naturalColor, string brandColor, bool isDark)
    {
        return GetFallbackPalette(isDark);
    }

    public async Task<UiThemePaletteDto?> GeneratePalettePreviewAsync(string naturalColor, string brandColor, bool isDark, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _api.GenerateAppearancePaletteAsync(
                naturalColor,
                brandColor,
                isDark,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error generating palette preview.");
            return null;
        }
    }

    public MudTheme CreateTheme(string appbarHeight)
    {
        var theme = _current.ResolvedAppearance?.Theme;
        var light = theme?.LightPalette is not null ? ComposeLight(theme.LightPalette) : BuiltInLight;
        var dark = theme?.DarkPalette is not null ? ComposeDark(theme.DarkPalette) : BuiltInDark;

        return new MudTheme
        {
            PaletteLight = light,
            PaletteDark = dark,
            Typography = Typography,
            LayoutProperties = new LayoutProperties
            {
                DefaultBorderRadius = "12px",
                AppbarHeight = appbarHeight
            }
        };
    }

    public async Task<bool> ResolveEffectiveDarkModeAsync(MudThemeProvider themeProvider)
    {
        var mode = _current.ThemeMode.ToLowerInvariant();

        if (mode is "dark" or "darkhighcontrast") return true;
        if (mode is "light" or "lighthighcontrast") return false;

        if (_current.ServerEffectiveDarkMode.HasValue)
        {
            return _current.ServerEffectiveDarkMode.Value;
        }

        try
        {
            return await themeProvider.GetSystemDarkModeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error resolving system dark mode preference.");
            return false;
        }
    }

    public async Task ArchiveProfileAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _api.ArchiveAppearanceProfileAsync(profileId, cancellationToken: cancellationToken);

            await RefreshProfilesAsync(cancellationToken);
            Changed?.Invoke(this, new AppearanceStateChangedEventArgs { State = _current });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error archiving profile.");
        }
    }

    public async Task<UserAppearanceProfileDto?> DuplicateProfileAsync(Guid profileId, string? name, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = name is not null
                ? new ClonePresetRequestDto { Name = name }
                : null;
            return await _api.DuplicateAppearanceProfileAsync(
                profileId,
                body: request,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error duplicating profile.");
            return null;
        }
    }

    public async Task RefreshProfilesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var profiles = await ReadProfilesAsync(cancellationToken);
            _current.UserProfiles = profiles ?? Array.Empty<UserAppearanceProfileDto>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error refreshing profiles.");
        }
    }

    public Task<ICollection<UiThemeListItemDto>> GetThemeCatalogAsync(
        bool isPlatformCatalog,
        bool activeOnly,
        CancellationToken cancellationToken = default) =>
        _themeAdminClient.GetUiThemeCatalogAsync(
            isPlatformCatalog,
            activeOnly,
            cancellationToken: cancellationToken);

    public Task<UiThemeDetailsDto> GetThemeDetailsAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        _themeAdminClient.GetUiThemeDetailsAsync(id, cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfGuid> CreateThemeAsync(
        CreateUiThemeDto request,
        CancellationToken cancellationToken = default) =>
        _themeAdminClient.CreateUiThemeAsync(request, cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfGuid> UpdateThemeAsync(
        Guid id,
        UpdateUiThemeDto request,
        CancellationToken cancellationToken = default) =>
        _themeAdminClient.UpdateUiThemeAsync(id, request, cancellationToken: cancellationToken);

    public Task DeleteThemeAsync(Guid id, CancellationToken cancellationToken = default) =>
        _themeAdminClient.DeleteUiThemeAsync(id, cancellationToken: cancellationToken);

    private static UiThemePaletteDto GetFallbackPalette(bool isDark) => isDark
        ? new UiThemePaletteDto
        {
            Primary = "#FAFAFA",
            PrimaryContrastText = "#1A1A1A",
            Secondary = "#A1A1AA",
            SecondaryContrastText = "#1A1A1A",
            Background = "#1A1A1A",
            Surface = "#242424",
            AppbarBackground = "rgba(26,26,26,0.92)",
            AppbarText = "#FAFAFA",
            DrawerBackground = "#1A1A1A",
            DrawerText = "#FAFAFA",
            DrawerIcon = "#A1A1AA",
            TextPrimary = "#FAFAFA",
            TextSecondary = "#A1A1AA",
            Info = "#A1A1AA",
            Success = "#34D399",
            Warning = "#FBBF24",
            Error = "#F87171",
            LinesDefault = "#3F3F46",
            Divider = "#2E2E2E"
        }
        : new UiThemePaletteDto
        {
            Primary = "#18181B",
            PrimaryContrastText = "#FFFFFF",
            Secondary = "#52525B",
            SecondaryContrastText = "#FFFFFF",
            Background = "#F5F5F7",
            Surface = "#FFFFFF",
            AppbarBackground = "#FFFFFF",
            AppbarText = "#18181B",
            DrawerBackground = "#FFFFFF",
            DrawerText = "#18181B",
            DrawerIcon = "#52525B",
            TextPrimary = "#18181B",
            TextSecondary = "#404040",
            Info = "#52525B",
            Success = "#047857",
            Warning = "#B45309",
            Error = "#DC2626",
            LinesDefault = "#A1A1AA",
            Divider = "#E4E4E7"
        };

    private async Task<ResolvedAppearanceDto?> ReadResolvedAppearanceAsync(CancellationToken cancellationToken)
    {
        return await _api.GetCurrentUserAppearancePreferencesAsync(cancellationToken: cancellationToken);
    }

    private async Task<IReadOnlyList<UserAppearanceProfileDto>?> ReadProfilesAsync(CancellationToken cancellationToken)
    {
        return (await _api.GetUserAppearanceProfilesAsync(cancellationToken: cancellationToken)).ToList();
    }

    private static string PaletteValue(string? value, string fallback)
    {
        var candidate = value?.Trim();
        if (string.IsNullOrEmpty(candidate))
        {
            return fallback;
        }

        if (candidate.StartsWith("#rgb", StringComparison.OrdinalIgnoreCase)
            && MudColor.TryParse(candidate[1..], out _))
        {
            return candidate[1..];
        }

        if (MudColor.TryParse(candidate, out _))
        {
            return candidate;
        }

        return fallback;
    }

    private static PaletteLight ComposeLight(UiThemePaletteDto dto) => new()
    {
        Primary = PaletteValue(dto.Primary, BuiltInLight.Primary.ToString()),
        PrimaryContrastText = PaletteValue(dto.PrimaryContrastText, BuiltInLight.PrimaryContrastText.ToString()),
        Secondary = PaletteValue(dto.Secondary, BuiltInLight.Secondary.ToString()),
        SecondaryContrastText = PaletteValue(dto.SecondaryContrastText, BuiltInLight.SecondaryContrastText.ToString()),
        Black = BuiltInLight.Black,
        AppbarText = PaletteValue(dto.AppbarText, BuiltInLight.AppbarText.ToString()),
        AppbarBackground = PaletteValue(dto.AppbarBackground, BuiltInLight.AppbarBackground.ToString()),
        Background = PaletteValue(dto.Background, BuiltInLight.Background.ToString()),
        Surface = PaletteValue(dto.Surface, BuiltInLight.Surface.ToString()),
        DrawerBackground = PaletteValue(dto.DrawerBackground, BuiltInLight.DrawerBackground.ToString()),
        DrawerText = PaletteValue(dto.DrawerText, BuiltInLight.DrawerText.ToString()),
        DrawerIcon = PaletteValue(dto.DrawerIcon, BuiltInLight.DrawerIcon.ToString()),
        GrayLight = BuiltInLight.GrayLight,
        GrayLighter = BuiltInLight.GrayLighter,
        ActionDefault = BuiltInLight.ActionDefault,
        ActionDisabled = BuiltInLight.ActionDisabled,
        ActionDisabledBackground = BuiltInLight.ActionDisabledBackground,
        TextPrimary = PaletteValue(dto.TextPrimary, BuiltInLight.TextPrimary.ToString()),
        TextSecondary = PaletteValue(dto.TextSecondary, BuiltInLight.TextSecondary.ToString()),
        TextDisabled = BuiltInLight.TextDisabled,
        Info = PaletteValue(dto.Info, BuiltInLight.Info.ToString()),
        Success = PaletteValue(dto.Success, BuiltInLight.Success.ToString()),
        Warning = PaletteValue(dto.Warning, BuiltInLight.Warning.ToString()),
        Error = PaletteValue(dto.Error, BuiltInLight.Error.ToString()),
        LinesDefault = PaletteValue(dto.LinesDefault, BuiltInLight.LinesDefault.ToString()),
        LinesInputs = PaletteValue(dto.LinesDefault, BuiltInLight.LinesInputs.ToString()),
        TableLines = PaletteValue(dto.LinesDefault, BuiltInLight.TableLines.ToString()),
        Divider = PaletteValue(dto.Divider, BuiltInLight.Divider.ToString()),
        OverlayLight = BuiltInLight.OverlayLight
    };

    private static PaletteDark ComposeDark(UiThemePaletteDto dto) => new()
    {
        Primary = PaletteValue(dto.Primary, BuiltInDark.Primary.ToString()),
        PrimaryContrastText = PaletteValue(dto.PrimaryContrastText, BuiltInDark.PrimaryContrastText.ToString()),
        Secondary = PaletteValue(dto.Secondary, BuiltInDark.Secondary.ToString()),
        SecondaryContrastText = PaletteValue(dto.SecondaryContrastText, BuiltInDark.SecondaryContrastText.ToString()),
        Surface = PaletteValue(dto.Surface, BuiltInDark.Surface.ToString()),
        Background = PaletteValue(dto.Background, BuiltInDark.Background.ToString()),
        BackgroundGray = BuiltInDark.BackgroundGray,
        AppbarText = PaletteValue(dto.AppbarText, BuiltInDark.AppbarText.ToString()),
        AppbarBackground = PaletteValue(dto.AppbarBackground, BuiltInDark.AppbarBackground.ToString()),
        DrawerBackground = PaletteValue(dto.DrawerBackground, BuiltInDark.DrawerBackground.ToString()),
        ActionDefault = BuiltInDark.ActionDefault,
        ActionDisabled = BuiltInDark.ActionDisabled,
        ActionDisabledBackground = BuiltInDark.ActionDisabledBackground,
        TextPrimary = PaletteValue(dto.TextPrimary, BuiltInDark.TextPrimary.ToString()),
        TextSecondary = PaletteValue(dto.TextSecondary, BuiltInDark.TextSecondary.ToString()),
        TextDisabled = BuiltInDark.TextDisabled,
        DrawerIcon = PaletteValue(dto.DrawerIcon, BuiltInDark.DrawerIcon.ToString()),
        DrawerText = PaletteValue(dto.DrawerText, BuiltInDark.DrawerText.ToString()),
        GrayLight = BuiltInDark.GrayLight,
        GrayLighter = BuiltInDark.GrayLighter,
        Info = PaletteValue(dto.Info, BuiltInDark.Info.ToString()),
        Success = PaletteValue(dto.Success, BuiltInDark.Success.ToString()),
        Warning = PaletteValue(dto.Warning, BuiltInDark.Warning.ToString()),
        Error = PaletteValue(dto.Error, BuiltInDark.Error.ToString()),
        LinesDefault = PaletteValue(dto.LinesDefault, BuiltInDark.LinesDefault.ToString()),
        LinesInputs = PaletteValue(dto.LinesDefault, BuiltInDark.LinesInputs.ToString()),
        TableLines = PaletteValue(dto.LinesDefault, BuiltInDark.TableLines.ToString()),
        Divider = PaletteValue(dto.Divider, BuiltInDark.Divider.ToString()),
        OverlayLight = BuiltInDark.OverlayLight
    };
}
