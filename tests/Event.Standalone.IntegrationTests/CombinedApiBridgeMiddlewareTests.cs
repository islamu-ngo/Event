using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Event.Standalone.Middleware;
using Event.Web.BffHosting.Abstractions;
using Event.Web.BffHosting.Security;
using Explore.Application.Constants;
using Explore.Blazor.Extensions;
using Explore.Blazor.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Event.Standalone.IntegrationTests;

public sealed class CombinedApiBridgeMiddlewareTests
{
    private const string SeenAuthorizationHeader = "X-Seen-Authorization";
    [Test]
    public async Task ValidCookieWithoutTokenSanitizesAndFailsClosed()
    {
        await using var app = await CreateApplicationAsync(cookieToken: null);
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/events");
        request.Headers.Add("X-Test-Cookie", "valid");
        request.Headers.Add("Authorization", "Bearer attacker");
        request.Headers.Add(EventBffHeaderNames.ApiKey, "attacker-key");
        request.Headers.Add(EventBffHeaderNames.TenantSlug, "attacker-tenant");

        using var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(response.Headers.Contains("X-Next-Reached")).IsFalse();
    }

    [Test]
    [Arguments("/api/instanceonboarding/status", false)]
    [Arguments("/api/instanceonboarding/journey", true)]
    public async Task AuthenticatedOnboardingReadPreservesIndependentServerOwnedAuthorities(string path, bool forwardsSetup)
    {
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await using var app = await CreateApplicationAsync(cookieToken: token);
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Test-Cookie", "valid");
        request.Headers.Add("Authorization", "Bearer " + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        request.Headers.Add(EventBffHeaderNames.SetupSecret, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        request.Headers.Add("Cookie", "setup-secret=" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));

        using var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.GetValues(SeenAuthorizationHeader).Single()).IsEqualTo($"Bearer {token}");
        // Journey needs the independently resolved setup capability before administrator creation.
        // Header presence is not active setup authority: the API independently checks setup lifecycle.
        await Assert.That(response.Headers.Contains("X-Seen-Setup")).IsEqualTo(forwardsSetup);
        if (forwardsSetup)
            await Assert.That(response.Headers.GetValues("X-Seen-Setup").Single())
                .IsEqualTo(app.Services.GetRequiredService<TestSetupState>().Secret);
        await Assert.That(response.Headers.GetValues("X-Seen-User").Single()).IsEqualTo("api-user");
        await Assert.That(response.Headers.GetValues("X-Seen-Auth-Type").Single()).IsEqualTo(ApiAuthenticationSchemeNames.MultiAuth);
        await Assert.That(response.Headers.Contains("X-Seen-Cookie")).IsFalse();
    }

    [Test]
    [Arguments("missing", false)]
    [Arguments("other-subject", false)]
    [Arguments("other-scheme", false)]
    [Arguments("protected", true)]
    [Arguments("forged", false)]
    [Arguments("expired", false)]
    public async Task JourneyCannotAcquireSetupAuthorityFromBrowserHeadersOrAnotherPrincipal(string source, bool forwardsSetup)
    {
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await using var app = await CreateApplicationAsync(cookieToken: token, hasSetupSession: false);
        string secret = app.Services.GetRequiredService<TestSetupState>().Secret;
        var sessions = app.Services.GetRequiredService<ISetupSecretSessionService>();
        var otherPrincipal = CreatePrincipal(source == "other-subject" ? "other-user" : "cookie-user",
            source == "other-scheme" ? "Keycloak" : TestAuthenticationHandler.CookieScheme);
        await Assert.That(otherPrincipal.TryGetSetupSessionIdentity(out var otherIdentity)).IsTrue();
        if (source is "other-subject" or "other-scheme")
            sessions.SetForUser(otherIdentity.PartitionKey, secret);

        var protection = app.Services.GetRequiredService<IDataProtectionProvider>();
        string? cookie = source switch
        {
            "protected" => app.Services.GetRequiredService<ISetupSecretCookieProtector>().Protect(secret),
            "forged" => secret,
            "expired" => protection.CreateProtector("Explore.Blazor.SetupSecretCookie.v1")
                .ToTimeLimitedDataProtector().Protect(secret, DateTimeOffset.UtcNow.AddMinutes(-1)),
            _ => null
        };
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/instanceonboarding/journey");
        request.Headers.Add("X-Test-Cookie", "valid");
        request.Headers.Add(EventBffHeaderNames.SetupSecret, secret);
        request.Headers.Add("Authorization", "Bearer " + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        if (cookie is not null) request.Headers.Add("Cookie", "setup-secret=" + cookie);

        using var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.GetValues(SeenAuthorizationHeader).Single()).IsEqualTo($"Bearer {token}");
        await Assert.That(response.Headers.Contains("X-Seen-Setup")).IsEqualTo(forwardsSetup);
        if (forwardsSetup)
            await Assert.That(response.Headers.GetValues("X-Seen-Setup").Single()).IsEqualTo(secret);
        await Assert.That(response.Headers.GetValues("X-Seen-User").Single()).IsEqualTo("api-user");
        await Assert.That(response.Headers.Contains("X-Seen-Cookie")).IsFalse();
    }

    [Test]
    public async Task NoCookieLeavesExternalBearerRequestUnchanged()
    {
        await using var app = await CreateApplicationAsync(cookieToken: "server-token");
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/events");
        request.Headers.Add("Authorization", "Bearer external-token");
        request.Headers.Add(EventBffHeaderNames.ApiKey, "external-key");

        using var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.GetValues(SeenAuthorizationHeader).Single())
            .IsEqualTo("Bearer external-token");
        await Assert.That(response.Headers.GetValues("X-Seen-Api-Key").Single())
            .IsEqualTo("external-key");
    }

    [Test]
    public async Task ValidCookieReconstructsTrustedHeadersAndApiPrincipalOnly()
    {
        await using var app = await CreateApplicationAsync(cookieToken: "server-token");
        using var client = app.GetTestClient();
        var antiforgery = await IssueAntiforgeryAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Patch, "/api/instance/settings/auth-provider");
        request.Headers.Add("X-Test-Cookie", "valid");
        request.Headers.Add("Cookie", antiforgery.CookieHeader);
        request.Headers.Add("X-CSRF-TOKEN", antiforgery.Token);
        request.Headers.Add("Authorization", "Bearer attacker");
        request.Headers.Add(EventBffHeaderNames.ApiKey, "attacker-key");
        request.Headers.Add("X-Control-Plane-Key", "attacker-control-plane-key");
        request.Headers.Add(EventBffHeaderNames.TenantSlug, "attacker-tenant");
        request.Headers.Add(EventBffHeaderNames.SupportAccessMode, "Write");

        using var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.GetValues(SeenAuthorizationHeader).Single())
            .IsEqualTo("Bearer server-token");
        await Assert.That(response.Headers.GetValues("X-Seen-Tenant").Single())
            .IsEqualTo("trusted-tenant");
        await Assert.That(response.Headers.GetValues("X-Seen-Setup").Single())
            .IsEqualTo(app.Services.GetRequiredService<TestSetupState>().Secret);
        await Assert.That(response.Headers.GetValues("X-Seen-Support").Single())
            .IsEqualTo("11111111-1111-1111-1111-111111111111");
        await Assert.That(response.Headers.GetValues("X-Seen-Auth-Type").Single())
            .IsEqualTo(ApiAuthenticationSchemeNames.MultiAuth);
        await Assert.That(response.Headers.Contains("X-Seen-Api-Key")).IsFalse();
        await Assert.That(response.Headers.Contains("X-Seen-Control-Plane-Key")).IsFalse();
    }

    [Test]
    [Arguments("/api/instance/settings/auth-provider", null)]
    [Arguments("/api/instance/settings/auth-provider", "invalid-token")]
    [Arguments("/api/InstanceOnboarding/auth-provider-configuration", null)]
    [Arguments("/api/InstanceOnboarding/auth-provider-configuration", "invalid-token")]
    public async Task CookieUnsafeSetupAndOnboardingRequestsRequireValidAntiforgery(
        string path,
        string? token)
    {
        await using var app = await CreateApplicationAsync(cookieToken: "server-token");
        using var client = app.GetTestClient();
        var antiforgery = await IssueAntiforgeryAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Patch, path);
        request.Headers.Add("X-Test-Cookie", "valid");
        request.Headers.Add("Cookie", antiforgery.CookieHeader);
        if (token is not null)
        {
            request.Headers.Add("X-CSRF-TOKEN", token);
        }

        using var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(response.Headers.Contains("X-Next-Reached")).IsFalse();
    }

    [Test]
    public async Task NoCookieLeavesExternalUnsafeBearerRequestIndependentOfAntiforgery()
    {
        await using var app = await CreateApplicationAsync(cookieToken: "server-token");
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Patch, "/api/instance/settings/auth-provider");
        request.Headers.Add("Authorization", "Bearer external-token");
        request.Headers.Add(EventBffHeaderNames.ApiKey, "external-key");

        using var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.GetValues(SeenAuthorizationHeader).Single())
            .IsEqualTo("Bearer external-token");
        await Assert.That(response.Headers.GetValues("X-Seen-Api-Key").Single())
            .IsEqualTo("external-key");
    }

    [Test]
    public async Task SessionResolutionRestoresOriginalPrincipalWhenProviderThrows()
    {
        var originalPrincipal = CreatePrincipal("original-user", "Original");
        var cookiePrincipal = CreatePrincipal("cookie-user", TestAuthenticationHandler.CookieScheme);
        var context = new DefaultHttpContext { User = originalPrincipal };
        var properties = new AuthenticationProperties();
        properties.StoreTokens([new AuthenticationToken { Name = "access_token", Value = "server-token" }]);
        var session = AuthenticateResult.Success(
            new AuthenticationTicket(cookiePrincipal, properties, TestAuthenticationHandler.CookieScheme));
        var throwingProvider = new ThrowingSupportAccessProvider();
        var enricher = new EventBffRequestEnricher(
            new NullAccessTokenProvider(),
            new TrustedTenantProvider(),
            new NoSetupSecretProvider(),
            throwingProvider);
        Exception? caught = null;

        try
        {
            _ = await enricher.ResolveForSessionAsync(context, session, CancellationToken.None);
        }
        catch (Exception exception)
        {
            caught = exception;
        }

        await Assert.That(caught).IsTypeOf<InvalidOperationException>();
        await Assert.That(throwingProvider.SawCookiePrincipal).IsTrue();
        await Assert.That(context.User).IsSameReferenceAs(originalPrincipal);
    }

    [Test]
    public async Task SessionResolutionRestoresOriginalPrincipalWhenCancelled()
    {
        var originalPrincipal = CreatePrincipal("original-user", "Original");
        var cookiePrincipal = CreatePrincipal("cookie-user", TestAuthenticationHandler.CookieScheme);
        var context = new DefaultHttpContext { User = originalPrincipal };
        var session = AuthenticateResult.Success(
            new AuthenticationTicket(cookiePrincipal, TestAuthenticationHandler.CookieScheme));
        var cancellingProvider = new CancellingSupportAccessProvider();
        var enricher = new EventBffRequestEnricher(
            new NullAccessTokenProvider(),
            new TrustedTenantProvider(),
            new NoSetupSecretProvider(),
            cancellingProvider);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Exception? caught = null;

        try
        {
            _ = await enricher.ResolveForSessionAsync(context, session, cancellation.Token);
        }
        catch (Exception exception)
        {
            caught = exception;
        }

        await Assert.That(caught).IsTypeOf<OperationCanceledException>();
        await Assert.That(cancellingProvider.SawCookiePrincipal).IsTrue();
        await Assert.That(context.User).IsSameReferenceAs(originalPrincipal);
    }

    [Test]
    public async Task NonApiPathDoesNotClassifyOrMutateRequest()
    {
        await using var app = await CreateApplicationAsync(cookieToken: null);
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("X-Test-Cookie", "valid");
        request.Headers.Add("Authorization", "Bearer external-token");

        using var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.GetValues(SeenAuthorizationHeader).Single())
            .IsEqualTo("Bearer external-token");
    }

    [Test]
    public async Task ValidCookieUnsafeRequestWithoutAntiforgeryFailsBeforeApiAuthentication()
    {
        await using var app = await CreateApplicationAsync(cookieToken: "server-token");
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/events");
        request.Headers.Add("X-Test-Cookie", "valid");
        request.Headers.Add("Cookie", ".AspNetCore.Cookies=test-session");

        using var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(response.Headers.Contains("X-Next-Reached")).IsFalse();
    }

    [Test]
    public async Task ValidCookieWithExpiredTokenFailsClosed()
    {
        await using var app = await CreateApplicationAsync(
            cookieToken: "eyJhbGciOiJub25lIn0.eyJleHAiOjF9.");
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/events");
        request.Headers.Add("X-Test-Cookie", "valid");
        request.Headers.Add("Authorization", "Bearer attacker");

        using var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(response.Headers.Contains("X-Next-Reached")).IsFalse();
    }

    [Test]
    public async Task UnrefreshableCookieSessionFailsClosed()
    {
        await using var app = await CreateApplicationAsync(cookieToken: "server-token");
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/events");
        request.Headers.Add("X-Test-Cookie", "refresh-rejected");
        request.Headers.Add(EventBffHeaderNames.ApiKey, "attacker-key");

        using var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(response.Headers.Contains("X-Next-Reached")).IsFalse();
    }

    private static async Task<WebApplication> CreateApplicationAsync(string? cookieToken, bool hasSetupSession = true)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing"
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = ApiAuthenticationSchemeNames.MultiAuth;
                options.DefaultChallengeScheme = ApiAuthenticationSchemeNames.MultiAuth;
            })
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                TestAuthenticationHandler.CookieScheme,
                _ => { })
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                ApiAuthenticationSchemeNames.MultiAuth,
                _ => { });
        builder.Services.AddSingleton(new TestTokenState(cookieToken));
        builder.Services.AddSingleton<IEventBffAccessTokenProvider, NullAccessTokenProvider>();
        builder.Services.AddSingleton<IEventBffTenantHintProvider, TrustedTenantProvider>();
        builder.Services.AddSingleton<IEventBffSupportAccessProvider, PrincipalSupportAccessProvider>();
        var setupState = new TestSetupState(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        var setupSessions = new SetupSecretSessionService();
        if (hasSetupSession)
        {
            CreatePrincipal("cookie-user", TestAuthenticationHandler.CookieScheme).TryGetSetupSessionIdentity(out var identity);
            setupSessions.SetForUser(identity.PartitionKey, setupState.Secret);
        }
        builder.Services.AddSingleton(setupState);
        builder.Services.AddSingleton<ISetupSecretSessionService>(setupSessions);
        builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        builder.Services.AddSingleton<ISetupSecretCookieProtector, SetupSecretCookieProtector>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.Configure<SetupSecretResolverOptions>(_ => { });
        builder.Services.AddScoped<ISetupSecretResolver, SetupSecretResolver>();
        builder.Services.AddBffTrustedRequestEnrichment();
        builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
        builder.Services.AddCombinedApiBridge();

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            if (context.Request.Path == "/xsrf")
            {
                var session = await context.AuthenticateAsync(TestAuthenticationHandler.CookieScheme);
                if (session.Succeeded) context.User = session.Principal!;
                var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
                await context.Response.WriteAsync(antiforgery.GetAndStoreTokens(context).RequestToken!);
                return;
            }

            await next(context);
        });
        app.UseCombinedApiBridge();
        app.UseAuthentication();
        app.Run(context =>
        {
            context.Response.Headers["X-Next-Reached"] = "true";
            CopyHeader(context, "Authorization", SeenAuthorizationHeader);
            CopyHeader(context, EventBffHeaderNames.ApiKey, "X-Seen-Api-Key");
            CopyHeader(context, "X-Control-Plane-Key", "X-Seen-Control-Plane-Key");
            CopyHeader(context, EventBffHeaderNames.TenantSlug, "X-Seen-Tenant");
            CopyHeader(context, EventBffHeaderNames.SetupSecret, "X-Seen-Setup");
            CopyHeader(context, "Cookie", "X-Seen-Cookie");
            if (context.User.Identity?.Name is { } name)
                context.Response.Headers["X-Seen-User"] = name;
            CopyHeader(context, EventBffHeaderNames.SupportAccessSessionId, "X-Seen-Support");
            if (!string.IsNullOrEmpty(context.User.Identity?.AuthenticationType))
            {
                context.Response.Headers["X-Seen-Auth-Type"] = context.User.Identity.AuthenticationType;
            }

            return Task.CompletedTask;
        });
        await app.StartAsync();
        return app;
    }

    private static async Task<AntiforgeryPair> IssueAntiforgeryAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/xsrf");
        request.Headers.Add("X-Test-Cookie", "valid");
        using var response = await client.SendAsync(request);
        var token = await response.Content.ReadAsStringAsync();
        var cookieHeader = string.Join(
            "; ",
            response.Headers.GetValues("Set-Cookie").Select(value => value.Split(';', 2)[0]));
        return new AntiforgeryPair(token, cookieHeader);
    }

    private static void CopyHeader(HttpContext context, string source, string destination)
    {
        if (context.Request.Headers.TryGetValue(source, out var value))
        {
            context.Response.Headers[destination] = value;
        }
    }

    private static ClaimsPrincipal CreatePrincipal(string name, string authenticationType) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Name, name), new Claim("sub", name)], authenticationType));

    private sealed record TestTokenState(string? Token);

    private sealed record TestSetupState(string Secret);

    private sealed record AntiforgeryPair(string Token, string CookieHeader);

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        TestTokenState tokenState)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string CookieScheme = "Cookies";
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Scheme.Name == CookieScheme)
            {
                if (!Request.Headers.TryGetValue("X-Test-Cookie", out var cookieState))
                {
                    return Task.FromResult(AuthenticateResult.NoResult());
                }

                if (cookieState == "refresh-rejected")
                {
                    Context.Items[Event.Web.BffHosting.Authentication.EventBffAuthenticationConstants
                        .TokenRefreshRejectedItemKey] = true;
                    return Task.FromResult(AuthenticateResult.Fail("refresh rejected"));
                }

                var properties = new AuthenticationProperties();
                if (tokenState.Token is not null)
                {
                    properties.StoreTokens([new AuthenticationToken { Name = "access_token", Value = tokenState.Token }]);
                }

                return Task.FromResult(Success("cookie-user", properties));
            }

            return Task.FromResult(tokenState.Token is not null
                && Request.Headers.Authorization.ToString() == $"Bearer {tokenState.Token}"
                ? Success("api-user", new AuthenticationProperties())
                : AuthenticateResult.NoResult());
        }

        private AuthenticateResult Success(string name, AuthenticationProperties properties)
        {
            return AuthenticateResult.Success(
                new AuthenticationTicket(CreatePrincipal(name, Scheme.Name), properties, Scheme.Name));
        }
    }

    private sealed class NullAccessTokenProvider : IEventBffAccessTokenProvider
    {
        public ValueTask<string?> ResolveAccessTokenAsync(HttpContext httpContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>(null);
    }

    private sealed class TrustedTenantProvider : IEventBffTenantHintProvider
    {
        public string ResolveTenantSlug(HttpContext httpContext) => "trusted-tenant";
    }

    private sealed class NoSetupSecretProvider : IEventBffSetupSecretProvider
    {
        public ValueTask<string?> ResolveSetupSecretAsync(HttpContext httpContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>(null);
    }

    private sealed class PrincipalSupportAccessProvider : IEventBffSupportAccessProvider
    {
        public ValueTask<string?> ResolveSupportAccessSessionIdAsync(HttpContext httpContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(httpContext.User.Identity?.Name == "cookie-user"
                ? "11111111-1111-1111-1111-111111111111"
                : null);
    }

    private sealed class ThrowingSupportAccessProvider : IEventBffSupportAccessProvider
    {
        public bool SawCookiePrincipal { get; private set; }

        public ValueTask<string?> ResolveSupportAccessSessionIdAsync(
            HttpContext httpContext,
            CancellationToken cancellationToken)
        {
            SawCookiePrincipal = httpContext.User.Identity?.Name == "cookie-user";
            throw new InvalidOperationException("provider failure");
        }
    }

    private sealed class CancellingSupportAccessProvider : IEventBffSupportAccessProvider
    {
        public bool SawCookiePrincipal { get; private set; }

        public ValueTask<string?> ResolveSupportAccessSessionIdAsync(
            HttpContext httpContext,
            CancellationToken cancellationToken)
        {
            SawCookiePrincipal = httpContext.User.Identity?.Name == "cookie-user";
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<string?>(null);
        }
    }
}
