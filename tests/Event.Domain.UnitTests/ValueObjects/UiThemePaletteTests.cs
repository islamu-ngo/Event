namespace Explore.Domain.UnitTests.ValueObjects;

using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

public class UiThemePaletteTests
{
    [Test]
    public async Task NormalizeHex_ShouldNormalize_3CharHex_To_6Char()
    {
        var result = UiThemePalette.NormalizeHex("#abc");

        await Assert.That(result).IsEqualTo("#AABBCC");
    }

    [Test]
    public async Task NormalizeHex_ShouldNormalize_6CharHex_To_Uppercase()
    {
        var result = UiThemePalette.NormalizeHex("#0f62fe");

        await Assert.That(result).IsEqualTo("#0F62FE");
    }

    [Test]
    public async Task NormalizeHex_Should_Handle_Hex_Without_Hash()
    {
        var result = UiThemePalette.NormalizeHex("3b82f6");

        await Assert.That(result).IsEqualTo("#3B82F6");
    }

    [Test]
    public async Task NormalizeHex_Should_Return_Black_For_Empty()
    {
        var result = UiThemePalette.NormalizeHex("");

        await Assert.That(result).IsEqualTo("#000000");
    }

    [Test]
    public async Task NormalizeHex_Should_Return_Black_For_Null()
    {
        var result = UiThemePalette.NormalizeHex(null!);

        await Assert.That(result).IsEqualTo("#000000");
    }

    [Test]
    public async Task NormalizeHex_Should_Return_Black_For_Whitespace()
    {
        var result = UiThemePalette.NormalizeHex("   ");

        await Assert.That(result).IsEqualTo("#000000");
    }

    [Test]
    public async Task Normalized_Should_Return_All_Tokens_In_Uppercase_6Char()
    {
        var palette = new UiThemePalette
        {
            Primary = "#0f62fe",
            PrimaryContrastText = "#fff",
            Secondary = "#475569",
            SecondaryContrastText = "#ffffff",
            Background = "#f1f5f9",
            Surface = "#ffffff",
            AppbarBackground = "#fff",
            AppbarText = "#1e293b",
            DrawerBackground = "#ffffff",
            DrawerText = "#1E293B",
            DrawerIcon = "#475569",
            TextPrimary = "#0f172a",
            TextSecondary = "#475569",
            Info = "#2563eb",
            Success = "#16a34a",
            Warning = "#d97706",
            Error = "#dc2626",
            LinesDefault = "#cbd5e1",
            Divider = "#cbd5e1"
        };

        var normalized = palette.Normalized();

        await Assert.That(normalized.Primary).IsEqualTo("#0F62FE");
        await Assert.That(normalized.PrimaryContrastText).IsEqualTo("#FFFFFF");
        await Assert.That(normalized.Surface).IsEqualTo("#FFFFFF");
        await Assert.That(normalized.AppbarBackground).IsEqualTo("#FFFFFF");
        await Assert.That(normalized.TextPrimary).IsEqualTo("#0F172A");
    }

    [Test]
    public async Task Normalized_Should_Preserve_Rgba_For_Translucent_Appbar()
    {
        var palette = CreateBlackPalette();

        var normalized = palette.Normalized();

        await Assert.That(normalized.AppbarBackground).IsEqualTo("rgba(18,18,18,0.92)");
    }

    [Test]
    public async Task UiThemePreset_Should_Have_Stable_Key_And_Null_TenantId_For_System_Presets()
    {
        var preset = CreateSystemPreset();

        await Assert.That(preset.ThemeKey).IsEqualTo("enterprise-blue");
        await Assert.That(preset.TenantId).IsNull();
        await Assert.That(preset.IsSystem).IsTrue();
        await Assert.That(preset.IsEditable).IsFalse();
    }

    [Test]
    public async Task UserAppearanceProfile_Should_Default_To_System_Mode()
    {
        var profile = CreateProfile();

        await Assert.That(profile.ThemeMode).IsEqualTo(AppearanceThemeMode.System);
        await Assert.That(profile.IsDefault).IsFalse();
        await Assert.That(profile.IsArchived).IsFalse();
        await Assert.That(profile.IsUserEditable).IsTrue();
    }

    [Test]
    public async Task AppearanceThemeMode_Should_Include_HighContrast_And_Custom_Values()
    {
        await Assert.That((int)AppearanceThemeMode.LightHighContrast).IsEqualTo(3);
        await Assert.That((int)AppearanceThemeMode.DarkHighContrast).IsEqualTo(4);
        await Assert.That((int)AppearanceThemeMode.Custom).IsEqualTo(5);
    }

    [Test]
    public async Task AppearanceResolutionSource_Should_Cover_All_Fallback_Chain_Levels()
    {
        var values = Enum.GetValues<AppearanceResolutionSource>();

        await Assert.That(values).Contains(AppearanceResolutionSource.UserTenantProfile);
        await Assert.That(values).Contains(AppearanceResolutionSource.UserGlobalProfile);
        await Assert.That(values).Contains(AppearanceResolutionSource.TenantDefaultPreset);
        await Assert.That(values).Contains(AppearanceResolutionSource.InstanceDefaultPreset);
        await Assert.That(values).Contains(AppearanceResolutionSource.SystemPresetFallback);
        await Assert.That(values).Contains(AppearanceResolutionSource.EmergencyFallback);
    }

    private static UiThemePreset CreateSystemPreset() => new()
    {
        Id = Guid.Parse("a1b2c3d4-1111-1111-1111-111111111111"),
        TenantId = null,
        ThemeKey = "enterprise-blue",
        DisplayName = "Enterprise Blue",
        Description = "Test preset",
        LightPalette = CreateWhitePalette(),
        DarkPalette = CreateBlackPalette(),
        IsSystem = true,
        IsEditable = false,
        IsActive = true,
        SeedVersion = 2
    };

    private static UserAppearanceProfile CreateProfile() => new()
    {
        Id = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        TenantId = null,
        Name = "Test Profile",
        ThemeMode = AppearanceThemeMode.System,
        LightPaletteSnapshot = CreateWhitePalette(),
        DarkPaletteSnapshot = CreateBlackPalette(),
        IsUserEditable = true,
        IsDefault = false,
        IsArchived = false
    };

    private static UiThemePalette CreateWhitePalette() => new()
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
        Success = "#16A34A",
        Warning = "#D97706",
        Error = "#DC2626",
        LinesDefault = "#A1A1AA",
        Divider = "#E4E4E7"
    };

    private static UiThemePalette CreateBlackPalette() => new()
    {
        Primary = "#FAFAFA",
        PrimaryContrastText = "#1A1A1A",
        Secondary = "#A1A1AA",
        SecondaryContrastText = "#1A1A1A",
        Background = "#1A1A1A",
        Surface = "#242424",
        AppbarBackground = "rgba(18,18,18,0.92)",
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
    };
}
