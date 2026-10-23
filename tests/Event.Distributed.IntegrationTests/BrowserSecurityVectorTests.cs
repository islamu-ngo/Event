using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Playwright;

namespace Event.Distributed.IntegrationTests;

[Category("Browser")]
[NotInParallel]
public sealed class BrowserSecurityVectorTests
{
    private const string ModuleOrigin = "http://127.0.0.1:42719";
    private static readonly string EventId = Guid.CreateVersion7().ToString("D");
    private static readonly string OrderId = Guid.CreateVersion7().ToString("D");
    private static string GuestPath => $"/registration/guest/events/{EventId}/orders/{OrderId}/status";

    [Test]
    public Task RecoveryPointerIsDecodedOnceScrubbedAndConsumedWithoutRetry() => RunModuleAsync(
        BrowserJourney.LocalRecovery, async browser =>
        {
            Dictionary<string, object> pointer = RecoveryPointer();
            string fragment = EncodePointer(pointer);
            int mutations = 0;
            string csrfCanary = GuestToken();
            bool cleanBeforeNetwork = true;
            bool protectedMutation = false;
            await browser.Context.RouteAsync("**/auth/status", async route =>
            {
                cleanBeforeNetwork &= new Uri(browser.Page.Url).Fragment.Length == 0;
                await route.FulfillAsync(new()
                {
                    Status = 200,
                    Headers = new Dictionary<string, string> { ["Set-Cookie"] = "XSRF-TOKEN=" + csrfCanary + "; Path=/; SameSite=Strict" },
                    Body = "{}"
                });
            });
            await browser.Context.RouteAsync("**/bff/auth/local/password-recoveries/consume", async route =>
            {
                mutations++;
                cleanBeforeNetwork &= new Uri(browser.Page.Url).Fragment.Length == 0;
                protectedMutation = route.Request.Headers.TryGetValue("x-csrf-token", out string? csrf) && csrf == csrfCanary;
                await route.FulfillAsync(new() { Status = 409, Body = "not-json-private-error" });
            });
            await browser.Page.GotoAsync("/recovery?keep=public#" + fragment);
            using JsonDocument result = JsonDocument.Parse(await browser.Page.EvaluateAsync<string>("""
                async () => {
                    const module = await import('/js/local-account-recovery.js');
                    const pointer = module.takeLocalAccountCapability();
                    const again = module.takeLocalAccountCapability();
                    let bodyRead = false;
                    for (const method of ['text', 'json', 'arrayBuffer', 'blob', 'formData', 'bytes']) {
                        if (typeof Response.prototype[method] === 'function') {
                            const original = Response.prototype[method];
                            Response.prototype[method] = function (...args) {
                                if (this.status !== 409) return original.apply(this, args);
                                bodyRead = true;
                                throw new Error('body-not-admitted');
                            };
                        }
                    }
                    const status = await module.submitLocalAccountRequest('/bff/auth/local/password-recoveries/consume', pointer);
                    return JSON.stringify({ pointer, again, status, bodyRead, clean: !location.hash && location.search === '?keep=public' });
                }
                """));
            JsonElement value = result.RootElement;
            await Assert.That(value.GetProperty("pointer").EnumerateObject().Count() == 7).IsTrue();
            foreach ((string key, object expected) in pointer)
                await Assert.That(value.GetProperty("pointer").GetProperty(key).ToString() == expected.ToString()).IsTrue();
            await Assert.That(value.GetProperty("again").ValueKind == JsonValueKind.Null).IsTrue();
            await Assert.That(value.GetProperty("clean").GetBoolean() && cleanBeforeNetwork && protectedMutation).IsTrue();
            await Assert.That(value.GetProperty("status").GetInt32() == 409 && mutations == 1).IsTrue();
            await Assert.That(value.GetProperty("bodyRead").GetBoolean()).IsFalse();
        });

    [Test]
    [Arguments("missing")]
    [Arguments("duplicate")]
    [Arguments("malformed")]
    [Arguments("purpose")]
    [Arguments("empty-token")]
    [Arguments("query")]
    [Arguments("query-and-fragment")]
    public Task InvalidRecoveryPointersAreErasedAndDenied(string variant) => RunModuleAsync(
        BrowserJourney.LocalRecovery, async browser =>
        {
            Dictionary<string, object> pointer = RecoveryPointer();
            string valid = EncodePointer(pointer);
            string input = variant switch
            {
                "missing" => "#" + valid[(valid.IndexOf('&') + 1)..],
                "duplicate" => "#" + valid + "&generation=" + Guid.CreateVersion7().ToString("D"),
                "malformed" => "#" + valid.Replace("operationId=", "operationId=%ZZ", StringComparison.Ordinal),
                "purpose" => "#" + valid.Replace("purpose=2", "purpose=4", StringComparison.Ordinal),
                "empty-token" => "#" + valid[..valid.IndexOf("token=", StringComparison.Ordinal)] + "token=",
                "query" => "?" + valid,
                "query-and-fragment" => "?token=query#" + valid,
                _ => throw new InvalidOperationException("unknown_vector")
            };
            await browser.Page.GotoAsync("/recovery" + input);
            using JsonDocument result = JsonDocument.Parse(await browser.Page.EvaluateAsync<string>("""
                async () => {
                    const module = await import('/js/local-account-recovery.js');
                    const pointer = module.takeLocalAccountCapability();
                    return JSON.stringify({ denied: pointer !== null && Object.keys(pointer).length === 0, clean: !location.hash && !location.search,
                        consumed: module.takeLocalAccountCapability() === null });
                }
                """));
            await Assert.That(result.RootElement.EnumerateObject().All(property => property.Value.GetBoolean())).IsTrue();
        });

    [Test]
    public Task RecoveryMutationRejectsPrivateFragmentsAndForeignEndpoints() => RunModuleAsync(
        BrowserJourney.LocalRecovery, async browser =>
        {
            int requests = 0;
            await browser.Context.RouteAsync("**/bff/**", route => { requests++; return route.AbortAsync(); });
            await browser.Page.GotoAsync("/recovery#private");
            int fragment = await browser.Page.EvaluateAsync<int>("""
                async () => (await import('/js/local-account-recovery.js'))
                    .submitLocalAccountRequest('/bff/auth/local/password', {})
                """);
            await browser.Page.GotoAsync("/recovery");
            bool foreign = await browser.Page.EvaluateAsync<bool>("""
                async () => {
                    const module = await import('/js/local-account-recovery.js');
                    const statuses = await Promise.all([
                        'https://example.invalid/bff/auth/local/password', '/tenant/bff/auth/local/password', '/bff/auth/local/unknown'
                    ].map(path => module.submitLocalAccountRequest(path, {})));
                    return statuses.every(status => status === 400);
                }
                """);
            await Assert.That(fragment == 400 && foreign && requests == 0).IsTrue();
        });

    [Test]
    [Arguments(16)]
    [Arguments(17)]
    public Task DedicatedWorkerProofPassesIndependentSha256Oracle(int difficulty) => RunModuleAsync(
        BrowserJourney.ChallengeProof, async browser =>
        {
            const string envelope = "assurance-proof-oracle";
            await browser.Page.GotoAsync("/proof");
            using JsonDocument proof = JsonDocument.Parse(await browser.Page.EvaluateAsync<string>("""
                async challenge => {
                    const solver = (await import('/js/anonymous-registration-challenge-interop.js')).createSolver();
                    const progress = [];
                    const nonce = await solver.solve(challenge, { invokeMethodAsync: async (name, attempts) => {
                        progress.push({ name, attempts });
                    }});
                    return JSON.stringify({ nonce, progress });
                }
                """, Challenge(envelope, difficulty)));
            string nonce = proof.RootElement.GetProperty("nonce").GetString()!;
            await Assert.That(IsProof(envelope, nonce, difficulty)).IsTrue();
            await Assert.That(IsProof("oracle-negative", "0000000000000000", difficulty)).IsFalse();
            await Assert.That(IsProof(envelope + "-changed", nonce, difficulty)).IsFalse();
            JsonElement[] progress = proof.RootElement.GetProperty("progress").EnumerateArray().ToArray();
            await Assert.That(progress.Length > 0 && progress[0].GetProperty("attempts").GetInt64() == 0).IsTrue();
            await Assert.That(progress.All(item => item.GetProperty("name").GetString() == "ReportProgress"
                && item.GetProperty("attempts").TryGetInt64(out long attempts) && attempts >= 0)).IsTrue();
        });

    [Test]
    public Task WorkerBusyCancellationAndReuseHaveIndependentLifetimes() => RunModuleAsync(
        BrowserJourney.ChallengeProof, async browser =>
        {
            await browser.Page.GotoAsync("/proof");
            string envelope = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            using JsonDocument result = JsonDocument.Parse(await browser.Page.EvaluateAsync<string>("""
                async challenge => {
                    const solver = (await import('/js/anonymous-registration-challenge-interop.js')).createSolver();
                    let observeStart;
                    const started = new Promise(resolve => { observeStart = resolve; });
                    const first = solver.solve({ ...challenge, difficulty: 22 }, { invokeMethodAsync: async (name, attempts) => {
                        observeStart(name === 'ReportProgress' && attempts === 0);
                        solver.cancel();
                    }}).then(() => 'unexpected-solution', failure => failure.message);
                    const busy = await solver.solve(challenge, { invokeMethodAsync: async () => {} })
                        .then(() => 'unexpected-solution', failure => failure.message);
                    const zeroBeforeCancel = await started;
                    const cancelled = await first;
                    const nonce = await solver.solve(challenge, { invokeMethodAsync: async () => {} });
                    solver.cancel();
                    return JSON.stringify({ busy, cancelled, zeroBeforeCancel, nonce });
                }
                """, Challenge(envelope, 16)));
            await Assert.That(result.RootElement.GetProperty("busy").GetString() == "challenge-busy").IsTrue();
            await Assert.That(result.RootElement.GetProperty("cancelled").GetString() == "challenge-cancelled").IsTrue();
            await Assert.That(result.RootElement.GetProperty("zeroBeforeCancel").GetBoolean()).IsTrue();
            await Assert.That(IsProof(envelope, result.RootElement.GetProperty("nonce").GetString()!, 16)).IsTrue();
        });

    [Test]
    [Arguments("version")]
    [Arguments("difficulty-low")]
    [Arguments("difficulty-high")]
    [Arguments("difficulty-fraction")]
    [Arguments("envelope-long")]
    [Arguments("envelope-newline")]
    [Arguments("expired")]
    [Arguments("expiry-invalid")]
    [Arguments("expiry-future")]
    public Task WorkerRefusesInvalidOrExpiredChallenges(string variant) => RunModuleAsync(
        BrowserJourney.ChallengeProof, async browser =>
        {
            Dictionary<string, object> challenge = Challenge(Convert.ToHexString(RandomNumberGenerator.GetBytes(24)), 16);
            switch (variant)
            {
                case "version": challenge["version"] = 2; break;
                case "difficulty-low": challenge["difficulty"] = 15; break;
                case "difficulty-high": challenge["difficulty"] = 23; break;
                case "difficulty-fraction": challenge["difficulty"] = 16.5; break;
                case "envelope-long": challenge["protectedChallenge"] = new string('a', 4097); break;
                case "envelope-newline": challenge["protectedChallenge"] = "invalid\nchallenge"; break;
                case "expired": challenge["expiresAt"] = DateTimeOffset.UtcNow.AddSeconds(-1).ToString("O"); break;
                case "expiry-invalid": challenge["expiresAt"] = "invalid"; break;
                case "expiry-future": challenge["expiresAt"] = DateTimeOffset.UtcNow.AddSeconds(240).ToString("O"); break;
                default: throw new InvalidOperationException("unknown_vector");
            }
            await browser.Page.GotoAsync("/proof");
            bool refused = await browser.Page.EvaluateAsync<bool>("""
                async challenge => {
                    const solver = (await import('/js/anonymous-registration-challenge-interop.js')).createSolver();
                    let progress = 0;
                    return solver.solve(challenge, { invokeMethodAsync: async () => { progress++; } })
                        .then(() => false, failure => failure.message === 'challenge-unavailable' && progress === 0);
                }
                """, challenge);
            await Assert.That(refused).IsTrue();
        });

    [Test]
    [Arguments("")]
    [Arguments("/community/nested")]
    public Task GuestAuthorityIsExactRouteMemoryOnlyAndOneShot(string pathBase) => RunModuleAsync(
        BrowserJourney.GuestStatus, async browser =>
        {
            string token = GuestToken();
            await browser.Page.GotoAsync(pathBase + GuestPath + "#capability=" + token);
            using JsonDocument result = await TakeGuestAsync(browser, EventId, OrderId);
            await Assert.That(result.RootElement.GetProperty("value").GetString() == token).IsTrue();
            await Assert.That(result.RootElement.GetProperty("consumed").GetBoolean()
                && result.RootElement.GetProperty("clean").GetBoolean()
                && result.RootElement.GetProperty("frozen").GetBoolean()).IsTrue();
            await browser.Page.GotoAsync(pathBase + GuestPath);
            using JsonDocument clean = await TakeGuestAsync(browser, EventId, OrderId);
            await Assert.That(clean.RootElement.GetProperty("value").ValueKind == JsonValueKind.Null).IsTrue();
        });

    [Test]
    [Arguments("foreign-order")]
    [Arguments("changed-path")]
    public Task GuestRouteMismatchConsumesAndDeniesAuthority(string variant) => RunModuleAsync(
        BrowserJourney.GuestStatus, async browser =>
        {
            await browser.Page.GotoAsync(GuestPath + "#capability=" + GuestToken());
            if (variant == "changed-path")
                await browser.Page.EvaluateAsync("() => history.replaceState(null, '', '/other')");
            using JsonDocument result = await TakeGuestAsync(browser, EventId,
                variant == "foreign-order" ? Guid.CreateVersion7().ToString("D") : OrderId);
            await Assert.That(result.RootElement.GetProperty("value").GetString() == string.Empty
                && result.RootElement.GetProperty("consumed").GetBoolean()).IsTrue();
        });

    [Test]
    [Arguments("malformed")]
    [Arguments("duplicate")]
    [Arguments("extra")]
    [Arguments("unknown")]
    [Arguments("encoded")]
    [Arguments("padded")]
    [Arguments("query")]
    [Arguments("mixed")]
    public Task GuestMalformedPrivateInputIsErasedAndDenied(string variant) => RunModuleAsync(
        BrowserJourney.GuestStatus, async browser =>
        {
            string token = GuestToken();
            string input = variant switch
            {
                "malformed" => "#capability=%ZZ",
                "duplicate" => "#capability=" + token + "&capability=" + token,
                "extra" => "#capability=" + token + "&extra=1",
                "unknown" => "#unknown=" + token,
                "encoded" => "#capability=%41" + token[1..],
                "padded" => "#capability=" + token + "=",
                "query" => "?capability=" + token,
                "mixed" => "?public=1#capability=" + token,
                _ => throw new InvalidOperationException("unknown_vector")
            };
            await browser.Page.GotoAsync(GuestPath + input);
            using JsonDocument result = await TakeGuestAsync(browser, EventId, OrderId);
            await Assert.That(result.RootElement.GetProperty("value").GetString() == string.Empty
                && result.RootElement.GetProperty("consumed").GetBoolean()
                && result.RootElement.GetProperty("clean").GetBoolean()).IsTrue();
        });

    [Test]
    public Task GuestExplicitExportsUsePrivatePathBaseUrlAndRevokeDownload() => RunModuleAsync(
        BrowserJourney.GuestStatus, async browser =>
        {
            string token = GuestToken();
            const string pathBase = "/community/nested";
            await browser.Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
            await browser.Page.GotoAsync(pathBase + GuestPath);
            bool copied = await SaveGuestAsync(browser, "copy", token);
            string clipboard = await browser.Page.EvaluateAsync<string>("() => navigator.clipboard.readText()");
            string expected = ModuleOrigin + pathBase + GuestPath + "#capability=" + token;
            await Assert.That(copied && clipboard == expected).IsTrue();
            Task<IDownload> downloading = browser.Page.WaitForDownloadAsync();
            bool saved = await SaveGuestAsync(browser, "download", token);
            IDownload download = await downloading;
            try
            {
                await using Stream? stream = await download.CreateReadStreamAsync();
                using var reader = new StreamReader(stream ?? throw new InvalidOperationException("download_missing"));
                string content = await reader.ReadToEndAsync();
                await Assert.That(saved && download.SuggestedFilename == "private-registration-status.txt"
                    && content == expected + "\n" && download.Url.StartsWith("blob:", StringComparison.Ordinal)).IsTrue();
                bool revoked = await browser.Page.EvaluateAsync<bool>("url => fetch(url).then(() => false, () => true)", download.Url);
                await Assert.That(revoked).IsTrue();
            }
            finally { await download.DeleteAsync(); }
            await Assert.That(!browser.Page.Url.Contains(token, StringComparison.Ordinal)
                && !(await browser.Page.ContentAsync()).Contains(token, StringComparison.Ordinal)
                && await browser.Page.Locator("a[download]").CountAsync() == 0).IsTrue();
        }, acceptDownloads: true);

    [Test]
    [Arguments("denied")]
    [Arguments("unavailable")]
    public Task GuestClipboardFailureDoesNotCreateFallbackArtifact(string variant) => RunModuleAsync(
        BrowserJourney.GuestStatus, async browser =>
        {
            await browser.Context.GrantPermissionsAsync([]);
            await browser.Page.GotoAsync(GuestPath);
            if (variant == "unavailable")
                await browser.Page.EvaluateAsync("() => Object.defineProperty(navigator, 'clipboard', { value: undefined })");
            bool downloaded = false;
            browser.Page.Download += (_, _) => downloaded = true;
            string token = GuestToken();
            bool copied = await SaveGuestAsync(browser, "copy", token);
            await Assert.That(!copied && !downloaded && !browser.Page.Url.Contains(token, StringComparison.Ordinal)
                && !(await browser.Page.ContentAsync()).Contains(token, StringComparison.Ordinal)).IsTrue();
        });

    [Test]
    public async Task ActualHostFirstNonceScriptScrubsPrivateInputBeforeSubsequentNetwork()
    {
        try
        {
            BrowserArtifactPolicy.AdmitDiagnostics(Environment.GetEnvironmentVariable("DEBUG"), Environment.GetEnvironmentVariable("PWDEBUG"));
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
            deadline.CancelAfter(TimeSpan.FromMinutes(15));
            await using var host = await OwnedAppHostFixture.CreateAsync("Split", deadline.Token,
                configuredAdministrator: true, deploymentMode: "MultiTenant");
            await host.StartAsync(deadline.Token);
            using HttpClient endpoint = host.CreateHttpClient("explore-blazor");
            await using (var setup = await OwnedBrowserContext.CreateAsync(endpoint.BaseAddress!))
                await BrowserJourneyTests.ReplaceBootstrapCredentialAsync(host, setup);
            using HttpClient api = await BrowserJourneyTests.AuthenticatedApiAsync(host, deadline.Token);
            string slug = "assurance-" + host.RunId[..8];
            await BrowserJourneyTests.CreateTenantAsync(api, slug, deadline.Token);
            foreach (string prefix in new[] { string.Empty, "/" + slug })
            foreach (bool malformed in new[] { false, true })
            {
                await using var browser = await OwnedBrowserContext.CreateAsync(endpoint.BaseAddress!);
                await browser.Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
                bool cleanAtRequest = true;
                bool privateNetwork = false;
                int subsequentRequests = 0;
                string token = GuestToken();
                await browser.Context.RouteAsync("**/*", async route =>
                {
                    privateNetwork |= route.Request.Url.Contains(token, StringComparison.Ordinal)
                        || route.Request.Headers.Values.Any(value => value.Contains(token, StringComparison.Ordinal));
                    if (!route.Request.IsNavigationRequest)
                    {
                        subsequentRequests++;
                        cleanAtRequest &= await browser.Page.EvaluateAsync<bool>("() => !location.hash && !location.search");
                    }
                    await route.ContinueAsync();
                });
                IResponse? response = await browser.Page.GotoAsync(prefix + GuestPath
                    + (malformed ? "?capability=" + token + "#capability=%ZZ" : "#capability=" + token));
                using JsonDocument document = JsonDocument.Parse(await browser.Page.EvaluateAsync<string>("""
                    () => JSON.stringify({ firstScript: document.head.firstElementChild?.tagName === 'SCRIPT',
                        nonce: document.head.firstElementChild?.nonce,
                        frozen: Object.isFrozen(window.guestRegistrationStatus), base: document.baseURI,
                        clean: !location.hash && !location.search })
                    """));
                JsonElement state = document.RootElement;
                string nonce = state.GetProperty("nonce").GetString() ?? string.Empty;
                await Assert.That(response is not null && response.Ok && nonce.Length > 0
                    && response.Headers.TryGetValue("content-security-policy", out string? csp)
                    && csp.Contains("'nonce-" + nonce + "'", StringComparison.Ordinal)
                    && state.GetProperty("firstScript").GetBoolean() && state.GetProperty("frozen").GetBoolean()
                    && state.GetProperty("clean").GetBoolean() && cleanAtRequest && !privateNetwork && subsequentRequests > 0).IsTrue();
                await Assert.That(state.GetProperty("base").GetString()
                    == new Uri(endpoint.BaseAddress!, prefix + "/").AbsoluteUri).IsTrue();
                await Assert.That(await SaveGuestAsync(browser, "copy", token)).IsTrue();
                string copied = await browser.Page.EvaluateAsync<string>("() => navigator.clipboard.readText()");
                await Assert.That(copied == new Uri(endpoint.BaseAddress!, prefix + GuestPath).AbsoluteUri + "#capability=" + token).IsTrue();
            }
        }
        catch (Exception failure) { throw BrowserArtifactPolicy.Sanitize(BrowserJourney.HostPrivacy, failure); }
    }

    private static async Task<JsonDocument> TakeGuestAsync(OwnedBrowserContext browser, string eventId, string orderId) =>
        JsonDocument.Parse(await browser.Page.EvaluateAsync<string>("""
            ({eventId, orderId}) => JSON.stringify({ value: guestRegistrationStatus.take(eventId, orderId),
                consumed: guestRegistrationStatus.take(eventId, orderId) === null,
                clean: !location.hash && !location.search, frozen: Object.isFrozen(guestRegistrationStatus) })
            """, new { eventId, orderId }));

    private static Task<bool> SaveGuestAsync(OwnedBrowserContext browser, string action, string token) =>
        browser.Page.EvaluateAsync<bool>("""
            input => guestRegistrationStatus.save(input.action, input.eventId, input.orderId, input.token)
            """, new { action, eventId = EventId, orderId = OrderId, token });

    private static Dictionary<string, object> RecoveryPointer() => new()
    {
        ["operationId"] = Guid.CreateVersion7().ToString("D"),
        ["localSubjectId"] = Guid.CreateVersion7().ToString("D"),
        ["personalActorId"] = Guid.CreateVersion7().ToString("D"),
        ["externalLoginId"] = Guid.CreateVersion7().ToString("D"),
        ["generation"] = Guid.CreateVersion7().ToString("D"),
        ["purpose"] = 2,
        ["token"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)) + "+/%2F=&"
    };

    private static string EncodePointer(Dictionary<string, object> pointer) => string.Join('&', pointer.Select(
        pair => pair.Key + "=" + Uri.EscapeDataString(pair.Value.ToString()!)));

    private static Dictionary<string, object> Challenge(string envelope, int difficulty) => new()
    {
        ["version"] = 1, ["difficulty"] = difficulty, ["protectedChallenge"] = envelope,
        ["expiresAt"] = DateTimeOffset.UtcNow.AddSeconds(110).ToString("O")
    };

    private static bool IsProof(string envelope, string nonce, int difficulty)
    {
        if (nonce.Length != 16 || nonce.Any(character => !char.IsAsciiHexDigitLower(character)))
            return false;
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes("islamu-event:anonymous-registration:v1\n" + envelope + "\n" + nonce));
        for (int bit = 0; bit < difficulty; bit++)
            if ((digest[bit / 8] & (0x80 >> (bit % 8))) != 0)
                return false;
        return true;
    }

    private static string GuestToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static async Task RunModuleAsync(BrowserJourney journey, Func<OwnedBrowserContext, Task> scenario,
        bool acceptDownloads = false)
    {
        try
        {
            await using var browser = await OwnedBrowserContext.CreateAsync(new Uri(ModuleOrigin), acceptDownloads);
            string scripts = FindScripts();
            await browser.Context.RouteAsync("**/*", async route =>
            {
                var uri = new Uri(route.Request.Url);
                if (uri.Host != "127.0.0.1") { await route.AbortAsync(); return; }
                string name = Path.GetFileName(uri.AbsolutePath);
                if (uri.AbsolutePath.StartsWith("/js/", StringComparison.Ordinal)
                    && name is "local-account-recovery.js" or "bff.js" or "anonymous-registration-challenge-interop.js"
                        or "anonymous-registration-challenge.js" or "guest-registration-status.js")
                {
                    await route.FulfillAsync(new() { ContentType = "text/javascript", Body = await File.ReadAllTextAsync(Path.Combine(scripts, name)) });
                    return;
                }
                if (route.Request.IsNavigationRequest)
                {
                    string prefix = uri.AbsolutePath.Contains("/community/nested/", StringComparison.Ordinal) ? "/community/nested/" : "/";
                    await route.FulfillAsync(new() { ContentType = "text/html", Body =
                        "<!doctype html><html><head><script src='/js/guest-registration-status.js'></script><base href='" + prefix + "'></head><body></body></html>" });
                    return;
                }
                await route.AbortAsync();
            });
            await scenario(browser).WaitAsync(TimeSpan.FromMinutes(3), TestContext.Current!.Execution.CancellationToken);
        }
        catch (Exception failure) { throw BrowserArtifactPolicy.Sanitize(journey, failure); }
    }

    private static string FindScripts()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string scripts = Path.Combine(directory.FullName, "src", "Explore.Blazor.Client", "wwwroot", "js");
            if (Directory.Exists(scripts)) return scripts;
        }
        throw new InvalidOperationException("product_browser_modules_missing");
    }
}
