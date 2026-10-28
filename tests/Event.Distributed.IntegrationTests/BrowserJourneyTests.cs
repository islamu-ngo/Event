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
            var cookies = (await browser.Context.CookiesAsync()).Where(cookie =>
                cookie.HttpOnly && cookie.Name.StartsWith(".AspNetCore.Cookies", StringComparison.Ordinal)).ToArray();
            await Assert.That(cookies.Length > 0).IsTrue();

            using HttpClient api = await AuthenticatedApiAsync(host, cancellationToken);
            string slug = await SelectActiveTenantAsync(host, api, cancellationToken);
            DateTimeOffset start = DateTimeOffset.UtcNow.AddDays(7);
            using JsonDocument created = await PostAsync(api, "/api/Event", new
            {
                title = "Browser session assurance event",
                description = "Owned peer circuit assurance.",
                eventFormatId = 2,
                eventTimeZoneId = "UTC",
                participationConfiguration = new { participationHandlingModeId = 4, advanceRegistrationObligationId = 3, identityAccessModeId = 1 },
                sessions = new[] { new { tempKey = "session", title = "Browser session assurance", startTime = start, endTime = start.AddHours(1), endTimeType = 0 } }
            }, cancellationToken);
            Guid eventId = created.RootElement.GetProperty("id").GetGuid();
            using JsonDocument details = await ReadAsync(api, $"/api/Event/{eventId:D}/management-detail", cancellationToken);
            using JsonDocument published = await PostAsync(api, $"/api/Event/{eventId:D}/publish", new
            {
                expectedConcurrencyStamp = details.RootElement.GetProperty("concurrencyStamp").GetGuid()
            }, cancellationToken);
            using JsonDocument management = await ReadAsync(api, $"/api/event/{eventId:D}/resources/management", cancellationToken);
            await Assert.That(management.RootElement.GetProperty("_links").TryGetProperty("export", out JsonElement export)).IsTrue();
            string exportPath = $"/api/event/{eventId:D}/resources/export";
            await Assert.That(string.Equals(export.GetProperty("href").GetString(), exportPath, StringComparison.Ordinal)).IsTrue();

            using HttpClient peerEndpoint = host.CreateHttpClient("explore-blazor-peer");
            await using var peer = await OwnedBrowserContext.CreateAsync(peerEndpoint.BaseAddress!);
            await Assert.That((await peer.Context.CookiesAsync()).Count == 0).IsTrue();
            await peer.Context.AddCookiesAsync(cookies.Select(cookie => new Microsoft.Playwright.Cookie
            {
                Name = cookie.Name,
                Value = cookie.Value,
                Domain = cookie.Domain,
                Path = cookie.Path,
                HttpOnly = cookie.HttpOnly,
                Secure = cookie.Secure,
                SameSite = cookie.SameSite,
                Expires = cookie.Expires
            }));
            await AssertAuthenticatedAsync(peer, true);
            IAPIResponse before = await peer.Context.APIRequest.GetAsync($"/{slug}{exportPath}");
            try { await Assert.That(before.Status).IsEqualTo(200); }
            finally { await before.DisposeAsync(); }
            string resourcesPath = $"/{slug}/studio/events/{eventId:D}/resources";
            var connected = new TaskCompletionSource<IWebSocket>(TaskCreationOptions.RunContinuationsAsynchronously);
            peer.Page.WebSocket += (_, socket) =>
            {
                if (Uri.TryCreate(socket.Url, UriKind.Absolute, out Uri? address)
                    && (address.AbsolutePath == "/_blazor" || address.AbsolutePath == $"/{slug}/_blazor"))
                    connected.TrySetResult(socket);
            };
            await peer.Page.GotoAsync(resourcesPath);
            IWebSocket connection = await connected.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            ILocator exportButton = peer.Page.GetByRole(AriaRole.Button, new() { Name = "Export metadata", Exact = true });
            await Expect(exportButton).ToBeVisibleAsync();
            await peer.Page.GetByRole(AriaRole.Button, new() { Name = "New resource", Exact = true }).ClickAsync();
            await Expect(peer.Page.GetByLabel("Title", new() { Exact = true })).ToBeVisibleAsync();
            await peer.Page.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
            await Expect(peer.Page.GetByLabel("Title", new() { Exact = true })).ToHaveCountAsync(0);
            await Assert.That(connection.IsClosed).IsFalse();
            await Expect(peer.Page.GetByRole(AriaRole.Region, new() { Name = "Export metadata", Exact = true })).ToHaveCountAsync(0);
            bool peerDownloaded = false;
            peer.Page.Download += (_, _) => peerDownloaded = true;

            await browser.Page.GotoAsync("/logout?returnUrl=/login");
            await Expect(browser.Page.GetByRole(AriaRole.Heading, new() { Name = "Sign in", Exact = true }))
                .ToBeVisibleAsync();
            await AssertAuthenticatedAsync(browser, false);
            await browser.Page.GotoAsync("/events/create");
            await Expect(browser.Page.GetByLabel("Username or email", new() { Exact = true })).ToBeVisibleAsync();
            await Expect(browser.Page.GetByRole(AriaRole.Heading, new() { Name = "Create Event", Exact = true }))
                .ToHaveCountAsync(0);

            await exportButton.ClickAsync();
            await Expect(peer.Page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true })).ToBeVisibleAsync();
            string peerPath = new Uri(peer.Page.Url).AbsolutePath;
            await Assert.That(peerPath == resourcesPath || peerPath == "/login" || peerPath == $"/{slug}/login").IsTrue();
            await AssertAuthenticatedAsync(peer, false);
            await Expect(peer.Page.GetByRole(AriaRole.Region, new() { Name = "Export metadata", Exact = true })).ToHaveCountAsync(0);
            await Expect(peer.Page.GetByText("Authorized metadata export loaded.", new() { Exact = true })).ToHaveCountAsync(0);
            await Assert.That(peerDownloaded).IsFalse();

            using HttpClient endpoint = host.CreateHttpClient("explore-blazor");
            await using var replay = await OwnedBrowserContext.CreateAsync(endpoint.BaseAddress!);
            await Assert.That((await replay.Context.CookiesAsync()).Count == 0).IsTrue();
            await AssertAuthenticatedAsync(replay, false);
            await replay.Context.AddCookiesAsync(cookies.Select(cookie => new Microsoft.Playwright.Cookie
            {
                Name = cookie.Name,
                Value = cookie.Value,
                Domain = cookie.Domain,
                Path = cookie.Path,
                HttpOnly = cookie.HttpOnly,
                Secure = cookie.Secure,
                SameSite = cookie.SameSite,
                Expires = cookie.Expires
            }));
            await AssertAuthenticatedAsync(replay, false);
            await replay.Page.GotoAsync("/events/create");
            await Expect(replay.Page.GetByRole(AriaRole.Heading, new() { Name = "Sign in", Exact = true })).ToBeVisibleAsync();
            await Expect(replay.Page.GetByRole(AriaRole.Heading, new() { Name = "Create Event", Exact = true })).ToHaveCountAsync(0);
            using HttpClient controlEndpoint = host.CreateHttpClient("explore-blazor-peer");
            await using var control = await OwnedBrowserContext.CreateAsync(controlEndpoint.BaseAddress!);
            await Assert.That((await control.Context.CookiesAsync()).Count == 0).IsTrue();
            await AssertAuthenticatedAsync(control, false);
            await LoginAsync(host, control.Page);
            await AssertAuthenticatedAsync(control, true);
            IAPIResponse after = await control.Context.APIRequest.GetAsync($"/{slug}{exportPath}");
            try { await Assert.That(after.Status).IsEqualTo(200); }
            finally { await after.DisposeAsync(); }
        }, deploymentMode: "MultiTenant", profile: Explore.AppHost.OwnedAppHostProfile.ExternalIdentitySharedBff);

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
            await SelectActiveTenantAsync(host, api, cancellationToken);
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
        }, deploymentMode: "MultiTenant");

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

            ILocator language = page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Change language\\.", RegexOptions.None, TimeSpan.FromSeconds(1)) });
            await language.FocusAsync();
            await page.Keyboard.PressAsync("Enter");
            ILocator arabic = page.GetByRole(AriaRole.Menuitem, new() { NameRegex = new Regex("\\(AR\\)", RegexOptions.None, TimeSpan.FromSeconds(1)) });
            await arabic.FocusAsync();
            await page.Keyboard.PressAsync("Enter");
            await Expect(page.Locator("html")).ToHaveAttributeAsync("lang", "ar");
            await Expect(page.Locator("html")).ToHaveAttributeAsync("dir", "rtl");
            await page.ReloadAsync();
            await Expect(page.Locator("html")).ToHaveAttributeAsync("lang", "ar");
            await Expect(page.Locator("html")).ToHaveAttributeAsync("dir", "rtl");

            await language.FocusAsync();
            await page.Keyboard.PressAsync("Enter");
            ILocator english = page.GetByRole(AriaRole.Menuitem, new() { NameRegex = new Regex("\\(EN\\)", RegexOptions.None, TimeSpan.FromSeconds(1)) });
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
            string slug = await SelectActiveTenantAsync(host, api, cancellationToken);
            Guid eventId = await CreateFreeEventAsync(api, cancellationToken);
            await LoginAsync(host, browser.Page);
            await browser.Page.GotoAsync($"/{slug}/registration/events/{eventId:D}/tickets");
            await Expect(browser.Page.GetByRole(AriaRole.Heading, new() { Name = "Select tickets", Exact = true }))
                .ToBeVisibleAsync();
            await browser.Page.GetByLabel("Quantity", new() { Exact = true }).FillAsync("1");
            await browser.Page.GetByRole(AriaRole.Button, new() { Name = "Reserve selected tickets", Exact = true })
                .ClickAsync();
            await browser.Page.WaitForURLAsync(new Regex($"/registration/events/{eventId:D}/orders/[0-9a-f-]+", RegexOptions.None, TimeSpan.FromSeconds(1)));

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
        }, deploymentMode: "MultiTenant");

    [Test]
    public Task WorkspaceKeyboardValidationAndRtlLayoutUseRealFocusAndGeometry() => RunAsync(
        BrowserJourney.WorkspaceAccessibility, async (host, browser, cancellationToken) =>
        {
            using HttpClient api = await AuthenticatedApiAsync(host, cancellationToken);
            string slug = await SelectActiveTenantAsync(host, api, cancellationToken);
            Guid eventId = await CreateFreeEventAsync(api, cancellationToken);
            await LoginAsync(host, browser.Page);
            IPage page = browser.Page;
            await page.SetViewportSizeAsync(1440, 1000);
            await page.GotoAsync($"/{slug}/studio/events/{eventId:D}/resources");
            ILocator create = page.GetByRole(AriaRole.Button, new() { Name = "New resource", Exact = true });
            await Expect(create).ToBeVisibleAsync();
            await create.FocusAsync();
            await page.Keyboard.PressAsync("Enter");
            ILocator title = page.GetByLabel("Title", new() { Exact = true });
            await title.FillAsync("   ");
            await page.GetByLabel("Audience", new() { Exact = true }).SelectOptionAsync(new SelectOptionValue { Label = "Public" });
            await page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).FocusAsync();
            await page.Keyboard.PressAsync("Enter");
            await Expect(title).ToHaveAttributeAsync("aria-invalid", "true");
            await Expect(title).ToHaveAttributeAsync("aria-describedby", "resource-title-error");
            await Expect(page.Locator("#resource-title-error")).ToBeVisibleAsync();
            await Expect(page.Locator("#resource-title-error")).ToHaveAttributeAsync("role", "alert");
            await page.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).FocusAsync();
            await page.Keyboard.PressAsync("Enter");
            await Expect(title).ToHaveCountAsync(0);
            await Expect(create).ToBeFocusedAsync();

            await page.GotoAsync($"/{slug}/studio/events/{eventId:D}");
            await AssertDesktopDockAsync(page, rtl: false);
            await page.SetViewportSizeAsync(390, 844);
            await AssertMobileOverlayAsync(page, rtl: false);
            await ChangeLanguageAsync(page, "AR");
            await Expect(page.Locator("html")).ToHaveAttributeAsync("lang", "ar");
            await Expect(page.Locator("html")).ToHaveAttributeAsync("dir", "rtl");
            await AssertMobileOverlayAsync(page, rtl: true);
            await page.SetViewportSizeAsync(1440, 1000);
            await page.ReloadAsync();
            await AssertDesktopDockAsync(page, rtl: true);
        }, deploymentMode: "MultiTenant");

    [Test]
    public Task SeparatelyProvisionedLocalUserCannotExposeResourceManagementAffordances() => RunAsync(
        BrowserJourney.WorkspaceAuthority, async (host, browser, cancellationToken) =>
        {
            using HttpClient admin = await AuthenticatedApiAsync(host, cancellationToken);
            string slug = await SelectActiveTenantAsync(host, admin, cancellationToken);
            Guid eventId = await CreateFreeEventAsync(admin, cancellationToken);
            string identifier = "assurance-reader-" + host.RunId + "@example.invalid";
            using JsonDocument issued = await PostAsync(admin, "/api/instance/local-identities", new
            {
                operationId = Guid.CreateVersion7(),
                email = identifier,
                firstName = "Assurance",
                lastName = "Reader"
            }, cancellationToken);
            string temporaryPassword = issued.RootElement.GetProperty("temporaryPassword").GetString()!;
            using HttpClient endpoint = host.CreateHttpClient("explore-blazor");
            await using var reader = await OwnedBrowserContext.CreateAsync(endpoint.BaseAddress!);
            await Assert.That((await reader.Context.CookiesAsync()).Count == 0).IsTrue();
            await ReplaceCredentialAsync(identifier, temporaryPassword, host.BootstrapReplacementPassword, reader);
            await reader.Page.GetByLabel("Username or email", new() { Exact = true }).FillAsync(identifier);
            await reader.Page.GetByLabel("Password", new() { Exact = true }).FillAsync(host.BootstrapReplacementPassword);
            await reader.Page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
            await reader.Page.WaitForURLAsync(url => new Uri(url).AbsolutePath != "/login");
            await AssertAuthenticatedAsync(reader, true);
            using HttpClient api = host.CreateHttpClient("explore-api");
            using JsonDocument login = await PostAsync(api, "/api/auth/local/login", new
            {
                identifier,
                password = host.BootstrapReplacementPassword
            }, cancellationToken);
            api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.RootElement.GetProperty("token").GetString());
            api.DefaultRequestHeaders.Add("X-Tenant-Slug", slug);
            using JsonDocument audience = await ReadAsync(api, $"/api/event/{eventId:D}/resources", cancellationToken);
            await Assert.That(audience.RootElement.GetProperty("_links").TryGetProperty("manage-resources", out _)).IsFalse();
            using HttpResponseMessage denied = await api.GetAsync($"/api/event/{eventId:D}/resources/management", cancellationToken);
            await Assert.That(denied.StatusCode == HttpStatusCode.Forbidden).IsTrue();
            await reader.Page.GotoAsync($"/{slug}/studio/events/{eventId:D}/resources");
            await Expect(reader.Page.GetByTestId("resources-route-unavailable")
                .Or(reader.Page.GetByRole(AriaRole.Heading, new() { Name = "Event unavailable", Exact = true }))).ToBeVisibleAsync();
            await Expect(reader.Page.GetByRole(AriaRole.Button, new() { Name = "New resource", Exact = true })).ToHaveCountAsync(0);
            await Expect(reader.Page.Locator("#resource-title")).ToHaveCountAsync(0);
            await AssertAuthenticatedAsync(reader, true);
        }, deploymentMode: "MultiTenant");

    private static async Task AssertMobileOverlayAsync(IPage page, bool rtl)
    {
        await Expect(page.GetByRole(AriaRole.Complementary, new() { Name = "Sidebar navigation", Exact = true })).ToHaveCountAsync(0);
        ILocator dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Sidebar navigation", Exact = true });
        await Expect(dialog).ToHaveCountAsync(0);
        ILocator toggle = page.GetByRole(AriaRole.Button, new() { Name = "Toggle sidebar navigation", Exact = true });
        await toggle.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Expect(dialog).ToBeVisibleAsync();
        await Expect(dialog).ToHaveAttributeAsync("aria-modal", "true");
        await Expect(dialog).ToHaveAttributeAsync("data-dock-panel-id", "shell.workspace-nav");
        await Assert.That(await dialog.EvaluateAsync<bool>("element => element === document.activeElement || element.contains(document.activeElement)")).IsTrue();
        int count = await dialog.Locator("a[href]:visible,button:enabled:visible,[tabindex='0']:visible").CountAsync();
        await Assert.That(count > 1).IsTrue();
        foreach (string key in new[] { "Tab", "Shift+Tab" })
            for (int index = 0; index < count + 2; index++)
            {
                await page.Keyboard.PressAsync(key);
                await Assert.That(await dialog.EvaluateAsync<bool>("element => element.contains(document.activeElement)")).IsTrue();
            }
        var bounds = await dialog.BoundingBoxAsync();
        await Assert.That(bounds is not null && bounds.X >= -1 && bounds.X + bounds.Width <= 391
            && (rtl ? bounds.X + bounds.Width >= 389 : bounds.X <= 1)).IsTrue();
        ILocator navigation = dialog.GetByRole(AriaRole.Navigation, new() { Name = "Event management navigation", Exact = true });
        await Expect(navigation).ToBeVisibleAsync();
        var links = await navigation.GetByRole(AriaRole.Link).AllAsync();
        float previousBottom = 0;
        foreach (ILocator link in links)
        {
            if (!await link.IsVisibleAsync()) continue;
            var item = await link.BoundingBoxAsync();
            await Assert.That(item is not null && item.Width > 0 && item.X >= bounds!.X - 1
                && item.X + item.Width <= bounds.X + bounds.Width + 1 && item.Y >= previousBottom - 1
                && !string.IsNullOrWhiteSpace(await link.InnerTextAsync())).IsTrue();
            previousBottom = item!.Y + item.Height;
        }
        await page.Keyboard.PressAsync("Escape");
        await Expect(dialog).ToHaveCountAsync(0);
        await Expect(toggle).ToBeFocusedAsync();
    }

    private static async Task AssertDesktopDockAsync(IPage page, bool rtl)
    {
        ILocator sidebar = page.GetByRole(AriaRole.Complementary, new() { Name = "Sidebar navigation", Exact = true });
        if (await sidebar.CountAsync() == 0)
        {
            await page.GetByRole(AriaRole.Button, new() { Name = "Toggle sidebar navigation", Exact = true }).FocusAsync();
            await page.Keyboard.PressAsync("Enter");
        }
        await Expect(sidebar).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Dialog, new() { Name = "Sidebar navigation", Exact = true })).ToHaveCountAsync(0);
        await Assert.That(await sidebar.GetAttributeAsync("aria-modal") is null or "false").IsTrue();
        var panel = await sidebar.BoundingBoxAsync();
        var main = await page.GetByRole(AriaRole.Main).BoundingBoxAsync();
        await Assert.That(panel is not null && main is not null && panel.Width > 0 && main.Width > 0
            && (rtl ? panel.X >= main.X + main.Width - 1 : panel.X + panel.Width <= main.X + 1)).IsTrue();
        ILocator focusable = sidebar.Locator("a[href]:visible,button:enabled:visible,[tabindex='0']:visible");
        int count = await focusable.CountAsync();
        await focusable.First.FocusAsync();
        bool escaped = false;
        for (int index = 0; index < count + 2; index++)
        {
            await page.Keyboard.PressAsync("Tab");
            if (!await sidebar.EvaluateAsync<bool>("element => element.contains(document.activeElement)"))
            {
                escaped = true;
                break;
            }
        }
        await Assert.That(escaped).IsTrue();
    }

    private static async Task ChangeLanguageAsync(IPage page, string code)
    {
        await page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Change language\\.", RegexOptions.None, TimeSpan.FromSeconds(1)) }).FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await page.GetByRole(AriaRole.Menuitem, new() { NameRegex = new Regex("\\(" + code + "\\)", RegexOptions.None, TimeSpan.FromSeconds(1)) }).FocusAsync();
        await page.Keyboard.PressAsync("Enter");
    }

    private static async Task RunAsync(BrowserJourney journey,
        Func<OwnedAppHostFixture, OwnedBrowserContext, CancellationToken, Task> scenario,
        string deploymentMode = "SingleTenant", Explore.AppHost.OwnedAppHostProfile profile = Explore.AppHost.OwnedAppHostProfile.Standard)
    {
        try
        {
            BrowserArtifactPolicy.AdmitDiagnostics(
                Environment.GetEnvironmentVariable("DEBUG"), Environment.GetEnvironmentVariable("PWDEBUG"));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(15));
            await using var host = await OwnedAppHostFixture.CreateAsync("Split", timeout.Token,
                configuredAdministrator: true, deploymentMode: deploymentMode, profile: profile);
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
        await page.WaitForURLAsync(new Regex("/events/create(?:[?#].*)?$", RegexOptions.None, TimeSpan.FromSeconds(1)));
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Create Event", Exact = true })).ToBeVisibleAsync();
    }

    internal static Task ReplaceBootstrapCredentialAsync(OwnedAppHostFixture host, OwnedBrowserContext browser) =>
        ReplaceCredentialAsync(host.BootstrapIdentifier, host.BootstrapPassword, host.BootstrapReplacementPassword, browser);

    private static async Task ReplaceCredentialAsync(string identifier, string temporaryPassword,
        string replacementPassword, OwnedBrowserContext browser)
    {
        IPage page = browser.Page;
        await page.GotoAsync("/login");
        await page.GetByLabel("Username or email", new() { Exact = true }).FillAsync(identifier);
        await page.GetByLabel("Password", new() { Exact = true }).FillAsync(temporaryPassword);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Change your password", Exact = true }))
            .ToBeVisibleAsync();
        await AssertAuthenticatedAsync(browser, false);
        await page.GetByLabel("New password", new() { Exact = true }).FillAsync(replacementPassword);
        await page.GetByLabel("Confirm new password", new() { Exact = true }).FillAsync(replacementPassword);
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

    private static async Task<string> SelectActiveTenantAsync(OwnedAppHostFixture host, HttpClient api, CancellationToken cancellationToken)
    {
        string slug = "assurance-" + host.RunId[..8];
        await CreateTenantAsync(api, slug, cancellationToken);
        api.DefaultRequestHeaders.Add("X-Tenant-Slug", slug);
        return slug;
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
