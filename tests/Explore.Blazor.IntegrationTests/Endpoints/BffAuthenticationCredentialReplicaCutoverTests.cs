using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Explore.Blazor.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Blazor.IntegrationTests.Endpoints;

[NotInParallel]
[ClassDataSource<BffKeycloakFixture>(Shared = SharedType.PerClass)]
public sealed class BffAuthenticationCredentialReplicaCutoverTests(BffKeycloakFixture keycloak)
{
    [Test]
    public async Task ReplacementBootAuthenticatesWhileRetiredCredentialCannotStartAnotherSession()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current!.Execution.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        CancellationToken cancellationToken = timeout.Token;
        // Native cookie refresh begins inside its 60-second expiry window; no sleep is needed.
        await keycloak.UseShortLivedAccessTokensAsync(cancellationToken);
        string retiredCredential = keycloak.ClientSecret;
        var originalRefresh = new RefreshObservation();
        await using var original = ObserveRefresh(new SecurityBlazorBffWebApplicationFactory(
            keycloak.Authority, keycloak.MetadataAddress, BffKeycloakFixture.TestClientId, retiredCredential),
            originalRefresh);
        using var browser = CreateBrowser(original);
        await Assert.That(await LoginAsync(browser, cancellationToken)).IsTrue();
        await Assert.That(await originalRefresh.Completed.Task.WaitAsync(cancellationToken))
            .IsEqualTo(HttpStatusCode.OK);

        await keycloak.RotateClientSecretAsync(cancellationToken);
        await Assert.That(string.Equals(retiredCredential, keycloak.ClientSecret, StringComparison.Ordinal)).IsFalse();

        originalRefresh.Completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var status = await browser.GetAsync("/auth/status", cancellationToken))
        {
            using var body = JsonDocument.Parse(await status.Content.ReadAsStringAsync(cancellationToken));
            await Assert.That(body.RootElement.GetProperty("isAuthenticated").GetBoolean()).IsFalse();
        }
        await Assert.That(await originalRefresh.Completed.Task.WaitAsync(cancellationToken))
            .IsEqualTo(HttpStatusCode.Unauthorized);

        // A fresh browser prevents the original session cookie from masking a failed exchange.
        using (var staleBrowser = CreateBrowser(original))
            await Assert.That(await LoginAsync(staleBrowser, cancellationToken, retiredCredential)).IsFalse();

        var replacementRefresh = new RefreshObservation();
        await using var replacement = ObserveRefresh(new SecurityBlazorBffWebApplicationFactory(
            keycloak.Authority, keycloak.MetadataAddress, BffKeycloakFixture.TestClientId, keycloak.ClientSecret),
            replacementRefresh);
        using var replacementBrowser = CreateBrowser(replacement);
        await Assert.That(await LoginAsync(replacementBrowser, cancellationToken)).IsTrue();
        await Assert.That(await replacementRefresh.Completed.Task.WaitAsync(cancellationToken))
            .IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task CoordinatedProcessStopDrainsOldRequestWhileReplacementKeepsAuthenticating()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current!.Execution.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        CancellationToken cancellationToken = timeout.Token;
        await using var fixture = new BffCredentialCutoverProcessFixture();
        await fixture.StartAsync(cancellationToken);
        string retiredCredential = keycloak.ClientSecret;
        await using var original = await fixture.StartReplicaAsync(keycloak, retiredCredential, cancellationToken);
        using var originalBrowser = original.CreateBrowser();
        await Assert.That(await LoginAsync(originalBrowser, cancellationToken)).IsTrue();
        Task<HttpResponseMessage> draining = originalBrowser.GetAsync("/api/cutover-drain", cancellationToken);
        await fixture.Entered.Task.WaitAsync(cancellationToken);

        await keycloak.RotateClientSecretAsync(cancellationToken);
        await using var replacement = await fixture.StartReplicaAsync(keycloak, keycloak.ClientSecret, cancellationToken);
        await Assert.That(replacement.ProcessId).IsNotEqualTo(original.ProcessId);
        using var replacementBrowser = replacement.CreateBrowser();
        await Assert.That(await LoginAsync(replacementBrowser, cancellationToken)).IsTrue();
        using (var staleBrowser = original.CreateBrowser())
            await Assert.That(await LoginAsync(staleBrowser, cancellationToken, retiredCredential)).IsFalse();

        // Exit and shutdown signals were subscribed before either process started.
        await original.SignalStopAsync(cancellationToken);
        await original.Stopping.Task.WaitAsync(cancellationToken);
        await Assert.That(original.Exited.Task.IsCompleted).IsFalse();
        await Assert.That(draining.IsCompleted).IsFalse();
        fixture.Release.TrySetResult();
        using var completed = await draining;
        await Assert.That(completed.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await completed.Content.ReadAsStringAsync(cancellationToken)).IsEqualTo("drained");
        await Assert.That(await original.Exited.Task.WaitAsync(cancellationToken)).IsEqualTo(0);
        await Assert.That(replacement.Exited.Task.IsCompleted).IsFalse();
        using var status = await replacementBrowser.GetAsync("/auth/status", cancellationToken);
        using var body = JsonDocument.Parse(await status.Content.ReadAsStringAsync(cancellationToken));
        await Assert.That(body.RootElement.GetProperty("isAuthenticated").GetBoolean()).IsTrue();
    }

    private static WebApplicationFactory<Program> ObserveRefresh(
        SecurityBlazorBffWebApplicationFactory factory, RefreshObservation observation) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddHttpClient("Event.Web.BffHosting.TokenRefresh")
                .AddHttpMessageHandler(() => new RefreshObserver(observation))));

    private sealed class RefreshObservation
    {
        public TaskCompletionSource<HttpStatusCode> Completed { get; set; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class RefreshObserver(RefreshObservation observation) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            bool refresh = request.Content is not null
                && (await request.Content.ReadAsStringAsync(cancellationToken))
                    .Split('&').Contains("grant_type=refresh_token", StringComparer.Ordinal);
            HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
            if (refresh)
                observation.Completed.TrySetResult(response.StatusCode);
            return response;
        }
    }

    private static HttpClient CreateBrowser(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });

    private async Task<bool> LoginAsync(
        HttpClient browser, CancellationToken cancellationToken, string? retiredCredential = null)
    {
        using var challenge = await browser.GetAsync(
            "/auth/challenge?provider=keycloak&returnUrl=/dashboard", cancellationToken);
        await Assert.That(challenge.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(challenge.Headers.Location is not null).IsTrue();

        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            CookieContainer = new CookieContainer()
        };
        using var provider = new HttpClient(handler);
        using var page = await provider.GetAsync(challenge.Headers.Location, cancellationToken);
        await Assert.That(page.StatusCode).IsEqualTo(HttpStatusCode.OK);
        string html = await page.Content.ReadAsStringAsync(cancellationToken);
        string action = Regex.Match(html,
            "<form(?=[^>]*id=\"kc-form-login\")(?=[^>]*action=\"(?<action>[^\"]+)\")[^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1)).Groups["action"].Value;
        await Assert.That(string.IsNullOrWhiteSpace(action)).IsFalse();
        using var credentials = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = "test-user",
            ["password"] = keycloak.TestUserPassword,
            ["credentialId"] = string.Empty,
            ["login"] = "Sign In"
        });
        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, WebUtility.HtmlDecode(action))
        {
            Content = credentials
        };
        loginRequest.Headers.TryAddWithoutValidation("Cookie", string.Join("; ",
            handler.CookieContainer.GetAllCookies().Cast<Cookie>()
                .Select(cookie => $"{cookie.Name}={cookie.Value}")));
        using var authorized = await provider.SendAsync(loginRequest, cancellationToken);
        await Assert.That(authorized.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(authorized.Headers.Location is not null).IsTrue();

        using var callback = await browser.GetAsync(authorized.Headers.Location, cancellationToken);
        string callbackBody = await callback.Content.ReadAsStringAsync(cancellationToken);
        await Assert.That(callbackBody.Contains(keycloak.ClientSecret, StringComparison.Ordinal)
            || callbackBody.Contains(keycloak.TestUserPassword, StringComparison.Ordinal)
            || retiredCredential is not null && callbackBody.Contains(retiredCredential, StringComparison.Ordinal)).IsFalse();
        using var status = await browser.GetAsync("/auth/status", cancellationToken);
        await Assert.That(status.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await status.Content.ReadAsStringAsync(cancellationToken));
        return body.RootElement.GetProperty("isAuthenticated").GetBoolean();
    }
}
