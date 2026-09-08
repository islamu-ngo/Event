using Explore.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Explore.Blazor.Client.Layout;

public partial class SetupLayout : LayoutComponentBase
{
    private bool _isDarkMode;
    private MudThemeProvider _mudThemeProvider = null!;
    private MudTheme _theme = null!;

    [Inject]
    private IAppearanceThemeService AppearanceThemeService { get; set; } = null!;

    [CascadingParameter(Name = "InitialTheme")]
    public bool? InitialTheme { get; set; }

    private string ActiveColorScheme => _isDarkMode ? "dark" : "light";

    protected override void OnInitialized()
    {
        if (InitialTheme.HasValue)
        {
            _isDarkMode = InitialTheme.Value;
        }

        _theme = AppearanceThemeService.CreateTheme(appbarHeight: "64px");
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && !InitialTheme.HasValue)
        {
            try
            {
                _isDarkMode = await AppearanceThemeService.ResolveEffectiveDarkModeAsync(_mudThemeProvider);
                StateHasChanged();
            }
            catch
            {
                // Ignore — default to light
            }
        }
    }

    private async Task ToggleDarkMode()
    {
        _isDarkMode = !_isDarkMode;
        var mode = _isDarkMode ? "dark" : "light";
        await AppearanceThemeService.SetThemeModeAsync(mode);
    }
}
