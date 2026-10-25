using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Explore.AppHost;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Distributed.IntegrationTests;

[Category("Distributed")]
public sealed class ExternalDataProtectionRuntimeTests
{
    [Test]
    [Arguments(OwnedBffPeerProtection.Shared)]
    [Arguments(OwnedBffPeerProtection.IndependentValidAuthority)]
    public async Task SelectedExternalAuthorityControlsPeerSessionsAcrossBffRestart(OwnedBffPeerProtection protection)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(7));
        CancellationToken cancellationToken = deadline.Token;
        await using var owner = await OwnedAppHostFixture.CreateAsync("Split", cancellationToken,
            configuredAdministrator: true, deploymentMode: "MultiTenant",
            profile: OwnedAppHostProfile.ExternalIdentitySharedBff, peerProtection: protection);
        await owner.StartAsync(cancellationToken);
        using var api = owner.CreateHttpClient("explore-api");
        Guid userId = await owner.AuthenticateBootstrapAsync(api, cancellationToken);
        await Assert.That(File.Exists(Path.Combine(owner.Root, "identity.sqlite"))).IsTrue();
        using var issuer = new BffSession(owner.Application.GetEndpoint("explore-blazor", "https"));
        using var peer = new BffSession(owner.Application.GetEndpoint("explore-blazor-peer", "https"));
        await Assert.That(issuer.Address).IsNotEqualTo(peer.Address);
        using (var anonymous = await peer.SendAsync(HttpMethod.Get, "/api/user", null, cancellationToken))
            await Assert.That(anonymous.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await issuer.SignInAsync(owner, cancellationToken);
        await RequireCurrentUserAsync(issuer, userId, cancellationToken);
        using var original = new BffSession(issuer.Address);
        original.CopyCookiesFrom(issuer);
        peer.CopyCookiesFrom(issuer);
        if (protection == OwnedBffPeerProtection.IndependentValidAuthority)
        {
            using (var foreign = await peer.SendAsync(HttpMethod.Get, "/api/user", null, cancellationToken))
                await Assert.That(foreign.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
            await RequireCurrentUserAsync(issuer, userId, cancellationToken);
            await peer.SignInAsync(owner, cancellationToken);
        }
        await RequireCurrentUserAsync(peer, userId, cancellationToken);

        Task stopped = owner.Application.ResourceNotifications.WaitForResourceAsync(
            "explore-blazor", KnownResourceStates.Exited, cancellationToken);
        var stop = await owner.Application.ResourceCommands.ExecuteCommandAsync(
            "explore-blazor", KnownResourceCommands.StopCommand, cancellationToken);
        await Assert.That(stop.Success).IsTrue();
        await stopped;
        using (var operations = owner.CreateHttpClient("explore-blazor", "operations"))
        {
            bool unavailable;
            try
            {
                using var response = await operations.GetAsync("/health", cancellationToken);
                unavailable = response.StatusCode == HttpStatusCode.ServiceUnavailable;
            }
            catch (HttpRequestException) { unavailable = true; }
            await Assert.That(unavailable).IsTrue();
        }
        await RequireCurrentUserAsync(peer, userId, cancellationToken);

        Task running = owner.Application.ResourceNotifications.WaitForResourceAsync(
            "explore-blazor", KnownResourceStates.Running, cancellationToken);
        var start = await owner.Application.ResourceCommands.ExecuteCommandAsync(
            "explore-blazor", KnownResourceCommands.StartCommand, cancellationToken);
        await Assert.That(start.Success).IsTrue();
        await running;
        await owner.Application.ResourceNotifications.WaitForResourceHealthyAsync("explore-blazor", cancellationToken);
        await RequireCurrentUserAsync(issuer, userId, cancellationToken);
        await RequireCurrentUserAsync(peer, userId, cancellationToken);

        using var second = new BffSession(issuer.Address);
        await second.SignInAsync(owner, cancellationToken);
        await RequireCurrentUserAsync(second, userId, cancellationToken);

        using var peerOriginal = new BffSession(peer.Address);
        peerOriginal.CopyCookiesFrom(peer);
        Task authorityStopped = owner.Application.ResourceNotifications.WaitForResourceAsync(
            "bff-session-authority", KnownResourceStates.Exited, cancellationToken);
        var stopAuthority = await owner.Application.ResourceCommands.ExecuteCommandAsync(
            "bff-session-authority", KnownResourceCommands.StopCommand, cancellationToken);
        await Assert.That(stopAuthority.Success).IsTrue();
        await authorityStopped;
        await RequireSavedSessionUnavailableAsync(original, issuer.Address, cancellationToken);
        await RequireSavedSessionUnavailableAsync(peerOriginal, peer.Address, cancellationToken);
        await RequireSavedSessionUnavailableAsync(second, issuer.Address, cancellationToken);
        using (var anonymous = new BffSession(peer.Address))
        using (var status = await anonymous.SendAsync(HttpMethod.Get, "/auth/status", null, cancellationToken))
        {
            await Assert.That(status.StatusCode).IsEqualTo(HttpStatusCode.OK);
            using var body = JsonDocument.Parse(await status.Content.ReadAsStringAsync(cancellationToken));
            await Assert.That(body.RootElement.GetProperty("isAuthenticated").GetBoolean()).IsFalse();
        }
        using (var anonymousApi = owner.CreateHttpClient("explore-api"))
        using (var status = await anonymousApi.GetAsync("/api/InstanceOnboarding/status", cancellationToken))
            await Assert.That(status.StatusCode).IsEqualTo(HttpStatusCode.OK);

        Task authorityRunning = owner.Application.ResourceNotifications.WaitForResourceAsync(
            "bff-session-authority", KnownResourceStates.Running, cancellationToken);
        var startAuthority = await owner.Application.ResourceCommands.ExecuteCommandAsync(
            "bff-session-authority", KnownResourceCommands.StartCommand, cancellationToken);
        await Assert.That(startAuthority.Success).IsTrue();
        await authorityRunning;
        await owner.Application.ResourceNotifications.WaitForResourceHealthyAsync("bff-session-authority", cancellationToken);
        await RequireSavedSessionRecoveredAsync(original, issuer.Address, userId, cancellationToken);
        await RequireSavedSessionRecoveredAsync(peerOriginal, peer.Address, userId, cancellationToken);
        await RequireSavedSessionRecoveredAsync(second, issuer.Address, userId, cancellationToken);

        using (var revoker = new BffSession(protection == OwnedBffPeerProtection.Shared ? peer.Address : issuer.Address))
        {
            revoker.CopyCookiesFrom(original);
            await RequireCurrentUserAsync(revoker, userId, cancellationToken);
            using var signout = await revoker.SendAsync(HttpMethod.Get, "/auth/signout", null, cancellationToken);
            await Assert.That(signout.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        }
        await RequireSavedCookieRejectedAsync(original, issuer.Address, cancellationToken);
        // An independent peer rejects this cookie by partition; issuer replay proves revocation.
        await RequireSavedCookieRejectedAsync(original, peer.Address, cancellationToken);
        await RequireCurrentUserAsync(second, userId, cancellationToken);
        if (protection == OwnedBffPeerProtection.Shared)
        {
            using var secondAtPeer = new BffSession(peer.Address);
            secondAtPeer.CopyCookiesFrom(second);
            await RequireCurrentUserAsync(secondAtPeer, userId, cancellationToken);
        }
        else
        {
            await RequireCurrentUserAsync(peer, userId, cancellationToken);
        }
        await owner.DisposeAsync();
        await Assert.That(Directory.Exists(owner.Root)).IsFalse();
    }

    [Test]
    [Arguments(OwnedBffPeerProtection.WrongPartition)]
    [Arguments(OwnedBffPeerProtection.GenerationBelowFloor)]
    [Arguments(OwnedBffPeerProtection.ExpiredAdmission)]
    [Arguments(OwnedBffPeerProtection.RevokedActiveKey)]
    public async Task PeerWithUnadmittedProtectionAuthorityFailsDuringActualHostStartup(OwnedBffPeerProtection protection)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(6));
        CancellationToken cancellationToken = deadline.Token;
        await using var owner = await OwnedAppHostFixture.CreateAsync("Split", cancellationToken,
            profile: OwnedAppHostProfile.ExternalIdentitySharedBff, peerProtection: protection);
        Task witnesses = Task.WhenAll(WaitForPeerFailureAsync(owner, cancellationToken),
            WaitForPeerAdmissionRejectionAsync(owner, cancellationToken));
        try
        {
            Exception? failure = null;
            try { await owner.StartAsync(cancellationToken); }
            catch (Exception exception) when (exception is not OperationCanceledException) { failure = exception; }
            await Assert.That(failure).IsNotNull();
            await witnesses;
        }
        finally
        {
            await deadline.CancelAsync();
            try { await witnesses; }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }
        }
        await owner.DisposeAsync();
        await Assert.That(Directory.Exists(owner.Root)).IsFalse();
    }

    private static async Task WaitForPeerFailureAsync(OwnedAppHostFixture owner, CancellationToken cancellationToken)
    {
        await foreach (var update in owner.Application.ResourceNotifications.WatchAsync(cancellationToken))
            if (update.Resource.Name == "explore-blazor-peer" && update.Snapshot.ExitCode is not (null or 0))
                return;
        throw new InvalidOperationException("assurance_peer_failure_not_observed");
    }

    private static async Task WaitForPeerAdmissionRejectionAsync(OwnedAppHostFixture owner, CancellationToken cancellationToken)
    {
        var logs = owner.Application.Services.GetRequiredService<ResourceLoggerService>();
        await foreach (var batch in logs.WatchAsync("explore-blazor-peer").WithCancellation(cancellationToken))
            if (batch.Any(line => line.Content.Contains("data_protection_admission_rejected", StringComparison.Ordinal)))
                return;
        throw new InvalidOperationException("assurance_peer_admission_rejection_not_observed");
    }

    private static async Task RequireCurrentUserAsync(BffSession session, Guid expectedUserId, CancellationToken cancellationToken)
    {
        using var response = await session.SendAsync(HttpMethod.Get, "/api/user", null, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        await Assert.That(body.RootElement.GetProperty("id").GetGuid()).IsEqualTo(expectedUserId);
    }

    private static async Task RequireSavedCookieRejectedAsync(BffSession original, Uri address, CancellationToken cancellationToken)
    {
        using var replay = new BffSession(address);
        replay.CopyCookiesFrom(original);
        using var response = await replay.SendAsync(HttpMethod.Get, "/api/user", null, cancellationToken);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    private static async Task RequireSavedSessionUnavailableAsync(BffSession original, Uri address, CancellationToken cancellationToken)
    {
        using var replay = new BffSession(address);
        replay.CopyCookiesFrom(original);
        using var response = await replay.SendAsync(HttpMethod.Get, "/api/user", null, cancellationToken);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await Assert.That(response.Headers.Contains("Set-Cookie")).IsFalse();
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/problem+json");
        string content = await response.Content.ReadAsStringAsync(cancellationToken);
        await Assert.That(original.ContainsCookieValue(content)).IsFalse();
        await Assert.That(content.Contains("Redis", StringComparison.OrdinalIgnoreCase)).IsFalse();
        using var body = JsonDocument.Parse(content);
        await Assert.That(body.RootElement.GetProperty("title").GetString()).IsEqualTo("Browser session unavailable");
        await Assert.That(body.RootElement.TryGetProperty("detail", out _)).IsFalse();
    }

    private static async Task RequireSavedSessionRecoveredAsync(
        BffSession original, Uri address, Guid expectedUserId, CancellationToken cancellationToken)
    {
        await EnabledWorkerRuntimeTests.WaitUntilAsync(async () =>
        {
            using var replay = new BffSession(address);
            replay.CopyCookiesFrom(original);
            using var response = await replay.SendAsync(HttpMethod.Get, "/api/user", null, cancellationToken);
            if (response.StatusCode == HttpStatusCode.ServiceUnavailable) return false;
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            await Assert.That(body.RootElement.GetProperty("id").GetGuid()).IsEqualTo(expectedUserId);
            return true;
        }, cancellationToken);
    }

    private sealed class BffSession(Uri address) : IDisposable
    {
        private readonly HttpClient client = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
            { BaseAddress = address };
        private readonly CookieContainer cookies = new();
        public Uri Address => address;

        public async Task SignInAsync(OwnedAppHostFixture owner, CancellationToken cancellationToken)
        {
            using (var anonymous = await SendAsync(HttpMethod.Get, "/auth/status", null, cancellationToken))
            {
                anonymous.EnsureSuccessStatusCode();
                using var status = JsonDocument.Parse(await anonymous.Content.ReadAsStringAsync(cancellationToken));
                await Assert.That(status.RootElement.GetProperty("isAuthenticated").GetBoolean()).IsFalse();
            }
            using var response = await SendAsync(HttpMethod.Post, "/bff/auth/local/login", new
            {
                identifier = owner.BootstrapIdentifier, password = owner.BootstrapReplacementPassword
            }, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            await Assert.That(body.RootElement.TryGetProperty("token", out _)).IsFalse();
            await Assert.That(body.RootElement.TryGetProperty("redirectUrl", out _)).IsTrue();
        }

        public void CopyCookiesFrom(BffSession issuer)
        {
            foreach (Cookie cookie in issuer.cookies.GetCookies(issuer.Address))
                cookies.Add(Address, new Cookie(cookie.Name, cookie.Value, cookie.Path)
                    { Secure = cookie.Secure, HttpOnly = cookie.HttpOnly, Expires = cookie.Expires });
        }

        public bool ContainsCookieValue(string content) => cookies.GetCookies(Address).Cast<Cookie>()
            .Any(cookie => !string.IsNullOrEmpty(cookie.Value) && content.Contains(cookie.Value, StringComparison.Ordinal));

        public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body,
            CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(method, path);
            string cookieHeader = cookies.GetCookieHeader(Address);
            if (cookieHeader.Length > 0) request.Headers.Add("Cookie", cookieHeader);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
                var csrf = cookies.GetCookies(Address)["XSRF-TOKEN"]
                    ?? throw new InvalidOperationException("assurance_antiforgery_cookie_missing");
                request.Headers.Add("X-CSRF-TOKEN", Uri.UnescapeDataString(csrf.Value));
            }
            var response = await client.SendAsync(request, cancellationToken);
            if (response.Headers.TryGetValues("Set-Cookie", out var values))
                foreach (string value in values) cookies.SetCookies(Address, value);
            return response;
        }

        public void Dispose() => client.Dispose();
    }
}
