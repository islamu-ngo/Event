using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Extensions;
using Explore.Blazor.Models;
using Explore.Blazor.Services;
using Explore.Blazor.Services.Auth;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace Explore.Blazor.IntegrationTests;

public sealed class LocalVisitorEnrollmentFlowTests
{
    private static CancellationToken Cancellation => TestContext.Current!.Execution.CancellationToken;
    private static string Secret() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    [Test]
    public async Task OriginalEnrollmentRemainsUsableSixMinutesIntoItsFifteenMinuteDeadline()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var start = await fixture.StartAsync();
        await Assert.That(start.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var original = await fixture.ReadFlowAsync();
        fixture.Clock.Advance(TimeSpan.FromMinutes(6));
        using var response = await fixture.RegisterAsync();
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(original!.ExpiresAt - fixture.Clock.GetUtcNow()).IsEqualTo(TimeSpan.FromMinutes(9));
    }

    [Test]
    public async Task NativeMaximumUnicodeScalarCredentialIsNotRejectedAsUtf16Length()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var start = await fixture.StartAsync();
        await Assert.That(start.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var response = await fixture.PostAsync("/register", new
        {
            username = fixture.Username,
            password = string.Concat(Enumerable.Repeat("\U0001F600", 128))
        });
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(fixture.Transport.Registrations).IsEqualTo(1);
    }

    [Test]
    [Arguments("/start")]
    [Arguments("/register")]
    public async Task AntiforgeryFailureDoesNotReachEnrollmentAndRemainsPrivate(string suffix)
    {
        await using var fixture = await Fixture.CreateAsync();
        using var response = await fixture.PostAsync(suffix, new { }, csrf: false);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(fixture.Transport.Reserves + fixture.Transport.Registrations).IsEqualTo(0);
        await Assert.That(response.Headers.CacheControl?.NoStore == true && response.Headers.CacheControl.Private).IsTrue();
        await Assert.That(response.Headers.GetValues("Referrer-Policy").Single()).IsEqualTo("no-referrer");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RegistrationRequiresOriginalProtectedCorrelationCookie(bool wrongCookie)
    {
        await using var fixture = await Fixture.CreateAsync();
        if (wrongCookie)
        {
            using var start = await fixture.StartAsync();
            fixture.SetFlowCookie(Secret());
        }
        using var response = await fixture.RegisterAsync();
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(fixture.Transport.Registrations).IsEqualTo(0);
    }

    [Test]
    [Arguments("tenant")]
    [Arguments("host")]
    [Arguments("path")]
    [Arguments("origin")]
    [Arguments("expiry")]
    public async Task OriginalTargetOriginPathAndDeadlineCannotBeRebound(string changed)
    {
        await using var fixture = await Fixture.CreateAsync();
        using var start = await fixture.StartAsync();
        await Assert.That(start.StatusCode).IsEqualTo(HttpStatusCode.OK);
        switch (changed)
        {
            case "tenant": fixture.Tenant = "bravo"; break;
            case "host": fixture.Host = "changed.example.test"; break;
            case "path": fixture.PathBase = "/bravo"; break;
            case "origin": fixture.Origin = "https://other.example.test"; break;
            case "expiry": fixture.Clock.Advance(TimeSpan.FromMinutes(16)); break;
        }
        using var response = await fixture.RegisterAsync();
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(fixture.Transport.Registrations).IsEqualTo(0);
        await Assert.That(fixture.FlowCookie).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task TargetChangesDuringAwaitedApiCallCannotExposeSuccess(bool duringRegistration)
    {
        await using var fixture = await Fixture.CreateAsync();
        if (duringRegistration)
        {
            using var start = await fixture.StartAsync();
            await Assert.That(start.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
        fixture.Transport.AfterMutation = () => fixture.Tenant = "bravo";
        using var response = duringRegistration ? await fixture.RegisterAsync() : await fixture.StartAsync();
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(fixture.FlowCookie).IsNull();
        string result = await response.Content.ReadAsStringAsync(Cancellation);
        await Assert.That(result.Contains("account_created", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task LostReserveResponseReusesOriginalProofOperationAndDeadlineOnlyOnExplicitRequest()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Transport.LoseNextResponse = true;
        using var uncertain = await fixture.StartAsync();
        string? cookie = fixture.FlowCookie;
        string? proof = fixture.Transport.Proof;
        Guid operation = fixture.Transport.Operation;
        await Assert.That(uncertain.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await Assert.That(fixture.Transport.Reserves).IsEqualTo(1);
        var before = await fixture.ReadFlowAsync();
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));

        using var reconciled = await fixture.StartAsync();
        var after = await fixture.ReadFlowAsync();
        await Assert.That(reconciled.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(fixture.Transport.Reserves).IsEqualTo(2);
        await Assert.That(fixture.Transport.Proof == proof && fixture.Transport.Operation == operation).IsTrue();
        await Assert.That(fixture.FlowCookie == cookie && before!.ExpiresAt == after!.ExpiresAt).IsTrue();
    }

    [Test]
    public async Task LostRegistrationResponseRetainsOriginalClaimWithoutCachingPasswordOrAutomaticReplay()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var start = await fixture.StartAsync();
        var before = await fixture.ReadFlowAsync();
        fixture.Transport.LoseNextResponse = true;
        using var uncertain = await fixture.RegisterAsync();
        var retained = await fixture.ReadFlowAsync();
        await Assert.That(uncertain.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await Assert.That(fixture.Transport.Registrations).IsEqualTo(1);
        await Assert.That(retained == before).IsTrue();
        using var reconciled = await fixture.RegisterAsync();
        await Assert.That(reconciled.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(fixture.Transport.Registrations).IsEqualTo(2);
        await Assert.That(fixture.Transport.ReconciliationKeptOriginalAuthority).IsTrue();
        await Assert.That(await fixture.ReadFlowAsync()).IsNull();
    }

    [Test]
    public async Task SuccessExposesOnlyAccountCreatedAndLocalLoginWithoutSessionOrAuthority()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var start = await fixture.StartAsync();
        string? protectedCookie = fixture.FlowCookie;
        var flow = await fixture.ReadFlowAsync();
        await Assert.That(flow is not null && flow.OperationId.Version == 7
            && flow.Proof.Length == 64 && flow.Proof.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f')).IsTrue();
        var cookie = start.Headers.GetValues("Set-Cookie").Select(value => SetCookieHeaderValue.Parse(value))
            .Single(value => value.Name == LocalVisitorEnrollmentFlowStore.CookieName);
        await Assert.That(cookie.Secure && cookie.HttpOnly && !cookie.Domain.HasValue && cookie.Path == "/").IsTrue();
        await Assert.That(protectedCookie!.Contains(flow!.Proof, StringComparison.Ordinal)
            || protectedCookie.Contains(flow.EnrollmentIntent!, StringComparison.Ordinal)).IsFalse();
        using var response = await fixture.RegisterAsync();
        string result = await response.Content.ReadAsStringAsync(Cancellation);
        using var browser = JsonDocument.Parse(result);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(browser.RootElement.GetProperty("status").GetString()).IsEqualTo("account_created");
        await Assert.That(browser.RootElement.GetProperty("redirectUrl").GetString())
            .IsEqualTo("/alpha/login?returnUrl=%2Falpha%2Fevents");
        await Assert.That(browser.RootElement.EnumerateObject().Count()).IsEqualTo(2);
        await Assert.That(result.Contains(flow.Proof, StringComparison.Ordinal)
            || result.Contains(flow.EnrollmentIntent!, StringComparison.Ordinal)
            || result.Contains(fixture.Password, StringComparison.Ordinal)
            || result.Contains(flow.OperationId.ToString(), StringComparison.Ordinal)).IsFalse();
        await Assert.That(fixture.Transport.UntrustedHeaders).IsFalse();
        await Assert.That(fixture.Transport.TargetWasPinned).IsTrue();
        await Assert.That(fixture.FlowCookie).IsNull();
        using var replay = await fixture.RegisterAsync();
        await Assert.That(replay.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(fixture.Transport.Registrations).IsEqualTo(1);
    }

    [Test]
    [Arguments(403)]
    [Arguments(409)]
    [Arguments(503)]
    public async Task TerminalFailureClearsAuthorityAndServiceUnavailablePreservesOriginals(int status)
    {
        await using var fixture = await Fixture.CreateAsync();
        using var start = await fixture.StartAsync();
        var original = await fixture.ReadFlowAsync();
        fixture.Transport.Status = (HttpStatusCode)status;
        using var response = await fixture.RegisterAsync();
        await Assert.That((int)response.StatusCode).IsEqualTo(status);
        var remaining = await fixture.ReadFlowAsync();
        await Assert.That(status == 503 ? remaining == original : remaining is null).IsTrue();
    }

    [Test]
    [Arguments("proof")]
    [Arguments("enrollmentIntent")]
    [Arguments("tenant")]
    [Arguments("operationId")]
    public async Task BrowserCannotSupplyAuthorityToRegistration(string member)
    {
        await using var fixture = await Fixture.CreateAsync();
        using var start = await fixture.StartAsync();
        using var response = await fixture.PostAsync("/register", new Dictionary<string, string>
        {
            ["username"] = fixture.Username, ["password"] = fixture.Password, [member] = Secret()
        });
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(fixture.Transport.Registrations).IsEqualTo(0);
    }

    [Test]
    [Arguments("//other.example.test")]
    [Arguments("/\\other.example.test")]
    [Arguments("/%2fother.example.test")]
    [Arguments("https://other.example.test")]
    public async Task NonlocalOrEncodedReturnPathsNeverReserve(string returnUrl)
    {
        await using var fixture = await Fixture.CreateAsync();
        using var response = await fixture.PostAsync("/start", new { returnUrl });
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(fixture.Transport.Reserves).IsEqualTo(0);
    }

    [Test]
    public async Task OversizedBodyIsRejectedBeforeDownstreamMutation()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var response = await fixture.PostAsync("/start", new { returnUrl = new string('a', 17000) });
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.RequestEntityTooLarge);
        await Assert.That(fixture.Transport.Reserves).IsEqualTo(0);
    }

    [Test]
    public async Task ClosedOnboardingDeniesEnrollmentThroughRealStatusProvider()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Transport.SetupComplete = false;
        using var response = await fixture.StartAsync();
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That(fixture.Transport.Reserves).IsEqualTo(0);
    }

    [Test]
    public async Task InvalidSuccessReceiptRetainsFlowForExplicitReconciliation()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var start = await fixture.StartAsync();
        var original = await fixture.ReadFlowAsync();
        fixture.Transport.WrongReceipt = true;
        using var response = await fixture.RegisterAsync();
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await Assert.That(await fixture.ReadFlowAsync() == original).IsTrue();
    }

    [Test]
    public async Task CookieContainsOnlyCorrelationAndCachedAuthorityNeverIncludesPassword()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var start = await fixture.StartAsync();
        var flow = await fixture.ReadFlowAsync();
        var protection = fixture.Services.GetRequiredService<IDataProtectionProvider>();
        string cookiePayload = protection.CreateProtector("Explore.LocalVisitorEnrollment.Cookie.v1")
            .Unprotect(fixture.FlowCookie!);
        await Assert.That(cookiePayload == flow!.Correlation).IsTrue();
        byte[]? cached = await fixture.Services.GetRequiredService<IDistributedCache>()
            .GetAsync("bff-local-enrollment:" + flow.Correlation, Cancellation);
        byte[] payload = protection.CreateProtector("Explore.LocalVisitorEnrollment.Cache.v1").Unprotect(cached!);
        string serialized = System.Text.Encoding.UTF8.GetString(payload);
        using var state = JsonDocument.Parse(payload);
        await Assert.That(state.RootElement.TryGetProperty("password", out _)
            || serialized.Contains(fixture.Password, StringComparison.Ordinal)).IsFalse();
        await Assert.That(state.RootElement.GetProperty("proof").GetString() == flow.Proof
            && state.RootElement.GetProperty("enrollmentIntent").GetString() == flow.EnrollmentIntent).IsTrue();
        await Assert.That(flow.ToString()).IsEqualTo(nameof(LocalVisitorEnrollmentFlow));
        await Assert.That(new LocalVisitorEnrollmentRegisterRequest(fixture.Username, fixture.Password).ToString())
            .IsEqualTo(nameof(LocalVisitorEnrollmentRegisterRequest));
    }

    [Test]
    public async Task LateReservationSnapshotCannotReviveCompletedFlow()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var start = await fixture.StartAsync();
        var original = await fixture.ReadFlowAsync();
        using var scope = fixture.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<LocalVisitorEnrollmentFlowStore>();
        var context = new DefaultHttpContext();
        await store.CompleteAsync(context, original!, Cancellation);
        await store.SaveAsync(original!, Cancellation);

        // The original browser still presents a valid protected cookie, but completion
        // must win over a reserve response that arrived after registration committed.
        using var response = await fixture.RegisterAsync();
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(fixture.Transport.Registrations).IsEqualTo(0);
    }

    private sealed class Fixture(WebApplication app, Transport transport, Clock clock) : IAsyncDisposable
    {
        private HttpClient? _client;
        private HttpClient Client => _client ??= app.GetTestClient();
        private readonly Dictionary<string, string> _cookies = new(StringComparer.Ordinal);
        internal Transport Transport { get; } = transport;
        internal IServiceProvider Services => app.Services;
        internal Clock Clock { get; } = clock;
        internal string Username { get; } = Secret();
        internal string Password { get; } = Secret();
        internal string Tenant { get; set; } = "alpha";
        internal string Host { get; set; } = "enrollment.example.test";
        internal string Origin { get; set; } = "https://enrollment.example.test";
        internal string PathBase { get; set; } = "/alpha";
        internal string? FlowCookie => _cookies.GetValueOrDefault(LocalVisitorEnrollmentFlowStore.CookieName);
        internal void SetFlowCookie(string value) => _cookies[LocalVisitorEnrollmentFlowStore.CookieName] = value;

        internal static async Task<Fixture> CreateAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.WebHost.UseTestServer();
            builder.Configuration["Authentication:Provider"] = "local";
            builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
            builder.Services.AddRateLimiter(options => options.AddConcurrencyLimiter(
                RateLimitingExtensions.LocalAuthenticationPolicy, limiter => { limiter.PermitLimit = 100; limiter.QueueLimit = 0; }));
            builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            builder.Services.AddDistributedMemoryCache();
            builder.Services.AddMemoryCache();
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddScoped<ITenantRouteContextAccessor, TenantRouteContextAccessor>();
            builder.Services.AddScoped<LocalVisitorEnrollmentFlowStore>();
            var clock = new Clock();
            builder.Services.AddSingleton<TimeProvider>(clock);
            var transport = new Transport(clock);
            builder.Services.AddHttpClient(BffLocalCredentialEndpoints.HttpClientName,
                client => client.BaseAddress = new Uri("https://api.example.test"))
                .ConfigurePrimaryHttpMessageHandler(() => transport);
            builder.Services.AddScoped<IInstanceOnboardingClient>(services => new InstanceOnboardingClient(
                services.GetRequiredService<IHttpClientFactory>().CreateClient(BffLocalCredentialEndpoints.HttpClientName)));
            builder.Services.AddSingleton<IBffOnboardingStatusProvider, BffOnboardingStatusProvider>();
            var app = builder.Build();
            var fixture = new Fixture(app, transport, clock);
            app.Use(async (context, next) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Loopback;
                context.Request.PathBase = fixture.PathBase;
                context.RequestServices.GetRequiredService<ITenantRouteContextAccessor>().SetTenantSlug(fixture.Tenant);
                // The real accessor is backed by request items, so an external await can
                // deterministically change the original target without replacing an authority.
                transport.CurrentContext = context;
                transport.CurrentFixture = fixture;
                await next(context);
            });
            app.UseLocalVisitorEnrollmentPrivacyHeaders();
            app.UseRouting();
            app.UseRateLimiter();
            app.MapGet("/antiforgery", (HttpContext context, IAntiforgery antiforgery) =>
                Results.Json(new { token = antiforgery.GetAndStoreTokens(context).RequestToken }));
            app.MapLocalVisitorEnrollmentEndpoints();
            await app.StartAsync(Cancellation);
            return fixture;
        }

        internal Task<HttpResponseMessage> StartAsync() => PostAsync("/start", new { returnUrl = "/alpha/events" });
        internal Task<HttpResponseMessage> RegisterAsync() => PostAsync("/register", new { username = Username, password = Password });
        internal async Task<HttpResponseMessage> PostAsync(string suffix, object body, bool csrf = true)
        {
            string? token = null;
            if (csrf)
            {
                using var tokenRequest = new HttpRequestMessage(HttpMethod.Get, "https://" + Host + "/antiforgery");
                AddCookies(tokenRequest);
                using var tokenResponse = await Client.SendAsync(tokenRequest, Cancellation);
                CaptureCookies(tokenResponse);
                using var json = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync(Cancellation));
                token = json.RootElement.GetProperty("token").GetString();
            }
            using var request = new HttpRequestMessage(HttpMethod.Post,
                "https://" + Host + BffLocalVisitorEnrollmentEndpoints.Prefix + suffix) { Content = JsonContent.Create(body) };
            request.Headers.Add("Origin", Origin);
            request.Headers.Add("X-Tenant-Slug", "untrusted");
            request.Headers.Add("X-Local-Enrollment-Proof", Secret());
            if (token is not null) request.Headers.Add("X-CSRF-TOKEN", token);
            AddCookies(request);
            var response = await Client.SendAsync(request, Cancellation);
            CaptureCookies(response);
            return response;
        }

        internal async Task<LocalVisitorEnrollmentFlow?> ReadFlowAsync()
        {
            using var scope = app.Services.CreateScope();
            var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            context.Request.Scheme = "https";
            context.Request.Host = new HostString(Host);
            context.Connection.RemoteIpAddress = IPAddress.Loopback;
            context.Request.PathBase = PathBase;
            context.Request.Headers.Origin = Origin;
            context.Request.Headers.Cookie = string.Join("; ", _cookies.Select(pair => pair.Key + "=" + pair.Value));
            context.Items[TenantRouteContextAccessor.TenantSlugItemKey] = Tenant;
            // Install synchronously before entering asynchronous reads.
            var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            accessor.HttpContext = context;
            try { return await scope.ServiceProvider.GetRequiredService<LocalVisitorEnrollmentFlowStore>().ReadAsync(context, Cancellation); }
            finally { accessor.HttpContext = null; }
        }

        private void AddCookies(HttpRequestMessage request)
        {
            if (_cookies.Count != 0) request.Headers.Add("Cookie", string.Join("; ", _cookies.Select(pair => pair.Key + "=" + pair.Value)));
        }
        private void CaptureCookies(HttpResponseMessage response)
        {
            if (!response.Headers.TryGetValues("Set-Cookie", out var values)) return;
            foreach (var value in values.Select(value => SetCookieHeaderValue.Parse(value)))
            {
                if (value.Expires <= DateTimeOffset.UtcNow || value.MaxAge == TimeSpan.Zero) _cookies.Remove(value.Name.ToString());
                else _cookies[value.Name.ToString()] = value.Value.ToString();
            }
        }
        public async ValueTask DisposeAsync() { _client?.Dispose(); await app.DisposeAsync(); }
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        internal void Advance(TimeSpan amount) => _now += amount;
    }

    private sealed class Transport(Clock clock) : HttpMessageHandler
    {
        internal int Reserves { get; private set; }
        internal int Registrations { get; private set; }
        internal Guid Operation { get; private set; }
        internal string? Proof { get; private set; }
        private readonly string _claim = Secret();
        private DateTimeOffset? _expiresAt;
        private string? _registration;
        internal bool LoseNextResponse { get; set; }
        internal bool SetupComplete { get; set; } = true;
        internal bool WrongReceipt { get; set; }
        internal bool ReconciliationKeptOriginalAuthority { get; private set; } = true;
        internal bool UntrustedHeaders { get; private set; }
        internal bool TargetWasPinned { get; private set; } = true;
        internal HttpStatusCode? Status { get; set; }
        internal Action? AfterMutation { get; set; }
        internal HttpContext? CurrentContext { get; set; }
        internal Fixture? CurrentFixture { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (!path.StartsWith("/api/auth/local/enrollment/", StringComparison.Ordinal))
                return Json(new { isCompleted = SetupComplete, state = SetupComplete ? "Completed" : "Closed",
                    mode = "Interactive", generation = 1, provider = "Local" });
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var operation = body.RootElement.GetProperty("operationId").GetGuid();
            var proof = request.Headers.GetValues("X-Local-Enrollment-Proof").Single();
            if (Operation != Guid.Empty)
                ReconciliationKeptOriginalAuthority &= Operation == operation && Proof == proof;
            Operation = operation;
            Proof = proof;
            UntrustedHeaders |= request.Headers.Authorization is not null
                || new[] { "Cookie", "X-CSRF-TOKEN", "X-Setup-Secret" }.Any(request.Headers.Contains);
            TargetWasPinned &= request.Headers.GetValues("X-Tenant-Slug").Single() == "alpha"
                && request.Headers.GetValues("X-Forwarded-Host").Single() == "enrollment.example.test";
            bool reserve = path.EndsWith("/reserve", StringComparison.Ordinal);
            if (reserve) Reserves++;
            else
            {
                Registrations++;
                string registration = body.RootElement.GetRawText();
                ReconciliationKeptOriginalAuthority &= _registration is null || _registration == registration;
                ReconciliationKeptOriginalAuthority &= body.RootElement.GetProperty("enrollmentIntent").GetString() == _claim;
                _registration = registration;
            }
            AfterMutation?.Invoke();
            if (CurrentContext is { } context && CurrentFixture is { } fixture)
                context.RequestServices.GetRequiredService<ITenantRouteContextAccessor>().SetTenantSlug(fixture.Tenant);
            _expiresAt ??= clock.GetUtcNow().AddMinutes(15);
            if (LoseNextResponse)
            {
                LoseNextResponse = false;
                throw new HttpRequestException();
            }
            if (Status is { } status) return new(status) { Content = JsonContent.Create(new { detail = Secret() }) };
            return reserve
                ? Json(new { operationId = operation, enrollmentIntent = _claim, expiresAt = _expiresAt })
                : Json(new { id = WrongReceipt ? Guid.CreateVersion7() : operation, success = true });
        }
        private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
    }
}
