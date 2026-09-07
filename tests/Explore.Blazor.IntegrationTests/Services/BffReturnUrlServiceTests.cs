using Explore.Blazor.Services.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using TUnit.Core;

namespace Explore.Blazor.IntegrationTests.Services;

public sealed class BffReturnUrlServiceTests
{
    private readonly BffReturnUrlService _service = new();

    [Test]
    public async Task GetSafeReturnUrl_Blank_ReturnsRoot()
    {
        var context = CreateContext(string.Empty);

        var result = _service.GetSafeReturnUrl(context, NullLogger.Instance);

        await Assert.That(result).IsEqualTo("/");
    }

    [Test]
    public async Task GetSafeReturnUrl_LocalPath_ReturnsPath()
    {
        var context = CreateContext("/admin/tenant/settings");

        var result = _service.GetSafeReturnUrl(context, NullLogger.Instance);

        await Assert.That(result).IsEqualTo("/admin/tenant/settings");
    }

    [Arguments("https://evil.example")]
    [Arguments("//evil.example")]
    [Arguments("/\\evil")]
    [Test]
    public async Task GetSafeReturnUrl_UnsafeValue_ReturnsRoot(string returnUrl)
    {
        var context = CreateContext(returnUrl);

        var result = _service.GetSafeReturnUrl(context, NullLogger.Instance);

        await Assert.That(result).IsEqualTo("/");
    }

    [Test]
    public async Task BuildLoginRedirectUrl_EncodesReturnUrlAndProvider()
    {
        var result = _service.BuildLoginRedirectUrl("/admin/tenant settings", "key cloak");

        await Assert.That(result).IsEqualTo("/login?returnUrl=%2Fadmin%2Ftenant%20settings&provider=key%20cloak");
        await Assert.That(result).DoesNotContain("challengeError=1");
    }

    [Test]
    public async Task BuildLoginRedirectUrl_WithChallengeError_PreservesExistingFlag()
    {
        var result = _service.BuildLoginRedirectUrl("/setup", challengeError: true);

        await Assert.That(result).IsEqualTo("/login?returnUrl=%2Fsetup&challengeError=1");
    }

    [Test]
    public async Task BuildChallengeRedirectUrl_WithProvider_EncodesProviderAndReturnUrl()
    {
        var result = _service.BuildChallengeRedirectUrl("/dashboard?tab=one two", "key cloak");

        await Assert.That(result).IsEqualTo("/auth/challenge?provider=key%20cloak&returnUrl=%2Fdashboard%3Ftab%3Done%20two");
    }

    [Test]
    public async Task BuildChallengeRedirectUrl_WithoutProvider_ReturnsLoginRedirect()
    {
        var result = _service.BuildChallengeRedirectUrl("/dashboard", provider: null);

        await Assert.That(result).IsEqualTo("/login?returnUrl=%2Fdashboard");
    }

    private static DefaultHttpContext CreateContext(string? returnUrl)
    {
        var context = new DefaultHttpContext();
        if (returnUrl is not null)
        {
            context.Request.QueryString = QueryString.Create("returnUrl", returnUrl);
        }

        return context;
    }
}
