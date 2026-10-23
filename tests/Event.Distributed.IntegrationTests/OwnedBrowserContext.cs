using Microsoft.Playwright;

namespace Event.Distributed.IntegrationTests;

internal enum BrowserJourney
{
    LoginLogout,
    CookieExpiry,
    ServerTokenExpiry,
    TenantBoundary,
    KeyboardLanguage,
    FreeRegistration
}

internal static class BrowserArtifactPolicy
{
    public static void AdmitDiagnostics(string? debug, string? playwrightDebug)
    {
        if (!string.IsNullOrEmpty(debug) || !string.IsNullOrEmpty(playwrightDebug))
            throw new InvalidOperationException("browser_raw_diagnostics_not_admitted");
    }

    public static void Admit(string artifact)
    {
        if (artifact != "outcome")
            throw new InvalidOperationException("browser_artifact_not_admitted");
    }

    public static string Outcome(BrowserJourney journey, bool passed)
    {
        string name = journey switch
        {
            BrowserJourney.LoginLogout => "login_logout",
            BrowserJourney.CookieExpiry => "cookie_expiry",
            BrowserJourney.ServerTokenExpiry => "server_token_expiry",
            BrowserJourney.TenantBoundary => "tenant_boundary",
            BrowserJourney.KeyboardLanguage => "keyboard_language",
            BrowserJourney.FreeRegistration => "free_registration",
            _ => throw new InvalidOperationException("browser_journey_not_admitted")
        };
        return $"browser_{name}_{(passed ? "passed" : "failed")}";
    }

    public static Exception Sanitize(BrowserJourney journey, Exception failure)
    {
        // Playwright errors can contain input values, DOM text and request URLs.
        ArgumentNullException.ThrowIfNull(failure);
        return new InvalidOperationException(Outcome(journey, false));
    }
}

internal sealed class OwnedBrowserContext : IAsyncDisposable
{
    private readonly IPlaywright playwright;
    private readonly IBrowser browser;

    private OwnedBrowserContext(IPlaywright playwright, IBrowser browser, IBrowserContext context, IPage page)
    {
        this.playwright = playwright;
        this.browser = browser;
        Context = context;
        Page = page;
    }

    public IBrowserContext Context { get; }
    public IPage Page { get; }

    public static async Task<OwnedBrowserContext> CreateAsync(Uri origin)
    {
        BrowserArtifactPolicy.AdmitDiagnostics(
            Environment.GetEnvironmentVariable("DEBUG"), Environment.GetEnvironmentVariable("PWDEBUG"));
        if (!origin.IsLoopback || origin.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("browser_origin_not_owned_loopback");

        IPlaywright playwright = await Playwright.CreateAsync();
        IBrowser? browser = null;
        try
        {
            browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
            IBrowserContext context = await browser.NewContextAsync(new()
            {
                BaseURL = origin.AbsoluteUri,
                Locale = "en-GB",
                TimezoneId = "UTC",
                AcceptDownloads = false
            });
            context.SetDefaultTimeout(15_000);
            context.SetDefaultNavigationTimeout(30_000);
            IPage page = await context.NewPageAsync();
            return new(playwright, browser, context, page);
        }
        catch
        {
            try
            {
                if (browser is not null)
                    await browser.CloseAsync();
            }
            finally
            {
                playwright.Dispose();
            }
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Context.CloseAsync();
        }
        finally
        {
            try
            {
                await browser.CloseAsync();
            }
            finally
            {
                playwright.Dispose();
            }
        }
    }
}
