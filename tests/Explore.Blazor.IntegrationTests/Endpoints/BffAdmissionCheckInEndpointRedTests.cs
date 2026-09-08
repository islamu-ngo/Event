using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Event.Web.BffHosting.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Explore.Blazor.IntegrationTests.Endpoints;

public sealed class BffAdmissionCheckInEndpointRedTests
{
    private const string ScannerCapabilityHeader = "X-Admission-Scanner-Capability";
    private const string TenantSlugHeader = "X-Tenant-Slug";
    private const string TenantIdHeader = "X-Tenant-Id";

    [Test]
    public async Task StaffAndScannerTransportsForwardExactlyOneAuthorityWithoutExposingSecrets()
    {
        AdmissionSecrets secrets = AdmissionSecrets.Create();
        await using var upstream = await AdmissionApiHost.StartAsync();
        await using WebApplicationFactory<Program> factory = CreateFactory(
            upstream.BaseAddress,
            secrets.ServerAccessToken);
        using HttpClient staffClient = CreateClient(factory, handleCookies: true);
        using HttpClient scannerClient = CreateClient(factory, handleCookies: false);
        BrowserSession session = await StartBrowserSessionAsync(staffClient, Guid.CreateVersion7());
        using HttpRequestMessage staffRequest = CreateStaffMutation(
            session,
            StaffCheckInRoute(Guid.CreateVersion7()),
            Guid.CreateVersion7(),
            secrets.ScannedCredential);
        staffRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            secrets.BrowserAccessToken);
        staffRequest.Headers.Add(ScannerCapabilityHeader, secrets.ScannerCapability);

        using HttpResponseMessage staffResponse = await staffClient.SendAsync(staffRequest);
        ObservedRequest observedStaff = await upstream.NextRequestAsync();
        string staffBody = await staffResponse.Content.ReadAsStringAsync();

        using HttpRequestMessage scannerRequest = CreateScannerMutation(
            secrets.ScannerCapability,
            secrets.ScannedCredential);
        scannerRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            secrets.BrowserAccessToken);
        using HttpResponseMessage scannerResponse = await scannerClient.SendAsync(scannerRequest);
        ObservedRequest observedScanner = await upstream.NextRequestAsync();
        string scannerBody = await scannerResponse.Content.ReadAsStringAsync();

        await Assert.That(staffResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(BearerMatches(observedStaff.Authorization, secrets.ServerAccessToken)).IsTrue();
        await Assert.That(string.IsNullOrEmpty(observedStaff.ScannerCapability)).IsTrue();
        await Assert.That(string.IsNullOrEmpty(observedStaff.Cookie)).IsTrue();
        await Assert.That(scannerResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(observedScanner.PathAndQuery).IsEqualTo(ScannerCheckInRoute);
        await Assert.That(string.IsNullOrEmpty(observedScanner.Authorization)).IsTrue();
        await Assert.That(OpaqueValueMatches(
            observedScanner.ScannerCapability,
            secrets.ScannerCapability)).IsTrue();
        await Assert.That(string.IsNullOrEmpty(observedScanner.Cookie)).IsTrue();
        foreach (string body in new[] { staffBody, scannerBody })
        {
            await Assert.That(ContainsAnySecret(body, secrets)).IsFalse();
        }

        await AssertPrivateAdmissionResponseAsync(staffResponse);
        await AssertPrivateAdmissionResponseAsync(scannerResponse);
    }

    [Test]
    public async Task StaffBatchAndUndoStripScannerAuthorityAcrossPathBaseVariants()
    {
        AdmissionSecrets secrets = AdmissionSecrets.Create();
        await using var upstream = await AdmissionApiHost.StartAsync();
        await using WebApplicationFactory<Program> factory = CreateFactory(
            upstream.BaseAddress,
            secrets.ServerAccessToken);
        using HttpClient client = CreateClient(factory, handleCookies: true);
        BrowserSession session = await StartBrowserSessionAsync(client, Guid.CreateVersion7());
        Guid eventId = Guid.CreateVersion7();
        string[] routes =
        [
            StaffBatchRoute(eventId),
            StaffUndoRoute(eventId, Guid.CreateVersion7()),
            StaffUndoRoute(eventId, Guid.NewGuid()),
            "/t/masjid-central" + StaffBatchRoute(eventId),
            "/t/masjid-central" + StaffUndoRoute(eventId, Guid.CreateVersion7()),
            "/t/masjid-central" + StaffUndoRoute(eventId, Guid.NewGuid())
        ];

        foreach (string route in routes)
        {
            await AssertStaffVariantAsync(client, upstream, session, route, secrets);
        }
    }

    [Test]
    public async Task ScannerBatchAndUndoStripStaffSessionAuthorityAcrossPathBaseVariants()
    {
        AdmissionSecrets secrets = AdmissionSecrets.Create();
        await using var upstream = await AdmissionApiHost.StartAsync();
        await using WebApplicationFactory<Program> factory = CreateFactory(
            upstream.BaseAddress,
            secrets.ServerAccessToken);
        using HttpClient client = CreateClient(factory, handleCookies: false);
        string authentication = TestAuthHandler.CreateAuthHeaderValue(
            Guid.CreateVersion7(),
            "Scanner route session must be ignored");
        string[] routes =
        [
            ScannerBatchRoute,
            ScannerUndoRoute(Guid.CreateVersion7()),
            ScannerUndoRoute(Guid.NewGuid()),
            "/t/masjid-central" + ScannerBatchRoute,
            "/t/masjid-central" + ScannerUndoRoute(Guid.CreateVersion7()),
            "/t/masjid-central" + ScannerUndoRoute(Guid.NewGuid())
        ];

        foreach (string route in routes)
        {
            await AssertScannerVariantAsync(client, upstream, authentication, route, secrets);
        }
    }

    [Test]
    public async Task StaffReadRoutesUseOnlyStaffAuthorityAcrossPathBaseVariants()
    {
        AdmissionSecrets secrets = AdmissionSecrets.Create();
        await using var upstream = await AdmissionApiHost.StartAsync();
        await using WebApplicationFactory<Program> factory = CreateFactory(
            upstream.BaseAddress,
            secrets.ServerAccessToken);
        using HttpClient client = CreateClient(factory, handleCookies: true);
        BrowserSession session = await StartBrowserSessionAsync(client, Guid.CreateVersion7());
        Guid eventId = Guid.CreateVersion7();
        string[] routes =
        [
            StaffDetailRoute(eventId, Guid.CreateVersion7()),
            StaffSummaryRoute(eventId),
            StaffAuditRoute(eventId),
            StaffHealthRoute(eventId),
            StaffOperationRoute(eventId, "stop"),
            StaffOperationRoute(eventId, "restore"),
            StaffOperationRoute(eventId, "reconcile"),
            "/t/masjid-central" + StaffDetailRoute(eventId, Guid.NewGuid()),
            "/t/masjid-central" + StaffSummaryRoute(eventId),
            "/t/masjid-central" + StaffAuditRoute(eventId),
            "/t/masjid-central" + StaffHealthRoute(eventId),
            "/t/masjid-central" + StaffOperationRoute(eventId, "stop"),
            "/t/masjid-central" + StaffOperationRoute(eventId, "restore"),
            "/t/masjid-central" + StaffOperationRoute(eventId, "reconcile")
        ];

        foreach (string route in routes)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, route);
            request.Headers.Add(TestAuthHandler.AuthHeaderName, session.Authentication);
            request.Headers.Add(ScannerCapabilityHeader, secrets.ScannerCapability);
            using HttpResponseMessage response = await client.SendAsync(request);
            ObservedRequest observed = await upstream.NextRequestAsync();

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(BearerMatches(observed.Authorization, secrets.ServerAccessToken)).IsTrue();
            await Assert.That(string.IsNullOrEmpty(observed.ScannerCapability)).IsTrue();
            await Assert.That(observed.PathAndQuery)
                .IsEqualTo(route.Replace("/t/masjid-central", string.Empty, StringComparison.Ordinal));
            await AssertPrivateAdmissionResponseAsync(response);
        }
    }

    [Test]
    public async Task StaffScannerCapabilityManagementRoutesNeverAcceptScannerAuthority()
    {
        AdmissionSecrets secrets = AdmissionSecrets.Create();
        await using var upstream = await AdmissionApiHost.StartAsync();
        await using WebApplicationFactory<Program> factory = CreateFactory(
            upstream.BaseAddress,
            secrets.ServerAccessToken);
        using HttpClient client = CreateClient(factory, handleCookies: true);
        BrowserSession session = await StartBrowserSessionAsync(client, Guid.CreateVersion7());
        Guid eventId = Guid.CreateVersion7();
        (HttpMethod Method, string Route)[] routes =
        [
            (HttpMethod.Get, ScannerCapabilityManagementRoute(eventId)),
            (HttpMethod.Post, ScannerCapabilityManagementRoute(eventId)),
            (HttpMethod.Delete,
                ScannerCapabilityManagementRoute(eventId) + "/" + Guid.NewGuid().ToString("D"))
        ];

        foreach ((HttpMethod method, string route) in routes)
        {
            await AssertStaffCapabilityManagementVariantAsync(
                client,
                upstream,
                session,
                method,
                route,
                secrets);
        }
    }

    [Test]
    public async Task UnrelatedAdmissionPrefixesAndTrailingJunkRemainOutsideSensitiveTransport()
    {
        AdmissionSecrets secrets = AdmissionSecrets.Create();
        await using var upstream = await AdmissionApiHost.StartAsync();
        await using WebApplicationFactory<Program> factory = CreateFactory(
            upstream.BaseAddress,
            secrets.ServerAccessToken);
        using HttpClient client = CreateClient(factory, handleCookies: false);
        Guid eventId = Guid.CreateVersion7();
        string[] routes =
        [
            StaffCheckInRoute(eventId) + "/batch/junk",
            StaffBatchRoute(eventId) + "/",
            StaffUndoRoute(eventId, Guid.CreateVersion7()) + "/junk",
            StaffDetailRoute(eventId, Guid.CreateVersion7()) + "/junk",
            StaffSummaryRoute(eventId) + "/junk",
            StaffAuditRoute(eventId) + "/junk",
            StaffHealthRoute(eventId) + "/junk",
            StaffOperationRoute(eventId, "stop") + "/junk",
            StaffOperationRoute(eventId, "unknown"),
            ScannerCheckInRoute + "-other",
            ScannerBatchRoute + "/junk",
            ScannerBatchRoute + "/",
            ScannerUndoRoute(Guid.CreateVersion7()) + "/junk",
            ScannerUndoRoute(Guid.NewGuid()) + "/junk"
        ];

        foreach (string route in routes)
        {
            await AssertUnrelatedVariantAsync(client, upstream, route, secrets);
        }
    }

    [Test]
    public async Task StaffMutationsRequireAntiforgeryBeforeAnyCapabilityReachesTheApi()
    {
        AdmissionSecrets secrets = AdmissionSecrets.Create();
        await using var upstream = await AdmissionApiHost.StartAsync();
        await using WebApplicationFactory<Program> factory = CreateFactory(
            upstream.BaseAddress,
            secrets.ServerAccessToken);
        using HttpClient client = CreateClient(factory, handleCookies: true);
        BrowserSession session = await StartBrowserSessionAsync(client, Guid.CreateVersion7());
        string route = StaffCheckInRoute(Guid.CreateVersion7());
        Guid targetId = Guid.CreateVersion7();
        using var missingToken = new HttpRequestMessage(HttpMethod.Post, route);
        missingToken.Headers.Add(TestAuthHandler.AuthHeaderName, session.Authentication);
        missingToken.Content = JsonContent.Create(new { targetId, credential = secrets.ScannedCredential });

        using HttpResponseMessage rejected = await client.SendAsync(missingToken);

        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(upstream.RequestCount).IsEqualTo(0);

        using HttpRequestMessage valid = CreateStaffMutation(
            session,
            route,
            targetId,
            secrets.ScannedCredential);
        using HttpResponseMessage accepted = await client.SendAsync(valid);
        ObservedRequest observed = await upstream.NextRequestAsync();

        await Assert.That(accepted.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(BearerMatches(observed.Authorization, secrets.ServerAccessToken)).IsTrue();
        await Assert.That(string.IsNullOrEmpty(observed.ScannerCapability)).IsTrue();
        await Assert.That(upstream.RequestCount).IsEqualTo(1);
    }

    [Test]
    public async Task TenantPathBasePreservesExactApiRouteAndStripsBrowserAuthorizationAndTenantHeaders()
    {
        AdmissionSecrets secrets = AdmissionSecrets.Create();
        await using var upstream = await AdmissionApiHost.StartAsync();
        await using WebApplicationFactory<Program> factory = CreateFactory(
            upstream.BaseAddress,
            secrets.ServerAccessToken);
        using HttpClient client = CreateClient(factory, handleCookies: true);
        BrowserSession session = await StartBrowserSessionAsync(client, Guid.CreateVersion7());
        Guid eventId = Guid.CreateVersion7();
        Guid targetId = Guid.CreateVersion7();
        string apiRoute = StaffCheckInRoute(eventId) + "?mode=hid&sequence=17";
        using HttpRequestMessage request = CreateStaffMutation(
            session,
            "/t/masjid-central" + apiRoute,
            targetId,
            secrets.ScannedCredential);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            secrets.BrowserAccessToken);
        request.Headers.Add(ScannerCapabilityHeader, secrets.ScannerCapability);
        request.Headers.Add(TenantSlugHeader, "browser-tenant");
        request.Headers.Add(TenantIdHeader, Guid.Empty.ToString("D"));

        using HttpResponseMessage response = await client.SendAsync(request);
        ObservedRequest observed = await upstream.NextRequestAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(observed.PathAndQuery).IsEqualTo(apiRoute);
        await Assert.That(BearerMatches(observed.Authorization, secrets.ServerAccessToken)).IsTrue();
        await Assert.That(string.IsNullOrEmpty(observed.ScannerCapability)).IsTrue();
        await Assert.That(observed.TenantSlug).IsEqualTo("masjid-central");
        await Assert.That(string.IsNullOrEmpty(observed.TenantId)).IsTrue();
        await AssertPrivateAdmissionResponseAsync(response);
    }

    [Test]
    public async Task UpstreamOutageFailsClosedExplicitlyWithoutOfflineValidationOrSilentQueue()
    {
        AdmissionSecrets secrets = AdmissionSecrets.Create();
        await using (var validationUpstream = await AdmissionApiHost.StartAsync(
                         StatusCodes.Status400BadRequest,
                         "{\"code\":\"validation_failed\"}"))
        await using (WebApplicationFactory<Program> validationFactory = CreateFactory(
                         validationUpstream.BaseAddress,
                         secrets.ServerAccessToken))
        using (HttpClient validationClient = CreateClient(validationFactory, handleCookies: false))
        using (HttpRequestMessage validationRequest = CreateScannerMutation(
                   secrets.ScannerCapability,
                   secrets.ScannedCredential))
        using (HttpResponseMessage validationResponse = await validationClient.SendAsync(validationRequest))
        {
            string validationBody = await validationResponse.Content.ReadAsStringAsync();
            await Assert.That(validationResponse.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
            await Assert.That(validationBody).Contains("validation_failed");
            await Assert.That(validationBody).DoesNotContain("admission_upstream_unavailable");
            await AssertPrivateAdmissionResponseAsync(validationResponse);
        }

        await using WebApplicationFactory<Program> factory = CreateFactory(
            "http://127.0.0.1:1",
            secrets.ServerAccessToken);
        using HttpClient client = CreateClient(factory, handleCookies: false);
        using HttpRequestMessage request = CreateScannerMutation(
            secrets.ScannerCapability,
            secrets.ScannedCredential);

        using HttpResponseMessage response = await client.SendAsync(request);
        string body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await Assert.That(response.Content.Headers.ContentType?.MediaType)
            .IsEqualTo("application/problem+json");
        await Assert.That(body).Contains("admission_upstream_unavailable");
        await Assert.That(body).DoesNotContain("checked_in");
        await Assert.That(body).DoesNotContain("accepted");
        await Assert.That(body).DoesNotContain("queued");
        await Assert.That(body).DoesNotContain("offline");
        await AssertPrivateAdmissionResponseAsync(response);

        await using WebApplicationFactory<Program> variantFactory = CreateFactory(
            "http://127.0.0.1:1",
            secrets.ServerAccessToken);
        using HttpClient staffClient = CreateClient(variantFactory, handleCookies: true);
        using HttpClient scannerClient = CreateClient(variantFactory, handleCookies: false);
        BrowserSession session = await StartBrowserSessionAsync(staffClient, Guid.CreateVersion7());
        string[] staffOutageRoutes =
        [
            StaffBatchRoute(Guid.CreateVersion7()),
            "/t/masjid-central" + StaffUndoRoute(Guid.CreateVersion7(), Guid.CreateVersion7())
        ];
        foreach (string route in staffOutageRoutes)
        {
            using HttpRequestMessage outageRequest = CreateStaffMutation(
                session,
                route,
                Guid.CreateVersion7(),
                secrets.ScannedCredential);
            using HttpResponseMessage outageResponse = await staffClient.SendAsync(outageRequest);
            await AssertExplicitAdmissionOutageAsync(outageResponse);
        }

        using (var readOutageRequest = new HttpRequestMessage(
                   HttpMethod.Get,
                   "/t/masjid-central" + StaffSummaryRoute(Guid.CreateVersion7())))
        {
            readOutageRequest.Headers.Add(TestAuthHandler.AuthHeaderName, session.Authentication);
            readOutageRequest.Headers.Add(ScannerCapabilityHeader, secrets.ScannerCapability);
            using HttpResponseMessage readOutageResponse = await staffClient.SendAsync(readOutageRequest);
            await AssertExplicitAdmissionOutageAsync(readOutageResponse);
        }

        string[] scannerOutageRoutes =
        [
            ScannerBatchRoute,
            "/t/masjid-central" + ScannerUndoRoute(Guid.CreateVersion7())
        ];
        foreach (string route in scannerOutageRoutes)
        {
            using HttpRequestMessage outageRequest = CreateScannerMutation(
                route,
                secrets.ScannerCapability,
                secrets.ScannedCredential);
            using HttpResponseMessage outageResponse = await scannerClient.SendAsync(outageRequest);
            await AssertExplicitAdmissionOutageAsync(outageResponse);
        }
    }

    [Test]
    public async Task ScannerCapabilityIsNotRetainedAcrossRouteOrAuthenticatedSessionChanges()
    {
        AdmissionSecrets secrets = AdmissionSecrets.Create();
        await using var upstream = await AdmissionApiHost.StartAsync();
        await using WebApplicationFactory<Program> factory = CreateFactory(
            upstream.BaseAddress,
            secrets.ServerAccessToken);
        using HttpClient staffClient = CreateClient(factory, handleCookies: true);
        using HttpClient firstScannerClient = CreateClient(factory, handleCookies: false);
        using HttpClient replacementScannerClient = CreateClient(factory, handleCookies: false);
        BrowserSession staffSession = await StartBrowserSessionAsync(staffClient, Guid.CreateVersion7());

        using (HttpRequestMessage first = CreateScannerMutation(
                   secrets.ScannerCapability,
                   secrets.ScannedCredential))
        using (HttpResponseMessage response = await firstScannerClient.SendAsync(first))
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }

        ObservedRequest initial = await upstream.NextRequestAsync();

        using (HttpRequestMessage routeChanged = CreateStaffMutation(
                   staffSession,
                   StaffCheckInRoute(Guid.CreateVersion7()),
                   Guid.CreateVersion7(),
                   secrets.ScannedCredential))
        using (HttpResponseMessage response = await staffClient.SendAsync(routeChanged))
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }

        ObservedRequest afterRouteChange = await upstream.NextRequestAsync();
        using HttpRequestMessage sessionChanged = CreateScannerMutation(
            capability: null,
            secrets.ScannedCredential);
        sessionChanged.Headers.Add(
            TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(Guid.CreateVersion7(), "Replacement scanner session"));
        using (HttpResponseMessage response = await replacementScannerClient.SendAsync(sessionChanged))
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }

        ObservedRequest afterSessionChange = await upstream.NextRequestAsync();

        await Assert.That(string.IsNullOrEmpty(initial.Authorization)).IsTrue();
        await Assert.That(OpaqueValueMatches(
            initial.ScannerCapability,
            secrets.ScannerCapability)).IsTrue();
        await Assert.That(BearerMatches(
            afterRouteChange.Authorization,
            secrets.ServerAccessToken)).IsTrue();
        await Assert.That(string.IsNullOrEmpty(afterRouteChange.ScannerCapability)).IsTrue();
        await Assert.That(string.IsNullOrEmpty(afterSessionChange.Authorization)).IsTrue();
        await Assert.That(string.IsNullOrEmpty(afterSessionChange.ScannerCapability)).IsTrue();
        await Assert.That(upstream.RequestCount).IsEqualTo(3);
    }

    private static async Task AssertStaffVariantAsync(
        HttpClient client,
        AdmissionApiHost upstream,
        BrowserSession session,
        string route,
        AdmissionSecrets secrets)
    {
        using HttpRequestMessage request = CreateStaffMutation(
            session,
            route,
            Guid.CreateVersion7(),
            secrets.ScannedCredential);
        request.Headers.Add(ScannerCapabilityHeader, secrets.ScannerCapability);
        using HttpResponseMessage response = await client.SendAsync(request);
        ObservedRequest observed = await upstream.NextRequestAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(BearerMatches(observed.Authorization, secrets.ServerAccessToken)).IsTrue();
        await Assert.That(string.IsNullOrEmpty(observed.ScannerCapability)).IsTrue();
        await Assert.That(observed.PathAndQuery)
            .IsEqualTo(route.Replace("/t/masjid-central", string.Empty, StringComparison.Ordinal));
        await AssertPrivateAdmissionResponseAsync(response);
    }

    private static async Task AssertScannerVariantAsync(
        HttpClient client,
        AdmissionApiHost upstream,
        string authentication,
        string route,
        AdmissionSecrets secrets)
    {
        using HttpRequestMessage request = CreateScannerMutation(
            route,
            secrets.ScannerCapability,
            secrets.ScannedCredential);
        request.Headers.Add(TestAuthHandler.AuthHeaderName, authentication);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            secrets.BrowserAccessToken);
        request.Headers.Add("Cookie", $"session={Guid.CreateVersion7():N}");
        using HttpResponseMessage response = await client.SendAsync(request);
        ObservedRequest observed = await upstream.NextRequestAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(string.IsNullOrEmpty(observed.Authorization)).IsTrue();
        await Assert.That(string.IsNullOrEmpty(observed.Cookie)).IsTrue();
        await Assert.That(OpaqueValueMatches(
            observed.ScannerCapability,
            secrets.ScannerCapability)).IsTrue();
        await Assert.That(observed.PathAndQuery)
            .IsEqualTo(route.Replace("/t/masjid-central", string.Empty, StringComparison.Ordinal));
        await AssertPrivateAdmissionResponseAsync(response);
    }

    private static async Task AssertStaffCapabilityManagementVariantAsync(
        HttpClient client,
        AdmissionApiHost upstream,
        BrowserSession session,
        HttpMethod method,
        string route,
        AdmissionSecrets secrets)
    {
        using HttpRequestMessage request = CreateStaffMutation(
            session,
            route,
            Guid.CreateVersion7(),
            secrets.ScannedCredential);
        request.Method = method;
        request.Headers.Add(ScannerCapabilityHeader, secrets.ScannerCapability);
        using HttpResponseMessage response = await client.SendAsync(request);
        ObservedRequest observed = await upstream.NextRequestAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(BearerMatches(observed.Authorization, secrets.ServerAccessToken)).IsTrue();
        await Assert.That(string.IsNullOrEmpty(observed.ScannerCapability)).IsTrue();
        await AssertPrivateAdmissionResponseAsync(response);
    }

    private static async Task AssertUnrelatedVariantAsync(
        HttpClient client,
        AdmissionApiHost upstream,
        string route,
        AdmissionSecrets secrets)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, route)
        {
            Content = JsonContent.Create(new { credential = secrets.ScannedCredential })
        };
        request.Headers.Add(TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(Guid.CreateVersion7()));
        request.Headers.Add(ScannerCapabilityHeader, secrets.ScannerCapability);
        using HttpResponseMessage response = await client.SendAsync(request);
        ObservedRequest observed = await upstream.NextRequestAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(BearerMatches(observed.Authorization, secrets.ServerAccessToken)).IsTrue();
        await Assert.That(OpaqueValueMatches(
            observed.ScannerCapability,
            secrets.ScannerCapability)).IsTrue();
        await Assert.That(response.Headers.CacheControl?.NoStore == true).IsFalse();
        await Assert.That(response.Headers.GetValues("Referrer-Policy").Single())
            .IsEqualTo("strict-origin-when-cross-origin");
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string apiBaseAddress,
        string serverAccessToken) =>
        new AdmissionBffFactory(apiBaseAddress, serverAccessToken);

    private static HttpClient CreateClient(
        WebApplicationFactory<Program> factory,
        bool handleCookies) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = handleCookies
        });

    private static async Task<BrowserSession> StartBrowserSessionAsync(HttpClient client, Guid userId)
    {
        string authentication = TestAuthHandler.CreateAuthHeaderValue(userId, "Admission scanner staff");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/auth/status");
        request.Headers.Add(TestAuthHandler.AuthHeaderName, authentication);
        using HttpResponseMessage response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.TryGetValues("Set-Cookie", out var values)).IsTrue();
        string token = values!
            .Select(ReadXsrfToken)
            .First(value => !string.IsNullOrWhiteSpace(value))!;
        return new BrowserSession(authentication, token);
    }

    private static HttpRequestMessage CreateStaffMutation(
        BrowserSession session,
        string route,
        Guid targetId,
        string scannedCredential)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, route)
        {
            Content = JsonContent.Create(new { targetId, credential = scannedCredential })
        };
        request.Headers.Add(TestAuthHandler.AuthHeaderName, session.Authentication);
        request.Headers.Add("X-CSRF-TOKEN", session.AntiforgeryToken);
        return request;
    }

    private static HttpRequestMessage CreateScannerMutation(
        string? capability,
        string scannedCredential) =>
        CreateScannerMutation(ScannerCheckInRoute, capability, scannedCredential);

    private static HttpRequestMessage CreateScannerMutation(
        string route,
        string? capability,
        string scannedCredential)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, route)
        {
            Content = JsonContent.Create(new { credential = scannedCredential })
        };
        if (capability is not null)
        {
            request.Headers.Add(ScannerCapabilityHeader, capability);
        }

        return request;
    }

    private static string? ReadXsrfToken(string setCookie)
    {
        const string prefix = "XSRF-TOKEN=";
        if (!setCookie.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        int end = setCookie.IndexOf(';', prefix.Length);
        string value = end < 0 ? setCookie[prefix.Length..] : setCookie[prefix.Length..end];
        return Uri.UnescapeDataString(value);
    }

    private static bool BearerMatches(string? authorization, string expectedToken) =>
        OpaqueValueMatches(authorization, $"Bearer {expectedToken}");

    private static bool OpaqueValueMatches(string? actual, string expected)
    {
        if (actual is null)
        {
            return false;
        }

        byte[] actualBytes = Encoding.UTF8.GetBytes(actual);
        byte[] expectedBytes = Encoding.UTF8.GetBytes(expected);
        return actualBytes.Length == expectedBytes.Length &&
            CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
    }

    private static bool ContainsAnySecret(string body, AdmissionSecrets secrets) =>
        body.Contains(secrets.ServerAccessToken, StringComparison.Ordinal) ||
        body.Contains(secrets.BrowserAccessToken, StringComparison.Ordinal) ||
        body.Contains(secrets.ScannerCapability, StringComparison.Ordinal) ||
        body.Contains(secrets.ScannedCredential, StringComparison.Ordinal);

    private static async Task AssertExplicitAdmissionOutageAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync();
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await Assert.That(response.Content.Headers.ContentType?.MediaType)
            .IsEqualTo("application/problem+json");
        await Assert.That(body).Contains("admission_upstream_unavailable");
        await AssertPrivateAdmissionResponseAsync(response);
    }

    private static async Task AssertPrivateAdmissionResponseAsync(HttpResponseMessage response)
    {
        await Assert.That(response.Headers.CacheControl?.Private).IsTrue();
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(response.Headers.Pragma.Any(value =>
            string.Equals(value.Name, "no-cache", StringComparison.OrdinalIgnoreCase))).IsTrue();
        await Assert.That(response.Headers.GetValues("Referrer-Policy").Single())
            .IsEqualTo("no-referrer");
    }

    private static string StaffCheckInRoute(Guid eventId) =>
        $"/api/events/{eventId:D}/admission/check-ins";

    private static string StaffBatchRoute(Guid eventId) =>
        StaffCheckInRoute(eventId) + "/batch";

    private static string StaffDetailRoute(Guid eventId, Guid checkInId) =>
        StaffCheckInRoute(eventId) + $"/{checkInId:D}";

    private static string StaffSummaryRoute(Guid eventId) =>
        StaffCheckInRoute(eventId) + "/summary";

    private static string StaffAuditRoute(Guid eventId) =>
        StaffCheckInRoute(eventId) + "/audit";

    private static string StaffHealthRoute(Guid eventId) =>
        StaffCheckInRoute(eventId) + "/health";

    private static string StaffOperationRoute(Guid eventId, string action) =>
        StaffCheckInRoute(eventId) + $"/operations/{action}";

    private static string StaffUndoRoute(Guid eventId, Guid checkInId) =>
        StaffCheckInRoute(eventId) + $"/{checkInId:D}/undo";

    private static string ScannerUndoRoute(Guid checkInId) =>
        ScannerCheckInRoute + $"/{checkInId:D}/undo";

    private static string ScannerCapabilityManagementRoute(Guid eventId) =>
        $"/api/events/{eventId:D}/admission/scanner-capabilities";

    private const string ScannerCheckInRoute = "/api/admission/scanner/check-ins";
    private const string ScannerBatchRoute = ScannerCheckInRoute + "/batch";

    private sealed record BrowserSession(string Authentication, string AntiforgeryToken);

    private sealed record AdmissionSecrets(
        string ServerAccessToken,
        string BrowserAccessToken,
        string ScannerCapability,
        string ScannedCredential)
    {
        public static AdmissionSecrets Create() => new(
            CreateOpaqueValue(),
            CreateOpaqueValue(),
            CreateOpaqueValue(),
            CreateOpaqueValue());

        private static string CreateOpaqueValue() =>
            Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    }

    private sealed class AdmissionBffFactory(
        string apiBaseAddress,
        string serverAccessToken) : BlazorBffWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ExploreApi:BaseUrl", apiBaseAddress);
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEventBffAccessTokenProvider>();
                services.AddSingleton<IEventBffAccessTokenProvider>(
                    new FixedAccessTokenProvider(serverAccessToken));
            });
        }
    }

    private sealed class FixedAccessTokenProvider(string accessToken) : IEventBffAccessTokenProvider
    {
        public ValueTask<string?> ResolveAccessTokenAsync(
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>(httpContext.User.Identity?.IsAuthenticated == true
                ? accessToken
                : null);
    }

    private sealed record ObservedRequest(
        string PathAndQuery,
        string? Authorization,
        string? ScannerCapability,
        string? TenantSlug,
        string? TenantId,
        string? Cookie);

    private sealed class AdmissionApiHost(
        int responseStatusCode,
        string responseBody) : IAsyncDisposable
    {
        private readonly Channel<ObservedRequest> requests = Channel.CreateUnbounded<ObservedRequest>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        private WebApplication? app;
        private int requestCount;

        public string BaseAddress { get; private set; } = string.Empty;
        public int RequestCount => Volatile.Read(ref requestCount);

        public static async Task<AdmissionApiHost> StartAsync(
            int responseStatusCode = StatusCodes.Status200OK,
            string responseBody = "{\"outcome\":\"checked_in\"}")
        {
            var host = new AdmissionApiHost(responseStatusCode, responseBody);
            await host.StartCoreAsync();
            return host;
        }

        public async Task<ObservedRequest> NextRequestAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            return await requests.Reader.ReadAsync(timeout.Token);
        }

        public async ValueTask DisposeAsync()
        {
            requests.Writer.TryComplete();
            if (app is not null)
            {
                await app.DisposeAsync();
            }
        }

        private async Task StartCoreAsync()
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder();
            builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
            app = builder.Build();
            app.Map("/{**path}", CaptureAsync);
            await app.StartAsync();
            BaseAddress = app.Services
                .GetRequiredService<IServer>()
                .Features
                .Get<IServerAddressesFeature>()!
                .Addresses
                .Single();
        }

        private async Task CaptureAsync(HttpContext context)
        {
            Interlocked.Increment(ref requestCount);
            await requests.Writer.WriteAsync(new ObservedRequest(
                $"{context.Request.Path}{context.Request.QueryString}",
                context.Request.Headers.Authorization.FirstOrDefault(),
                context.Request.Headers[ScannerCapabilityHeader].FirstOrDefault(),
                context.Request.Headers[TenantSlugHeader].FirstOrDefault(),
                context.Request.Headers[TenantIdHeader].FirstOrDefault(),
                context.Request.Headers.Cookie.FirstOrDefault()), context.RequestAborted);
            context.Response.StatusCode = responseStatusCode;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(responseBody, context.RequestAborted);
        }
    }
}
