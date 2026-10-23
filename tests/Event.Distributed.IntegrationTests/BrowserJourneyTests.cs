using System.Net.Http.Headers;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Event.Distributed.IntegrationTests;

[Category("Browser")]
[NotInParallel]
public sealed class BrowserJourneyTests
{
    [Test]
    public Task LoginAndLogoutRemoveAccessToProtectedPages() => RunAsync(
        BrowserJourney.LoginLogout, async (host, browser, cancellationToken) =>
        {
            await LoginAsync(host, browser.Page);
            await AssertAuthenticatedAsync(browser, true);

            await browser.Page.GotoAsync("/logout?returnUrl=/login");
            await Expect(browser.Page.GetByRole(AriaRole.Heading, new() { Name = "Sign in", Exact = true }))
                .ToBeVisibleAsync();
            await AssertAuthenticatedAsync(browser, false);
            await browser.Page.GotoAsync("/events/create");
            await Expect(browser.Page.GetByLabel("Username or email", new() { Exact = true })).ToBeVisibleAsync();
            await Expect(browser.Page.GetByRole(AriaRole.Heading, new() { Name = "Create Event", Exact = true }))
                .ToHaveCountAsync(0);
        });

    [Test]
    public Task ExpiredBrowserCookieRequiresFreshCredentials() => RunAsync(
        BrowserJourney.CookieExpiry, async (host, browser, cancellationToken) =>
        {
            await LoginAsync(host, browser.Page);
            await AssertAuthenticatedAsync(browser, true);
            var cookies = (await browser.Context.CookiesAsync()).Where(cookie =>
                cookie.HttpOnly && cookie.Name.StartsWith(".AspNetCore.Cookies", StringComparison.Ordinal)).ToArray();
            await Assert.That(cookies.Length > 0).IsTrue();
            await browser.Context.AddCookiesAsync(cookies.Select(cookie => new Microsoft.Playwright.Cookie
            {
                Name = cookie.Name,
                Value = cookie.Value,
                Domain = cookie.Domain,
                Path = cookie.Path,
                HttpOnly = cookie.HttpOnly,
                Secure = cookie.Secure,
                SameSite = cookie.SameSite,
                Expires = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds()
            }));

            await browser.Page.GotoAsync("/events/create");
            await Expect(browser.Page.GetByLabel("Username or email", new() { Exact = true })).ToBeVisibleAsync();
            await AssertAuthenticatedAsync(browser, false);
            await LoginAsync(host, browser.Page);
            await AssertAuthenticatedAsync(browser, true);
        });

    [Test]
    public Task ServerTokenExpiryRejectsPreviouslyAuthorizedApiAndBrowserSession() => RunAsync(
        BrowserJourney.ServerTokenExpiry, async (host, browser, cancellationToken) =>
        {
            await LoginAsync(host, browser.Page);
            await AssertAuthenticatedAsync(browser, true);
            using HttpClient api = await AuthenticatedApiAsync(host, cancellationToken);
            Guid eventId = await CreateFreeEventAsync(api, cancellationToken);
            string protectedPath = $"/api/events/{eventId:D}/ticketing";
            using (HttpResponseMessage initial = await api.GetAsync(protectedPath, cancellationToken))
                await Assert.That(initial.StatusCode).IsEqualTo(HttpStatusCode.OK);

            using var expiryDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            expiryDeadline.CancelAfter(TimeSpan.FromMinutes(6));
            using var observations = new PeriodicTimer(TimeSpan.FromSeconds(5));
            while (await observations.WaitForNextTickAsync(expiryDeadline.Token))
            {
                using HttpResponseMessage response = await api.GetAsync(protectedPath, expiryDeadline.Token);
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                    break;
                await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            }

            await AssertAuthenticatedAsync(browser, false);
            await browser.Page.GotoAsync("/events/create");
            await Expect(browser.Page.GetByLabel("Username or email", new() { Exact = true })).ToBeVisibleAsync();
        });

    [Test]
    public Task KeyboardLoginAndLanguageSwitchPreserveFocusAndDirection() => RunAsync(
        BrowserJourney.KeyboardLanguage, async (host, browser, cancellationToken) =>
        {
            IPage page = browser.Page;
            await page.GotoAsync("/login");
            ILocator identifier = page.GetByLabel("Username or email", new() { Exact = true });
            await Expect(identifier).ToBeVisibleAsync();
            await identifier.FocusAsync();
            await identifier.FillAsync(host.BootstrapIdentifier);
            await page.Keyboard.PressAsync("Tab");
            ILocator password = page.GetByLabel("Password", new() { Exact = true });
            await Expect(password).ToBeFocusedAsync();
            await password.FillAsync(host.BootstrapReplacementPassword);
            await password.PressAsync("Enter");
            await page.WaitForURLAsync(url => !new Uri(url).AbsolutePath.Equals("/login", StringComparison.Ordinal));
            await AssertAuthenticatedAsync(browser, true);

            ILocator language = page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Change language\\.") });
            await language.FocusAsync();
            await page.Keyboard.PressAsync("Enter");
            ILocator arabic = page.GetByRole(AriaRole.Menuitem, new() { NameRegex = new Regex("\\(AR\\)") });
            await arabic.FocusAsync();
            await page.Keyboard.PressAsync("Enter");
            await Expect(page.Locator("html")).ToHaveAttributeAsync("lang", "ar");
            await Expect(page.Locator("html")).ToHaveAttributeAsync("dir", "rtl");
            await page.ReloadAsync();
            await Expect(page.Locator("html")).ToHaveAttributeAsync("lang", "ar");
            await Expect(page.Locator("html")).ToHaveAttributeAsync("dir", "rtl");

            await language.FocusAsync();
            await page.Keyboard.PressAsync("Enter");
            ILocator english = page.GetByRole(AriaRole.Menuitem, new() { NameRegex = new Regex("\\(EN\\)") });
            await english.FocusAsync();
            await page.Keyboard.PressAsync("Enter");
            await Expect(page.Locator("html")).ToHaveAttributeAsync("dir", "ltr");
            await Expect(page.Locator("html")).ToHaveAttributeAsync("lang", "en");
            ILocator skip = page.GetByRole(AriaRole.Link, new() { Name = "Skip to main content", Exact = true });
            await skip.FocusAsync();
            await page.Keyboard.PressAsync("Enter");
            await Expect(page.GetByRole(AriaRole.Main)).ToBeFocusedAsync();
        });

    [Test]
    public Task TenantPathWinsOverForgedBrowserTenantHeader() => RunAsync(
        BrowserJourney.TenantBoundary, async (host, browser, cancellationToken) =>
        {
            using HttpClient api = await AuthenticatedApiAsync(host, cancellationToken);
            string first = "assurance-a-" + host.RunId[..8];
            string second = "assurance-b-" + host.RunId[..8];
            await CreateTenantAsync(api, first, cancellationToken);
            await CreateTenantAsync(api, second, cancellationToken);
            api.DefaultRequestHeaders.Add("X-Tenant-Slug", first);
            Guid eventId = await CreateFreeEventAsync(api, cancellationToken);

            await browser.Page.GotoAsync($"/{first}/registration/events/{eventId:D}/tickets");
            await Expect(browser.Page.GetByRole(AriaRole.Heading, new() { Name = "Free admission", Exact = true }))
                .ToBeVisibleAsync();
            await browser.Context.SetExtraHTTPHeadersAsync(new Dictionary<string, string> { ["X-Tenant-Slug"] = first });
            await browser.Page.GotoAsync($"/{second}/registration/events/{eventId:D}/tickets");
            await Expect(browser.Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Tickets are not available for this event.");
            await Expect(browser.Page.GetByRole(AriaRole.Heading, new() { Name = "Free admission", Exact = true }))
                .ToHaveCountAsync(0);
            await Expect(browser.Page.GetByRole(AriaRole.Button, new() { Name = "Reserve selected tickets", Exact = true }))
                .ToHaveCountAsync(0);
        }, deploymentMode: "MultiTenant");

    [Test]
    public Task FreeRegistrationConfirmsAndRemovesFinalizeAffordance() => RunAsync(
        BrowserJourney.FreeRegistration, async (host, browser, cancellationToken) =>
        {
            using HttpClient api = await AuthenticatedApiAsync(host, cancellationToken);
            Guid eventId = await CreateFreeEventAsync(api, cancellationToken);
            await LoginAsync(host, browser.Page);
            await browser.Page.GotoAsync($"/registration/events/{eventId:D}/tickets");
            await Expect(browser.Page.GetByRole(AriaRole.Heading, new() { Name = "Select tickets", Exact = true }))
                .ToBeVisibleAsync();
            await browser.Page.GetByLabel("Quantity", new() { Exact = true }).FillAsync("1");
            await browser.Page.GetByRole(AriaRole.Button, new() { Name = "Reserve selected tickets", Exact = true })
                .ClickAsync();
            await browser.Page.WaitForURLAsync(new Regex($"/registration/events/{eventId:D}/orders/[0-9a-f-]+"));

            Guid orderId = Guid.Parse(new Uri(browser.Page.Url).Segments[^1]);
            using JsonDocument reserved = await ReadAsync(api, $"/api/events/{eventId:D}/registration-orders/{orderId:D}", cancellationToken);
            await Assert.That(reserved.RootElement.GetProperty("totalDueMinor").GetInt64()).IsEqualTo(0L);
            await Assert.That(reserved.RootElement.GetProperty("_links").TryGetProperty("finalize", out _)).IsTrue();
            await browser.Page.GetByRole(AriaRole.Button, new() { Name = "Finalize registration", Exact = true }).ClickAsync();
            await Expect(browser.Page.GetByText("Your registration is confirmed.", new() { Exact = true })).ToBeVisibleAsync();
            await Expect(browser.Page.GetByRole(AriaRole.Button, new() { Name = "Finalize registration", Exact = true }))
                .ToHaveCountAsync(0);
            await browser.Page.ReloadAsync();
            await Expect(browser.Page.GetByText("Your registration is confirmed.", new() { Exact = true })).ToBeVisibleAsync();
            using JsonDocument confirmed = await ReadAsync(api, $"/api/events/{eventId:D}/registration-orders/{orderId:D}", cancellationToken);
            await Assert.That(confirmed.RootElement.GetProperty("statusCode").GetString()).IsEqualTo("CONFIRMED");
            await Assert.That(confirmed.RootElement.GetProperty("_links").TryGetProperty("finalize", out _)).IsFalse();
        });

    private static async Task RunAsync(BrowserJourney journey,
        Func<OwnedAppHostFixture, OwnedBrowserContext, CancellationToken, Task> scenario,
        string deploymentMode = "SingleTenant")
    {
        try
        {
            BrowserArtifactPolicy.AdmitDiagnostics(
                Environment.GetEnvironmentVariable("DEBUG"), Environment.GetEnvironmentVariable("PWDEBUG"));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(15));
            await using var host = await OwnedAppHostFixture.CreateAsync("Split", timeout.Token,
                configuredAdministrator: true, deploymentMode: deploymentMode);
            await host.StartAsync(timeout.Token);
            using HttpClient endpoint = host.CreateHttpClient("explore-blazor");
            await using var browser = await OwnedBrowserContext.CreateAsync(endpoint.BaseAddress!);
            await Assert.That((await browser.Context.CookiesAsync()).Count).IsEqualTo(0);
            await ReplaceBootstrapCredentialAsync(host, browser);
            await scenario(host, browser, timeout.Token);
        }
        catch (Exception failure)
        {
            throw BrowserArtifactPolicy.Sanitize(journey, failure);
        }
    }

    private static async Task LoginAsync(OwnedAppHostFixture host, IPage page)
    {
        await page.GotoAsync("/login?returnUrl=/events/create");
        await page.GetByLabel("Username or email", new() { Exact = true }).FillAsync(host.BootstrapIdentifier);
        await page.GetByLabel("Password", new() { Exact = true }).FillAsync(host.BootstrapReplacementPassword);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
        await page.WaitForURLAsync(new Regex("/events/create(?:[?#].*)?$"));
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Create Event", Exact = true })).ToBeVisibleAsync();
    }

    internal static async Task ReplaceBootstrapCredentialAsync(OwnedAppHostFixture host, OwnedBrowserContext browser)
    {
        IPage page = browser.Page;
        await page.GotoAsync("/login");
        await page.GetByLabel("Username or email", new() { Exact = true }).FillAsync(host.BootstrapIdentifier);
        await page.GetByLabel("Password", new() { Exact = true }).FillAsync(host.BootstrapPassword);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Change your password", Exact = true }))
            .ToBeVisibleAsync();
        await AssertAuthenticatedAsync(browser, false);
        await page.GetByLabel("New password", new() { Exact = true }).FillAsync(host.BootstrapReplacementPassword);
        await page.GetByLabel("Confirm new password", new() { Exact = true }).FillAsync(host.BootstrapReplacementPassword);
        await page.GetByRole(AriaRole.Button, new() { Name = "Change password", Exact = true }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Sign in", Exact = true })).ToBeVisibleAsync();
        await AssertAuthenticatedAsync(browser, false);
    }

    private static async Task AssertAuthenticatedAsync(OwnedBrowserContext browser, bool expected)
    {
        IAPIResponse response = await browser.Context.APIRequest.GetAsync("/auth/status");
        try
        {
            await Assert.That(response.Status).IsEqualTo(200);
            using JsonDocument status = JsonDocument.Parse(await response.BodyAsync());
            await Assert.That(status.RootElement.GetProperty("isAuthenticated").GetBoolean()).IsEqualTo(expected);
        }
        finally
        {
            await response.DisposeAsync();
        }
    }

    internal static async Task<HttpClient> AuthenticatedApiAsync(OwnedAppHostFixture host, CancellationToken cancellationToken)
    {
        HttpClient api = host.CreateHttpClient("explore-api");
        try
        {
            using JsonDocument login = await PostAsync(api, "/api/auth/local/login", new
            {
                identifier = host.BootstrapIdentifier,
                password = host.BootstrapReplacementPassword
            }, cancellationToken);
            api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.RootElement.GetProperty("token").GetString());
            return api;
        }
        catch
        {
            api.Dispose();
            throw;
        }
    }

    private static async Task<Guid> CreateFreeEventAsync(HttpClient api, CancellationToken cancellationToken)
    {
        DateTimeOffset start = DateTimeOffset.UtcNow.AddDays(7);
        using JsonDocument created = await PostAsync(api, "/api/Event", new
        {
            title = "Browser assurance free event",
            description = "Owned browser registration assurance.",
            eventFormatId = 2,
            eventTimeZoneId = "UTC",
            participationConfiguration = new { participationHandlingModeId = 4, advanceRegistrationObligationId = 3, identityAccessModeId = 1 },
            sessions = new[] { new { tempKey = "session", title = "Browser assurance session", startTime = start, endTime = start.AddHours(1), endTimeType = 0 } }
        }, cancellationToken);
        Guid eventId = created.RootElement.GetProperty("id").GetGuid();
        using JsonDocument draft = await PostAsync(api, $"/api/events/{eventId:D}/ticketing/draft", new { currencyCode = "EUR" }, cancellationToken);
        using JsonDocument ticket = await PostAsync(api, $"/api/events/{eventId:D}/ticketing/ticket-types", new
        {
            name = "Free admission",
            ticketPricingModeId = 2,
            participantDataCollectionModeId = 1,
            perOrderLimit = 1,
            entitlements = new[] { new { entitlementScopeTypeId = 1, includedQuantity = 1, entitlementSelectionRuleId = 1 } }
        }, cancellationToken);
        using JsonDocument catalog = await PostAsync(api, $"/api/events/{eventId:D}/ticketing/publish", new { }, cancellationToken);
        using JsonDocument details = await ReadAsync(api, $"/api/Event/{eventId:D}/management-detail", cancellationToken);
        using JsonDocument published = await PostAsync(api, $"/api/Event/{eventId:D}/publish", new
        {
            expectedConcurrencyStamp = details.RootElement.GetProperty("concurrencyStamp").GetGuid()
        }, cancellationToken);
        return eventId;
    }

    internal static async Task CreateTenantAsync(HttpClient api, string slug, CancellationToken cancellationToken)
    {
        using JsonDocument tenant = await PostAsync(api, "/api/Tenant", new
        {
            fullName = "Browser assurance community",
            slug,
            isActive = true,
            assignCurrentUserAsTenantAdmin = true,
            directoryOperatorIdentity = new
            {
                publicName = "Browser assurance community",
                legalName = "Browser assurance community",
                operatorKindCode = "registered_organization",
                jurisdictionCountryCode = "BE",
                registrationIdentifier = "assurance-" + slug,
                publicContactEmail = "assurance@example.invalid",
                legalNoticeUrl = "https://example.invalid/legal",
                termsUrl = "https://example.invalid/terms",
                privacyUrl = "https://example.invalid/privacy"
            }
        }, cancellationToken);
    }

    private static async Task<JsonDocument> PostAsync(HttpClient api, string path, object body, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await api.PostAsJsonAsync(path, body, cancellationToken);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
    }

    private static async Task<JsonDocument> ReadAsync(HttpClient api, string path, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await api.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
    }
}
