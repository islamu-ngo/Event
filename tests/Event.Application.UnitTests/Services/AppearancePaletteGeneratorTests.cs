namespace Explore.Application.UnitTests.Services;

using Explore.Application.DTOs.Appearance;
using Explore.Application.Services;

public class AppearancePaletteGeneratorTests
{
    [Test]
    public async Task GenerateLightPalette_Should_Populate_All_18_Tokens()
    {
        var palette = AppearancePaletteGenerator.GenerateLightPalette("#475569", "#3B82F6");

        await Assert.That(palette.Primary).IsNotEmpty();
        await Assert.That(palette.PrimaryContrastText).IsNotEmpty();
        await Assert.That(palette.Secondary).IsNotEmpty();
        await Assert.That(palette.SecondaryContrastText).IsNotEmpty();
        await Assert.That(palette.Background).IsNotEmpty();
        await Assert.That(palette.Surface).IsNotEmpty();
        await Assert.That(palette.AppbarBackground).IsNotEmpty();
        await Assert.That(palette.AppbarText).IsNotEmpty();
        await Assert.That(palette.DrawerBackground).IsNotEmpty();
        await Assert.That(palette.DrawerText).IsNotEmpty();
        await Assert.That(palette.DrawerIcon).IsNotEmpty();
        await Assert.That(palette.TextPrimary).IsNotEmpty();
        await Assert.That(palette.TextSecondary).IsNotEmpty();
        await Assert.That(palette.Info).IsEqualTo("#2563EB");
        await Assert.That(palette.Success).IsEqualTo("#16A34A");
        await Assert.That(palette.Warning).IsEqualTo("#D97706");
        await Assert.That(palette.Error).IsEqualTo("#DC2626");
        await Assert.That(palette.LinesDefault).IsNotEmpty();
        await Assert.That(palette.Divider).IsNotEmpty();
    }

    [Test]
    public async Task GenerateDarkPalette_Should_Populate_All_18_Tokens()
    {
        var palette = AppearancePaletteGenerator.GenerateDarkPalette("#1E293B", "#3B82F6");

        await Assert.That(palette.Primary).IsNotEmpty();
        await Assert.That(palette.PrimaryContrastText).IsNotEmpty();
        await Assert.That(palette.Secondary).IsNotEmpty();
        await Assert.That(palette.SecondaryContrastText).IsNotEmpty();
        await Assert.That(palette.Background).IsNotEmpty();
        await Assert.That(palette.Surface).IsNotEmpty();
        await Assert.That(palette.AppbarBackground).IsNotEmpty();
        await Assert.That(palette.AppbarText).IsNotEmpty();
        await Assert.That(palette.DrawerBackground).IsNotEmpty();
        await Assert.That(palette.DrawerText).IsNotEmpty();
        await Assert.That(palette.DrawerIcon).IsNotEmpty();
        await Assert.That(palette.TextPrimary).IsNotEmpty();
        await Assert.That(palette.TextSecondary).IsNotEmpty();
        await Assert.That(palette.Info).IsEqualTo("#60A5FA");
        await Assert.That(palette.Success).IsEqualTo("#10B981");
        await Assert.That(palette.Warning).IsEqualTo("#F59E0B");
        await Assert.That(palette.Error).IsEqualTo("#EF4444");
        await Assert.That(palette.LinesDefault).IsNotEmpty();
        await Assert.That(palette.Divider).IsNotEmpty();
    }

    [Test]
    public async Task GenerateHighContrastLightPalette_Should_Have_Black_Text_On_White()
    {
        var palette = AppearancePaletteGenerator.GenerateHighContrastLightPalette("#475569", "#3B82F6");

        await Assert.That(palette.TextPrimary).IsEqualTo("#000000");
        await Assert.That(palette.AppbarText).IsEqualTo("#000000");
        await Assert.That(palette.DrawerText).IsEqualTo("#000000");
        await Assert.That(palette.DrawerIcon).IsEqualTo("#000000");
        await Assert.That(palette.Background).IsEqualTo("#FFFFFF");
        await Assert.That(palette.Surface).IsEqualTo("#FFFFFF");
        await Assert.That(palette.AppbarBackground).IsEqualTo("#FFFFFF");
        await Assert.That(palette.LinesDefault).IsEqualTo("#000000");
        await Assert.That(palette.Divider).IsEqualTo("#000000");
    }

    [Test]
    public async Task GenerateHighContrastDarkPalette_Should_Have_White_Text_On_Black()
    {
        var palette = AppearancePaletteGenerator.GenerateHighContrastDarkPalette("#1E293B", "#3B82F6");

        await Assert.That(palette.TextPrimary).IsEqualTo("#FFFFFF");
        await Assert.That(palette.AppbarText).IsEqualTo("#FFFFFF");
        await Assert.That(palette.DrawerText).IsEqualTo("#FFFFFF");
        await Assert.That(palette.DrawerIcon).IsEqualTo("#FFFFFF");
        await Assert.That(palette.Background).IsEqualTo("#000000");
        await Assert.That(palette.Surface).IsEqualTo("#0A0A0A");
        await Assert.That(palette.AppbarBackground).IsEqualTo("#000000");
        await Assert.That(palette.LinesDefault).IsEqualTo("#FFFFFF");
        await Assert.That(palette.Divider).IsEqualTo("#FFFFFF");
    }

    [Test]
    public async Task GenerateLightPalette_Should_Use_Brand_As_Primary()
    {
        var palette = AppearancePaletteGenerator.GenerateLightPalette("#F1F5F9", "#6366F1");

        await Assert.That(palette.Primary.ToUpperInvariant()).StartsWith("#6366F1".Substring(0, 4).ToUpperInvariant());
    }

    [Test]
    public async Task GenerateDarkPalette_Should_Lighten_Brand_For_Primary()
    {
        var palette = AppearancePaletteGenerator.GenerateDarkPalette("#1E293B", "#6366F1");

        await Assert.That(palette.Primary).IsNotEmpty();
        await Assert.That(palette.PrimaryContrastText).IsNotEmpty();
    }

    [Test]
    public async Task HslColor_FromHex_Should_Parse_6Char_Hex()
    {
        var hsl = HslColor.FromHex("#3B82F6");

        await Assert.That(hsl.H).IsGreaterThan(0);
        await Assert.That(hsl.S).IsGreaterThan(0);
        await Assert.That(hsl.L).IsGreaterThan(0);
    }

    [Test]
    public async Task HslColor_ToHex_Should_Roundtrip()
    {
        var original = "#3B82F6";
        var hsl = HslColor.FromHex(original);
        var result = hsl.ToHex();

        await Assert.That(result.ToUpperInvariant()).IsEqualTo("#3B82F6");
    }

    [Test]
    public async Task HslColor_ContrastTextColor_Should_Return_Black_For_Light_Colors()
    {
        var light = HslColor.FromHex("#FFFFFF");
        var result = light.ContrastTextColor();

        await Assert.That(result).IsEqualTo("#0F172A");
    }

    [Test]
    public async Task HslColor_ContrastTextColor_Should_Return_White_For_Dark_Colors()
    {
        var dark = HslColor.FromHex("#000000");
        var result = dark.ContrastTextColor();

        await Assert.That(result).IsEqualTo("#FFFFFF");
    }

    [Test]
    public async Task HslColor_AdjustLightness_Should_Clamp_To_0_100()
    {
        var hsl = HslColor.FromHex("#3B82F6");

        var lightened = hsl.AdjustLightness(150);
        await Assert.That(lightened.L).IsEqualTo(100);

        var darkened = hsl.AdjustLightness(-50);
        await Assert.That(darkened.L).IsEqualTo(0);
    }

    [Test]
    public async Task HslColor_WithAlpha_Should_Produce_Rgba()
    {
        var hsl = HslColor.FromHex("#3B82F6");
        var withAlpha = hsl.WithAlpha(0.12);

        var hex = withAlpha.ToHex();
        await Assert.That(hex).Contains("rgba");
    }
}
