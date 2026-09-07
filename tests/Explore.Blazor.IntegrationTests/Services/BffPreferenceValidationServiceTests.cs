using Explore.Blazor.Services.Preferences;

namespace Explore.Blazor.IntegrationTests.Services;

public sealed class BffPreferenceValidationServiceTests
{
    [Test]
    public async Task NormalizeThemeMode_WithWhitespaceAndUppercase_ReturnsLowercaseMode()
    {
        var service = new BffPreferenceValidationService();

        var result = service.NormalizeThemeMode("  DarkHighContrast  ");

        await Assert.That(result).IsEqualTo("darkhighcontrast");
        await Assert.That(service.ThemeModeValidationMessage).Contains("custom");
    }

    [Test]
    public async Task NormalizeThemeMode_WithUnknownMode_ReturnsNull()
    {
        var service = new BffPreferenceValidationService();

        var result = service.NormalizeThemeMode("sepia");

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task NormalizeLanguage_WithRegisteredCulture_ReturnsCanonicalCode()
    {
        var service = new BffPreferenceValidationService();

        var result = service.NormalizeLanguage("fr");

        await Assert.That(result).IsEqualTo("fr");
        await Assert.That(result).IsNotNull();
    }

    [Test]
    public async Task NormalizeLanguage_WithUnknownCulture_ReturnsNull()
    {
        var service = new BffPreferenceValidationService();

        var result = service.NormalizeLanguage("zz");

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task NormalizeDirection_WithWhitespaceAndUppercase_ReturnsLowercaseDirection()
    {
        var service = new BffPreferenceValidationService();

        var result = service.NormalizeDirection(" RTL ");

        await Assert.That(result).IsEqualTo("rtl");
        await Assert.That(result).IsNotNull();
    }

    [Test]
    public async Task NormalizeDirection_WithUnknownDirection_ReturnsNull()
    {
        var service = new BffPreferenceValidationService();

        var result = service.NormalizeDirection("sideways");

        await Assert.That(result).IsNull();
    }
}
