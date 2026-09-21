using System.Buffers;
using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Services;
using Explore.Blazor.IntegrationTests.Fixtures;
using Explore.Blazor.Services;
using Event.Web.BffHosting.Security;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Explore.Blazor.IntegrationTests.Endpoints;

[NotInParallel]
[ClassDataSource<BffKeycloakFixture>(Shared = SharedType.PerClass)]
public sealed class BffSetupJourneyTransportTests(BffKeycloakFixture keycloak)
{
    [Test]
    public async Task ExternalCallbackRetainsSetupCookiesThroughHostedCircuitAndYarp()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        var ct = timeout.Token;
        string secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        Guid userId = Guid.CreateVersion7();
        var captures = new System.Collections.Concurrent.ConcurrentQueue<(bool Bearer, bool Setup, bool Cookie)>();
        var circuitResult = new TaskCompletionSource<CircuitIdentityObservation>(TaskCreationOptions.RunContinuationsAsynchronously);
        var syncTokens = new System.Collections.Concurrent.ConcurrentQueue<string>();
        ClaimsPrincipal? callbackPrincipal = null;
        string? validatedIdToken = null;
        string? providerAccessToken = null;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var upstream = builder.Build();
        upstream.MapGet("/api/instanceonboarding/journey", (HttpContext context) =>
        {
            bool bearer = context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.Ordinal);
            bool setup = context.Request.Headers["X-Setup-Secret"] == secret;
            captures.Enqueue((bearer, setup, context.Request.Headers.ContainsKey("Cookie")));
            return setup ? Results.Json(new
            {
                state = "Available", generation = "transport-test", profile = new { siteName = "Private instance" },
                bootstrap = new { isCompleted = false, isAuthenticated = bearer, state = "InteractivePending", provider = "Keycloak", selectedDeploymentMode = "SingleTenant" },
                authentication = new { provider = "Keycloak", state = "Ready" }, authorization = new { provider = "local", state = "Ready" },
                preflight = new { isReadyToLaunch = true, blockingChecks = Array.Empty<object>(), warningChecks = Array.Empty<object>() },
                _links = new { refresh = new { href = "/api/instanceonboarding/journey" } }
            }) : Results.Problem(statusCode: 403);
        });
        foreach (string path in new[] { "/api/events", "/api/instanceonboarding/journey/details", "/api/instanceonboarding/journey-report" })
            upstream.MapGet(path, (HttpContext context) => Results.Json(new
            { setupForwarded = context.Request.Headers.ContainsKey("X-Setup-Secret"), cookieLeaked = context.Request.Headers.ContainsKey("Cookie") }));
        upstream.MapPost("/api/instanceonboarding/validate-secret", async (HttpContext context) =>
        {
            using var body = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
            return Results.Json(new { valid = body.RootElement.GetProperty("secret").GetString() == secret });
        });
        upstream.MapGet("/api/instanceonboarding/status", () => Results.Json(new
        { isCompleted = false, state = "InteractivePending", mode = "Interactive", provider = "Keycloak", generation = 1, selectedDeploymentMode = "SingleTenant" }));
        upstream.MapGet("/api/instanceonboarding/auth-provider-configuration", () => Results.Json(new
        { primaryProviderId = 1, keycloakAuthority = keycloak.Authority, keycloakClientId = BffKeycloakFixture.TestClientId }));
        upstream.MapGet("/api/instance/settings/branding", () => Results.Json(new { defaultBrandDisplayName = "Private instance" }));
        upstream.MapGet("/api/user/admin-authority", () => Results.Json(new { isInstanceAdmin = false }));
        upstream.MapPost("/api/user/sync", (HttpContext context) =>
        {
            syncTokens.Enqueue(context.Request.Headers.Authorization.ToString());
            return Results.Json(new { success = true, id = userId });
        });
        upstream.MapGet("/api/user", () => Results.Json(new { id = userId }));
        await upstream.StartAsync(ct);
        string address = upstream.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        await using var root = new SecurityBlazorBffWebApplicationFactory(keycloak.Authority, keycloak.MetadataAddress,
            BffKeycloakFixture.TestClientId, keycloak.ClientSecret);
        await using var factory = root.WithWebHostBuilder(host =>
        {
            host.UseSetting("ExploreApi:BaseUrl", address);
            host.UseSetting("Authentication:Provider", "keycloak");
            host.ConfigureTestServices(services =>
            {
                services.RemoveAll<IInstanceOnboardingClient>();
                services.AddScoped<IInstanceOnboardingClient>(provider => new InstanceOnboardingClient(
                    provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(IInstanceOnboardingClient))));
                services.RemoveAll<IBffOnboardingStatusProvider>();
                services.AddSingleton<IBffOnboardingStatusProvider, BffOnboardingStatusProvider>();
                services.AddScoped<CircuitHandler>(provider => new JourneyCircuitProbe(
                    provider.GetRequiredService<IInstanceOnboardingService>(),
                    provider.GetRequiredService<AuthenticationStateProvider>(),
                    provider.GetRequiredService<ICircuitAccessTokenService>(),
                    provider.GetRequiredService<IUserService>(), circuitResult));
            });
        });
        using var browser = factory.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = false });
        var cookies = new CookieContainer();
        async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
        {
            using var ownedRequest = request;
            Uri target = new(browser.BaseAddress!, request.RequestUri!);
            string cookieHeader = cookies.GetCookieHeader(target);
            if (cookieHeader.Length > 0) request.Headers.Add("Cookie", cookieHeader);
            var response = await browser.SendAsync(request, ct);
            if (response.Headers.TryGetValues("Set-Cookie", out var values))
                foreach (string value in values) cookies.SetCookies(target, value);
            return response;
        }
        using (var status = await SendAsync(new(HttpMethod.Get, "/auth/status"))) status.EnsureSuccessStatusCode();
        using (var setup = new HttpRequestMessage(HttpMethod.Post, "/bff/setup-secret") { Content = JsonContent.Create(new { secret }) })
        {
            setup.Headers.Add("X-CSRF-TOKEN", Uri.UnescapeDataString(cookies.GetCookies(browser.BaseAddress!)["XSRF-TOKEN"]!.Value));
            using var saved = await SendAsync(setup);
            await Assert.That(saved.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
        using var challenge = await SendAsync(new(HttpMethod.Get, "/auth/challenge?provider=keycloak&returnUrl=/onboarding/instance"));
        await Assert.That(challenge.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        var oidc = factory.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get("Keycloak");
        var onValidated = oidc.Events.OnTokenValidated;
        oidc.Events.OnTokenValidated = async context =>
        {
            providerAccessToken = context.TokenEndpointResponse?.AccessToken;
            await onValidated(context);
            callbackPrincipal = context.Principal is null ? null : new ClaimsPrincipal(
                context.Principal.Identities.Select(identity => new ClaimsIdentity(identity)));
            validatedIdToken = context.TokenEndpointResponse?.IdToken ?? context.ProtocolMessage.IdToken;
        };
        using var idpHandler = new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() };
        using var idp = new HttpClient(idpHandler);
        using var loginPage = await idp.GetAsync(challenge.Headers.Location, ct);
        string html = await loginPage.Content.ReadAsStringAsync(ct);
        string action = WebUtility.HtmlDecode(Regex.Match(html,
            "<form(?=[^>]*id=\"kc-form-login\")(?=[^>]*action=\"(?<action>[^\"]+)\")[^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Groups["action"].Value);
        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, action)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            { ["username"] = "test-user", ["password"] = keycloak.TestUserPassword, ["credentialId"] = string.Empty })
        };
        loginRequest.Headers.Add("Cookie", string.Join("; ", idpHandler.CookieContainer.GetAllCookies().Cast<Cookie>().Select(cookie => $"{cookie.Name}={cookie.Value}")));
        using var login = await idp.SendAsync(loginRequest, ct);
        await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        using var callback = await SendAsync(new(HttpMethod.Get, login.Headers.Location));
        await Assert.That(callback.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        using (var status = await SendAsync(new(HttpMethod.Get, "/auth/status")))
        {
            using var body = JsonDocument.Parse(await status.Content.ReadAsStringAsync(ct));
            await Assert.That(body.RootElement.GetProperty("isAuthenticated").GetBoolean()).IsTrue();
        }
        var cookieOptions = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get("Cookies");
        var cookieContext = new DefaultHttpContext();
        cookieContext.Request.Headers.Cookie = cookies.GetCookieHeader(browser.BaseAddress!);
        var ticket = cookieOptions.TicketDataFormat.Unprotect(cookieOptions.CookieManager.GetRequestCookie(cookieContext, cookieOptions.Cookie.Name!)!);
        await Assert.That(ticket).IsNotNull();
        var idToken = new JwtSecurityTokenHandler().ReadJwtToken(validatedIdToken);
        var accessToken = new JwtSecurityTokenHandler().ReadJwtToken(ticket!.Properties.GetTokenValue("access_token"));
        using var page = await SendAsync(new(HttpMethod.Get, "/onboarding/instance"));
        await Assert.That(page.StatusCode).IsEqualTo(HttpStatusCode.OK);
        string rendered = await page.Content.ReadAsStringAsync(ct);
        string[] descriptors = Regex.Matches(rendered, "<!--Blazor:(?<marker>\\{.*?\\})-->", RegexOptions.CultureInvariant)
            .Select(match => match.Groups["marker"].Value).Where(marker =>
            {
                using var document = JsonDocument.Parse(marker);
                return document.RootElement.TryGetProperty("type", out var type) && type.GetString() == "server"
                    && document.RootElement.TryGetProperty("descriptor", out _);
            }).ToArray();
        await Assert.That(descriptors.Length > 0).IsTrue();
        using var negotiate = await SendAsync(new(HttpMethod.Post, "/_blazor/negotiate?negotiateVersion=1"));
        using var negotiation = JsonDocument.Parse(await negotiate.Content.ReadAsStringAsync(ct));
        string connection = negotiation.RootElement.GetProperty("connectionToken").GetString()!;
        var sockets = factory.Server.CreateWebSocketClient();
        sockets.ConfigureRequest = request => request.Headers.Cookie = cookies.GetCookieHeader(browser.BaseAddress!);
        using var socket = await sockets.ConnectAsync(new Uri($"wss://localhost/_blazor?id={Uri.EscapeDataString(connection)}"), ct);
        await socket.SendAsync(Encoding.UTF8.GetBytes("{\"protocol\":\"blazorpack\",\"version\":1}\u001e"), WebSocketMessageType.Text, true, ct);
        _ = await ReceiveAsync(socket, ct);
        var protocol = factory.Services.GetServices<IHubProtocol>().Single(candidate => candidate.Name == "blazorpack");
        var writer = new ArrayBufferWriter<byte>();
        protocol.WriteMessage(new InvocationMessage("start", "StartCircuit",
            ["https://localhost/", "https://localhost/onboarding/instance", $"[{string.Join(',', descriptors)}]", string.Empty]), writer);
        await socket.SendAsync(writer.WrittenMemory, WebSocketMessageType.Binary, true, ct);
        var circuit = await circuitResult.Task.WaitAsync(ct);
        await Assert.That(providerAccessToken == ticket.Properties.GetTokenValue("access_token")).IsTrue();
        await Assert.That(accessToken.Subject == idToken.Subject && accessToken.Issuer == idToken.Issuer).IsTrue();
        await Assert.That(circuit.Principal.TryGetCircuitSubject(out _)).IsTrue();
        await Assert.That(circuit.Journey).IsTrue();
        await Assert.That(circuit.Token == ticket.Properties.GetTokenValue("access_token")).IsTrue();
        foreach (ClaimsPrincipal stage in new[] { callbackPrincipal!, ticket.Principal, circuit.Principal })
        {
            await Assert.That(stage.FindFirst("sub")?.Value == idToken.Subject).IsTrue();
            await Assert.That(stage.FindFirst("iss")?.Value == idToken.Issuer).IsTrue();
            await Assert.That(stage.FindFirst("email_verified")?.Value == idToken.Claims.SingleOrDefault(claim => claim.Type == "email_verified")?.Value).IsTrue();
        }
        await Assert.That(!syncTokens.IsEmpty && syncTokens.All(value => value == "Bearer " + ticket.Properties.GetTokenValue("access_token"))).IsTrue();
        await Assert.That(captures.Any(capture => capture.Bearer && capture.Setup && !capture.Cookie)).IsTrue();
        using (var setupStatus = await SendAsync(new(HttpMethod.Get, "/bff/setup-secret")))
        {
            using var body = JsonDocument.Parse(await setupStatus.Content.ReadAsStringAsync(ct));
            await Assert.That(body.RootElement.GetProperty("hasPersistedSecret").GetBoolean()).IsTrue();
            await Assert.That(body.RootElement.GetProperty("isValid").GetBoolean()).IsTrue();
        }
        using var proxyRequest = new HttpRequestMessage(HttpMethod.Get, "/api/instanceonboarding/journey");
        proxyRequest.Headers.Add("X-Setup-Secret", Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        using var proxy = await SendAsync(proxyRequest);
        var final = captures.Last();
        await Assert.That(final.Bearer).IsTrue();
        await Assert.That(final.Cookie).IsFalse();
        await Assert.That(proxy.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(final.Setup).IsTrue();
        foreach (string path in new[] { "/api/events", "/api/instanceonboarding/journey/details", "/api/instanceonboarding/journey-report" })
        {
            using var unrelatedRequest = new HttpRequestMessage(HttpMethod.Get, path);
            unrelatedRequest.Headers.Add("X-Setup-Secret", secret);
            using var unrelated = await SendAsync(unrelatedRequest);
            using var body = JsonDocument.Parse(await unrelated.Content.ReadAsStringAsync(ct));
            await Assert.That(body.RootElement.GetProperty("setupForwarded").GetBoolean()).IsFalse();
            await Assert.That(body.RootElement.GetProperty("cookieLeaked").GetBoolean()).IsFalse();
        }
        using (var status = await SendAsync(new(HttpMethod.Get, "/auth/status"))) status.EnsureSuccessStatusCode();
        using (var clear = new HttpRequestMessage(HttpMethod.Delete, "/bff/setup-secret"))
        {
            clear.Headers.Add("X-CSRF-TOKEN", Uri.UnescapeDataString(cookies.GetCookies(browser.BaseAddress!)["XSRF-TOKEN"]!.Value));
            using var cleared = await SendAsync(clear);
            cleared.EnsureSuccessStatusCode();
        }
        string expired = factory.Services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("Explore.Blazor.SetupSecretCookie.v1").ToTimeLimitedDataProtector()
            .Protect(secret, DateTimeOffset.UtcNow.AddMinutes(-1));
        foreach (string? invalid in new[] { null, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), expired })
        {
            if (invalid is not null) cookies.Add(browser.BaseAddress!, new Cookie("setup-secret", invalid, "/") { Secure = true });
            using var deniedRequest = new HttpRequestMessage(HttpMethod.Get, "/api/instanceonboarding/journey");
            deniedRequest.Headers.Add("X-Setup-Secret", secret);
            using var denied = await SendAsync(deniedRequest);
            await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
            var rejected = captures.Last();
            await Assert.That(rejected.Bearer && !rejected.Setup && !rejected.Cookie).IsTrue();
        }
    }

    private sealed record CircuitIdentityObservation(ClaimsPrincipal Principal, string? Token, bool Journey);

    private sealed class JourneyCircuitProbe(IInstanceOnboardingService journey, AuthenticationStateProvider authentication,
        ICircuitAccessTokenService tokens, IUserService users, TaskCompletionSource<CircuitIdentityObservation> result) : CircuitHandler
    {
        public override int Order => int.MaxValue;
        public override async Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            try
            {
                var principal = (await authentication.GetAuthenticationStateAsync()).User;
                await users.SyncUserAsync();
                bool available = await journey.GetJourneyAsync(cancellationToken) is not null;
                result.TrySetResult(new(principal.Clone(), tokens.AccessToken, available));
            }
            catch (Exception exception) { result.TrySetException(exception); throw; }
        }
    }

    private static async Task<byte[]> ReceiveAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var writer = new ArrayBufferWriter<byte>();
        byte[] buffer = new byte[4096];
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close) throw new InvalidOperationException("Circuit closed before handshake.");
            writer.Write(buffer.AsSpan(0, result.Count));
        } while (!result.EndOfMessage);
        return writer.WrittenSpan.ToArray();
    }
}
