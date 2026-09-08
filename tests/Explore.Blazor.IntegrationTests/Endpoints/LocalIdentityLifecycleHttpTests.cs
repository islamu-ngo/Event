
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text.Json;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;

namespace Explore.Blazor.IntegrationTests.Endpoints;

public sealed class LocalIdentityLifecycleHttpTests
{
    private static CancellationToken Cancellation => TestContext.Current!.Execution.CancellationToken;
    private static string Secret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

    [Test]
    [Arguments("/email-verifications")]
    [Arguments("/email-verifications/consume")]
    [Arguments("/password-recoveries")]
    [Arguments("/password-recoveries/consume")]
    public async Task AnonymousMutationsRequireNativeAntiforgeryAndArePrivate(string suffix)
    {
        await using var fixture = new Fixture();
        using var response = await fixture.Client.PostAsJsonAsync(BffLocalIdentityLifecycleEndpoints.Prefix + suffix,
            new { identifier = Secret() }, Cancellation);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(response.Headers.CacheControl?.NoStore == true && response.Headers.CacheControl.Private).IsTrue();
        await Assert.That(fixture.Transport.Mutations).IsEqualTo(0);
    }

    [Test]
    [Arguments("/email-verifications")]
    [Arguments("/password-recoveries")]
    public async Task PublicRequestsReturnUniformEmptyAcceptedWithoutSessionOrBrowserAuthority(string suffix)
    {
        await using var fixture = new Fixture();
        foreach (string identifier in new[] { Secret(), Secret() + "@example.test" })
        {
            using var request = await fixture.RequestAsync(suffix, new { identifier });
            request.Headers.Authorization = new("Bearer", Secret());
            request.Headers.Add("Idempotency-Key", Secret());
            request.Headers.Add("X-Setup-Secret", Secret());
            using var response = await fixture.Client.SendAsync(request, Cancellation);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
            await Assert.That((await response.Content.ReadAsByteArrayAsync(Cancellation)).Length).IsEqualTo(0);
            await Assert.That(fixture.Transport.Authorization).IsNull();
            await Assert.That(fixture.Transport.UntrustedHeaders).IsFalse();
            using var body = JsonDocument.Parse(fixture.Transport.Body!);
            await Assert.That(body.RootElement.GetProperty("identifier").GetString() == identifier).IsTrue();
            await Assert.That(await fixture.AuthenticatedAsync()).IsFalse();
        }
        await Assert.That(fixture.Transport.Mutations).IsEqualTo(2);
    }

    [Test]
    [Arguments("/email-verifications/consume", 1)]
    [Arguments("/email-verifications/consume", 2)]
    [Arguments("/password-recoveries/consume", 3)]
    public async Task ConsumptionForwardsExactGeneratedPointerAndClearsNativeSession(string suffix, int purpose)
    {
        await using var fixture = new Fixture();
        await fixture.LoginAsync();
        await Assert.That(await fixture.AuthenticatedAsync()).IsTrue();
        string token = Secret(), password = Secret();
        var operation = Guid.CreateVersion7();
        object body = purpose == 3 ? new LocalPasswordRecoveryCompletionRequestDto
        {
            OperationId = operation, LocalSubjectId = Guid.CreateVersion7(), PersonalActorId = Guid.CreateVersion7(),
            ExternalLoginId = Guid.CreateVersion7(), Generation = Guid.CreateVersion7(),
            Purpose = purpose, Token = token, NewPassword = password
        } : new LocalEmailConfirmationRequestDto
        {
            OperationId = operation, LocalSubjectId = Guid.CreateVersion7(), PersonalActorId = Guid.CreateVersion7(),
            ExternalLoginId = Guid.CreateVersion7(), Generation = Guid.CreateVersion7(),
            Purpose = purpose, Token = token
        };
        using var request = await fixture.RequestAsync(suffix, body);
        using var response = await fixture.Client.SendAsync(request, Cancellation);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(fixture.Transport.Authorization).IsNull();
        using var forwarded = JsonDocument.Parse(fixture.Transport.Body!);
        await Assert.That(forwarded.RootElement.GetProperty("operationId").GetGuid()).IsEqualTo(operation);
        await Assert.That(forwarded.RootElement.GetProperty("purpose").GetInt32()).IsEqualTo(purpose);
        await Assert.That(forwarded.RootElement.GetProperty("token").GetString() == token).IsTrue();
        await Assert.That(forwarded.RootElement.EnumerateObject().Count()).IsEqualTo(purpose == 3 ? 8 : 7);
        if (purpose == 3) await Assert.That(forwarded.RootElement.GetProperty("newPassword").GetString() == password).IsTrue();
        await Assert.That(await fixture.AuthenticatedAsync()).IsFalse();
        await Assert.That((await response.Content.ReadAsByteArrayAsync(Cancellation)).Length).IsEqualTo(0);
    }

    [Test]
    public async Task OrdinaryPasswordChangeRequiresCookieAndCurrentPasswordWithoutEmailDiscovery()
    {
        await using var fixture = new Fixture();
        string current = Secret(), next = Secret();
        var body = new LocalPasswordChangeRequestDto { CurrentPassword = current, NewPassword = next };
        using (var denied = await fixture.RequestAsync("/password", body))
        {
            denied.Headers.Authorization = new("Bearer", fixture.Transport.AccessToken);
            using var response = await fixture.Client.SendAsync(denied, Cancellation);
            await Assert.That(response.IsSuccessStatusCode).IsFalse();
            await Assert.That(fixture.Transport.Mutations).IsEqualTo(0);
        }
        await fixture.LoginAsync();
        using var request = await fixture.RequestAsync("/password", body);
        using var accepted = await fixture.Client.SendAsync(request, Cancellation);
        await Assert.That(accepted.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(fixture.Transport.Authorization == "Bearer " + fixture.Transport.AccessToken).IsTrue();
        using var forwarded = JsonDocument.Parse(fixture.Transport.Body!);
        await Assert.That(forwarded.RootElement.GetProperty("currentPassword").GetString() == current).IsTrue();
        await Assert.That(forwarded.RootElement.GetProperty("newPassword").GetString() == next).IsTrue();
        await Assert.That(forwarded.RootElement.EnumerateObject().Count()).IsEqualTo(2);
        await Assert.That(await fixture.AuthenticatedAsync()).IsFalse();
    }

    [Test]
    public async Task ProposedEmailUsesTheValidatedCookieWithoutBodyAuthority()
    {
        await using var fixture = new Fixture();
        await fixture.LoginAsync();
        string email = Secret() + "@example.test";
        using var request = await fixture.RequestAsync("/email-verifications", new LocalEmailVerificationRequestDto { ProposedEmail = email });
        request.Headers.Authorization = new("Bearer", Secret());
        using var response = await fixture.Client.SendAsync(request, Cancellation);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        await Assert.That(fixture.Transport.Authorization == "Bearer " + fixture.Transport.AccessToken).IsTrue();
        using var body = JsonDocument.Parse(fixture.Transport.Body!);
        await Assert.That(body.RootElement.GetProperty("proposedEmail").GetString() == email).IsTrue();
        await Assert.That(body.RootElement.TryGetProperty("securityStamp", out _)
            || body.RootElement.TryGetProperty("localSubjectId", out _)).IsFalse();
        await Assert.That(await fixture.AuthenticatedAsync()).IsTrue();
    }

    [Test]
    public async Task LandingIsAnonymousAndPrivate()
    {
        await using var fixture = new Fixture();
        using var response = await fixture.Client.GetAsync(BffLocalIdentityLifecycleEndpoints.LandingPath, Cancellation);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.CacheControl?.Private == true && response.Headers.CacheControl.NoStore).IsTrue();
        await Assert.That(response.Headers.GetValues("Referrer-Policy").Single()).IsEqualTo("no-referrer");
    }

    [Test]
    [Arguments(400)]
    [Arguments(409)]
    [Arguments(503)]
    public async Task DownstreamFailuresStayFailedWithoutRetryOrBodyDisclosure(int status)
    {
        await using var fixture = new Fixture();
        fixture.Transport.Status = (HttpStatusCode)status;
        using var request = await fixture.RequestAsync("/password-recoveries", new LocalPasswordRecoveryRequestDto { Identifier = Secret() });
        using var response = await fixture.Client.SendAsync(request, Cancellation);
        await Assert.That((int)response.StatusCode).IsEqualTo(status);
        await Assert.That((await response.Content.ReadAsByteArrayAsync(Cancellation)).Length).IsEqualTo(0);
        await Assert.That(fixture.Transport.Mutations).IsEqualTo(1);
        await Assert.That(await fixture.AuthenticatedAsync()).IsFalse();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly BlazorBffWebApplicationFactory _root = new();
        private readonly WebApplicationFactory<Program> _factory;
        internal Transport Transport { get; } = new();
        internal HttpClient Client { get; }
        internal Fixture()
        {
            _factory = _root.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Authentication:Provider", "local");
                builder.ConfigureTestServices(services =>
                {
                    services.PostConfigure<AuthenticationOptions>(options =>
                    {
                        options.DefaultScheme = options.DefaultAuthenticateScheme = options.DefaultChallengeScheme =
                            options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                    });
                    services.PostConfigure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme, options =>
                    {
                        options.ForwardAuthenticate = options.ForwardChallenge = options.ForwardDefault = null;
                        options.ForwardDefaultSelector = null;
                    });
                    services.RemoveAll<Explore.Blazor.Services.IDynamicAuthSchemeManager>();
                    services.AddSingleton<Explore.Blazor.Services.IDynamicAuthSchemeManager, Explore.Blazor.Services.DynamicAuthSchemeManager>();
                    services.AddScoped<Explore.Blazor.Services.BffAdminClaimsTransformation>();
                    services.AddSingleton<IHttpMessageHandlerBuilderFilter>(new TransportFilter(Transport));
                    services.AddHttpClient("LifecycleTestApi", client => client.BaseAddress = new Uri("https://api.example.test"));
                    services.RemoveAll<ILocalAuthClient>();
                    services.AddScoped<ILocalAuthClient>(provider => new LocalAuthClient(provider.GetRequiredService<IHttpClientFactory>().CreateClient("LifecycleTestApi")));
                });
            });
            Client = _factory.CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true });
        }

        internal async Task<HttpRequestMessage> RequestAsync(string suffix, object body)
        {
            using var status = await Client.GetAsync("/auth/status", Cancellation);
            string xsrf = status.Headers.GetValues("Set-Cookie").Select(value => SetCookieHeaderValue.Parse(value))
                .Single(cookie => cookie.Name == "XSRF-TOKEN").Value.ToString();
            var request = new HttpRequestMessage(HttpMethod.Post, BffLocalIdentityLifecycleEndpoints.Prefix + suffix) { Content = JsonContent.Create(body) };
            request.Headers.Add("X-CSRF-TOKEN", Uri.UnescapeDataString(xsrf));
            return request;
        }
        internal async Task LoginAsync()
        {
            using var request = await RequestAsync("/login", new { identifier = Secret(), password = Secret(), isPersistent = false, returnUrl = "/" });
            using var response = await Client.SendAsync(request, Cancellation);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
        internal async Task<bool> AuthenticatedAsync()
        {
            using var status = await Client.GetAsync("/auth/status", Cancellation);
            using var body = JsonDocument.Parse(await status.Content.ReadAsStringAsync(Cancellation));
            return body.RootElement.GetProperty("isAuthenticated").GetBoolean();
        }
        public async ValueTask DisposeAsync() { Client.Dispose(); await _factory.DisposeAsync(); await _root.DisposeAsync(); }
    }

    private sealed class TransportFilter(Transport transport) : IHttpMessageHandlerBuilderFilter
    {
        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
        { next(builder); builder.PrimaryHandler = transport; };
    }
    private sealed class Transport : HttpMessageHandler
    {
        private readonly Guid _user = Guid.CreateVersion7();
        internal string AccessToken { get; }
        internal int Mutations { get; private set; }
        internal string? Body { get; private set; }
        internal string? Authorization { get; private set; }
        internal bool UntrustedHeaders { get; private set; }
        internal HttpStatusCode? Status { get; set; }
        internal Transport()
        {
            var token = new JwtSecurityToken(issuer: "islamu-event-local", audience: "islamu-event-api",
                claims: [new Claim("sub", _user.ToString("D")), new Claim("auth_provider", "local"), new Claim("local_session_stamp", Secret())],
                notBefore: DateTime.UtcNow.AddSeconds(-1), expires: DateTime.UtcNow.AddMinutes(30),
                signingCredentials: new SigningCredentials(new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(64)), SecurityAlgorithms.HmacSha256));
            AccessToken = new JwtSecurityTokenHandler().WriteToken(token);
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path.Equals("/api/auth/local/login", StringComparison.OrdinalIgnoreCase)) return Json(new
            {
                success = true, userId = _user, firstName = "Local", lastName = "Browser", emailVerified = false,
                roles = Array.Empty<string>(), token = AccessToken, expiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
            });
            if (path.Equals("/api/User", StringComparison.OrdinalIgnoreCase)) return Json(new { id = _user, firstName = "Local", lastName = "Browser" });
            if (path.StartsWith("/api/auth/local/", StringComparison.OrdinalIgnoreCase))
            {
                Mutations++;
                Body = await request.Content!.ReadAsStringAsync(ct);
                Authorization = request.Headers.Authorization?.ToString();
                UntrustedHeaders = new[] { "Cookie", "X-Setup-Secret", "X-CSRF-TOKEN", "Idempotency-Key" }.Any(request.Headers.Contains);
                var status = Status ?? (path.EndsWith("/consume", StringComparison.Ordinal) || path.EndsWith("/password", StringComparison.Ordinal)
                    ? HttpStatusCode.NoContent : HttpStatusCode.Accepted);
                return new HttpResponseMessage(status) { Content = JsonContent.Create(new { token = Secret(), detail = Secret() }) };
            }
            return Json(new { primaryProviderId = 4 });
        }
        private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
    }
}
