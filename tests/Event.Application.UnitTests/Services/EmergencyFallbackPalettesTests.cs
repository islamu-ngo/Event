namespace Explore.Application.UnitTests.Services;

using Explore.Application.Services;
using Explore.Domain.ValueObjects;

public class EmergencyFallbackPalettesTests
{
    [Test]
    public async Task FallbackLight_Should_Have_All_18_Tokens_Populated()
    {
        var palette = EmergencyFallbackPalettes.FallbackLight;

        await AssertTokenNotEmpty(palette.Primary);
        await AssertTokenNotEmpty(palette.PrimaryContrastText);
        await AssertTokenNotEmpty(palette.Secondary);
        await AssertTokenNotEmpty(palette.SecondaryContrastText);
        await AssertTokenNotEmpty(palette.Background);
        await AssertTokenNotEmpty(palette.Surface);
        await AssertTokenNotEmpty(palette.AppbarBackground);
        await AssertTokenNotEmpty(palette.AppbarText);
        await AssertTokenNotEmpty(palette.DrawerBackground);
        await AssertTokenNotEmpty(palette.DrawerText);
        await AssertTokenNotEmpty(palette.DrawerIcon);
        await AssertTokenNotEmpty(palette.TextPrimary);
        await AssertTokenNotEmpty(palette.TextSecondary);
        await AssertTokenNotEmpty(palette.Info);
        await AssertTokenNotEmpty(palette.Success);
        await AssertTokenNotEmpty(palette.Warning);
        await AssertTokenNotEmpty(palette.Error);
        await AssertTokenNotEmpty(palette.LinesDefault);
        await AssertTokenNotEmpty(palette.Divider);
    }

    [Test]
    public async Task FallbackDark_Should_Have_All_18_Tokens_Populated()
    {
        var palette = EmergencyFallbackPalettes.FallbackDark;

        await AssertTokenNotEmpty(palette.Primary);
        await AssertTokenNotEmpty(palette.PrimaryContrastText);
        await AssertTokenNotEmpty(palette.Secondary);
        await AssertTokenNotEmpty(palette.SecondaryContrastText);
        await AssertTokenNotEmpty(palette.Background);
        await AssertTokenNotEmpty(palette.Surface);
        await AssertTokenNotEmpty(palette.AppbarBackground);
        await AssertTokenNotEmpty(palette.AppbarText);
        await AssertTokenNotEmpty(palette.DrawerBackground);
        await AssertTokenNotEmpty(palette.DrawerText);
        await AssertTokenNotEmpty(palette.DrawerIcon);
        await AssertTokenNotEmpty(palette.TextPrimary);
        await AssertTokenNotEmpty(palette.TextSecondary);
        await AssertTokenNotEmpty(palette.Info);
        await AssertTokenNotEmpty(palette.Success);
        await AssertTokenNotEmpty(palette.Warning);
        await AssertTokenNotEmpty(palette.Error);
        await AssertTokenNotEmpty(palette.LinesDefault);
        await AssertTokenNotEmpty(palette.Divider);
    }

    [Test]
    public async Task FallbackLightHighContrast_Should_Have_Black_Text_On_White()
    {
        var palette = EmergencyFallbackPalettes.FallbackLightHighContrast;

        await Assert.That(palette.TextPrimary).IsEqualTo("#000000");
        await Assert.That(palette.AppbarText).IsEqualTo("#000000");
        await Assert.That(palette.DrawerText).IsEqualTo("#000000");
        await Assert.That(palette.DrawerIcon).IsEqualTo("#000000");
        await Assert.That(palette.Background).IsEqualTo("#FFFFFF");
        await Assert.That(palette.Surface).IsEqualTo("#FFFFFF");
        await Assert.That(palette.LinesDefault).IsEqualTo("#000000");
        await Assert.That(palette.Divider).IsEqualTo("#000000");
    }

    [Test]
    public async Task FallbackDarkHighContrast_Should_Have_White_Text_On_Black()
    {
        var palette = EmergencyFallbackPalettes.FallbackDarkHighContrast;

        await Assert.That(palette.TextPrimary).IsEqualTo("#FFFFFF");
        await Assert.That(palette.AppbarText).IsEqualTo("#FFFFFF");
        await Assert.That(palette.DrawerText).IsEqualTo("#FFFFFF");
        await Assert.That(palette.DrawerIcon).IsEqualTo("#FFFFFF");
        await Assert.That(palette.Background).IsEqualTo("#000000");
        await Assert.That(palette.Surface).IsEqualTo("#0A0A0A");
        await Assert.That(palette.LinesDefault).IsEqualTo("#FFFFFF");
        await Assert.That(palette.Divider).IsEqualTo("#FFFFFF");
    }

    [Test]
    public async Task FallbackLight_Should_Match_Enterprise_Blue()
    {
        var palette = EmergencyFallbackPalettes.FallbackLight;

        await Assert.That(palette.Primary).IsEqualTo("#18181B");
        await Assert.That(palette.Background).IsEqualTo("#F5F5F7");
        await Assert.That(palette.Surface).IsEqualTo("#FFFFFF");
    }

    [Test]
    public async Task FallbackDark_Should_Match_Enterprise_Blue_Dark()
    {
        var palette = EmergencyFallbackPalettes.FallbackDark;

        await Assert.That(palette.Primary).IsEqualTo("#FAFAFA");
        await Assert.That(palette.PrimaryContrastText).IsEqualTo("#1A1A1A");
        await Assert.That(palette.Background).IsEqualTo("#1A1A1A");
    }

    private static async Task AssertTokenNotEmpty(string token)
    {
        await Assert.That(token).IsNotNull();
        await Assert.That(token).IsNotEmpty();
    }
}
