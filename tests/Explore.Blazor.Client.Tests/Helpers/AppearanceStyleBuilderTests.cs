using Explore.Blazor.Client.Helpers;

namespace Explore.Blazor.Client.Tests.Helpers;

public sealed class AppearanceStyleBuilderTests
{
    [Test]
    public async Task BuildStyle_WithColorOnlyOverlay_AppliesOverlayImage()
    {
        var settings = new AppearanceSettings
        {
            BackgroundColor = "#5D7661",
            BackgroundEffect = "SoftOverlay"
        };

        var style = AppearanceStyleBuilder.BuildStyle(settings, "#F8FAFC");

        await Assert.That(style).Contains("background: #5D7661;");
        await Assert.That(style).Contains("background-image: linear-gradient(rgba(0,0,0,0.24), rgba(0,0,0,0.24));");
    }

    [Test]
    public async Task BuildStyle_WithImageAndStrongOverlay_ComposesColorOverlayAndImage()
    {
        var settings = new AppearanceSettings
        {
            BackgroundColor = "#A86D4F",
            ImageUri = "https://cdn.example.test/theme.jpg",
            BackgroundEffect = "StrongOverlay"
        };

        var style = AppearanceStyleBuilder.BuildStyle(settings, "#F8FAFC");

        await Assert.That(style).Contains("background: #A86D4F;");
        await Assert.That(style).Contains("background-image: linear-gradient(rgba(0,0,0,0.40), rgba(0,0,0,0.40)), url('https://cdn.example.test/theme.jpg');");
        await Assert.That(style).Contains("background-position: center;");
        await Assert.That(style).Contains("background-size: cover;");
    }

    [Test]
    public async Task BuildSurfaceStyle_WithDarkThemeColor_UsesReadableLightTextVariables()
    {
        var settings = new AppearanceSettings
        {
            BackgroundColor = "#1E293B",
            BackgroundEffect = "None"
        };

        var style = AppearanceStyleBuilder.BuildSurfaceStyle(settings, "#F8FAFC");

        await Assert.That(style).Contains("--event-theme-text-color: #FFFFFF;");
        await Assert.That(style).Contains("--event-theme-muted-color: rgba(255,255,255,0.84);");
        await Assert.That(style).Contains("color: var(--event-theme-text-color);");
    }

    [Test]
    public async Task BuildSurfaceStyle_WithLightFallback_UsesReadableDarkTextVariables()
    {
        var settings = new AppearanceSettings();

        var style = AppearanceStyleBuilder.BuildSurfaceStyle(settings, "#F8FAFC");

        await Assert.That(style).Contains("--event-theme-text-color: #000000;");
        await Assert.That(style).Contains("--event-theme-muted-color: rgba(0,0,0,0.76);");
    }

    [Test]
    public async Task BuildSurfaceStyle_WithMidToneClayPreset_UsesAaSafeDarkTextVariables()
    {
        var settings = new AppearanceSettings
        {
            BackgroundColor = "#A86D4F",
            BackgroundEffect = "None"
        };

        var style = AppearanceStyleBuilder.BuildSurfaceStyle(settings, "#F8FAFC");

        await Assert.That(style).Contains("--event-theme-text-color: #000000;");
    }
}
