using Explore.Blazor.Services.Preferences;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using NSubstitute;

namespace Explore.Blazor.IntegrationTests.Services;

public sealed class BffPreferenceCookieServiceTests
{
    [Test]
    public async Task BuildDefaultResolvedAppearance_WithCookieValues_ReturnsResolvedAppearance()
    {
        var service = CreateService();
        var context = CreateContext();
        context.Request.Headers.Cookie = "theme=darkhighcontrast; direction=rtl; lang=fr";

        var result = service.BuildDefaultResolvedAppearance(context);

        await Assert.That(result.ThemeMode).IsEqualTo("darkhighcontrast");
        await Assert.That(result.Direction).IsEqualTo("rtl");
        await Assert.That(result.Language).IsEqualTo("fr");
        await Assert.That(result.ServerEffectiveDarkMode).IsTrue();
        await Assert.That(result.Theme).IsNull();
    }

    [Test]
    public async Task ReadCookiePreferences_WithUnsupportedTheme_FallsBackToSystem()
    {
        var service = CreateService();
        var context = CreateContext();
        context.Request.Headers.Cookie = "theme=custom; direction=ltr; lang=fr";

        var result = service.ReadCookiePreferences(context);

        await Assert.That(result.ThemeMode).IsEqualTo("system");
        await Assert.That(result.Direction).IsEqualTo("ltr");
        await Assert.That(result.Language).IsEqualTo("fr");
        await Assert.That(result.DefaultThemeId).IsNull();
    }

    [Test]
    public async Task PersistLanguageCookies_AppendsLanguageAndAspNetCoreCultureCookies()
    {
        var service = CreateService();
        var context = CreateContext();

        service.PersistLanguageCookie(context, "fr");
        service.PersistAspNetCoreCultureCookie(context, "fr");

        var setCookieHeaders = context.Response.Headers.SetCookie.ToArray();
        await Assert.That(setCookieHeaders).Contains(header => header.StartsWith("lang=fr", StringComparison.Ordinal));
        await Assert.That(setCookieHeaders).Contains(header => header.StartsWith(".AspNetCore.Culture=", StringComparison.Ordinal));
        await Assert.That(setCookieHeaders).Count().IsEqualTo(2);
    }

    [Test]
    public async Task PersistDirectionCookie_WithAuto_DeletesDirectionCookie()
    {
        var service = CreateService();
        var context = CreateContext();

        service.PersistDirectionCookie(context, "auto");

        var setCookieHeader = context.Response.Headers.SetCookie.ToString();
        await Assert.That(setCookieHeader).Contains("direction=");
        await Assert.That(setCookieHeader.Split("expires=", StringSplitOptions.None).Length - 1).IsEqualTo(1);
        await Assert.That(setCookieHeader.ToLowerInvariant()).Contains("samesite=lax");
    }

    private static BffPreferenceCookieService CreateService()
    {
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.EnvironmentName.Returns("Development");

        return new BffPreferenceCookieService(environment);
    }

    private static DefaultHttpContext CreateContext() => new();
}
