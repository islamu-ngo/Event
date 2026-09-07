// ABOUTME: Integration tests for browser-facing setup-secret BFF endpoint sanitization.
// ABOUTME: Verifies local request validation and safe upstream error translation.

using System.Text;
using System.Threading.RateLimiting;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Extensions;
using Explore.Blazor.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;

namespace Explore.Blazor.IntegrationTests.Endpoints;

public sealed class BffSetupSecretEndpointsTests
{
    [Test]
    public async Task SetupSecret_Post_WhenUpstreamForbiddenIncludesRawError_ReturnsSafeProblem()
    {
        using var handler = new ValidateSecretHandler(
            HttpStatusCode.Forbidden,
            """{"valid":false,"error":"provider rejected secret raw-secret-value"}""");
        await using var app = await CreateAppAsync(handler);

        using var response = await app.Client.PostAsJsonAsync("/bff/setup-secret", new { secret = "candidate-secret" });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).Contains("Invalid setup secret.");
        await Assert.That(body).DoesNotContain("provider rejected");
        await Assert.That(body).DoesNotContain("raw-secret-value");
    }

    [Test]
    public async Task SetupSecret_Post_WhenUpstreamValidFalseIncludesRawError_ReturnsSafeProblem()
    {
        using var handler = new ValidateSecretHandler(
            HttpStatusCode.OK,
            """{"valid":false,"error":"database said exact setup secret hash mismatch"}""");
        await using var app = await CreateAppAsync(handler);

        using var response = await app.Client.PostAsJsonAsync("/bff/setup-secret", new { secret = "candidate-secret" });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).Contains("Invalid setup secret.");
        await Assert.That(body).DoesNotContain("database said");
        await Assert.That(body).DoesNotContain("hash mismatch");
    }

    [Test]
    public async Task SetupSecret_Post_WhenRequestJsonMalformed_ReturnsSafeBadRequest()
    {
        using var handler = new ValidateSecretHandler(HttpStatusCode.OK, """{"valid":true}""");
        await using var app = await CreateAppAsync(handler);
        using var content = new StringContent("{", Encoding.UTF8, "application/json");

        using var response = await app.Client.PostAsync("/bff/setup-secret", content);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).Contains("Setup secret request body must be valid JSON.");
        await Assert.That(body).DoesNotContain("JsonException");
        await Assert.That(handler.CallCount).IsEqualTo(0);
    }

    [Test]
    public async Task SetupSecret_Post_WhenSecretTooLong_DoesNotCallApi()
    {
        using var handler = new ValidateSecretHandler(HttpStatusCode.OK, """{"valid":true}""");
        await using var app = await CreateAppAsync(handler);
        var tooLongSecret = new string('a', 513);

        using var response = await app.Client.PostAsJsonAsync("/bff/setup-secret", new { secret = tooLongSecret });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).Contains("512 characters or fewer");
        await Assert.That(handler.CallCount).IsEqualTo(0);
    }

    [Test]
    public async Task SetupSecret_Post_WithBrowserSetupSecretHeader_ValidatesOnlyBodySecret()
    {
        using var handler = new ValidateSecretHandler(HttpStatusCode.OK, """{"valid":true}""");
        await using var app = await CreateAppAsync(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/setup-secret");
        request.Headers.Add("X-Setup-Secret", "browser-controlled-secret");
        request.Content = JsonContent.Create(new { secret = "body-secret" });

        using var response = await app.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(handler.CallCount).IsEqualTo(1);
        await Assert.That(handler.CapturedRequestBody).Contains("body-secret");
        await Assert.That(handler.CapturedRequestBody).DoesNotContain("browser-controlled-secret");
        await Assert.That(handler.CapturedSetupSecretHeader).IsNull();
    }

    [Test]
    public async Task SetupSecret_Post_WhenBrowserFacingRequestIsHttp_SetsNonSecureCookie()
    {
        using var handler = new ValidateSecretHandler(HttpStatusCode.OK, """{"valid":true}""");
        await using var app = await CreateAppAsync(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/bff/setup-secret")
        {
            Content = JsonContent.Create(new { secret = "candidate-secret" })
        };

        using var response = await app.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var cookie = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("setup-secret=", StringComparison.Ordinal));
        var sessionCookie = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("setup-secret-session=", StringComparison.Ordinal));
        var normalizedCookie = cookie.ToLowerInvariant();
        var normalizedSessionCookie = sessionCookie.ToLowerInvariant();
        await Assert.That(normalizedCookie).DoesNotContain("; secure");
        await Assert.That(normalizedSessionCookie).DoesNotContain("; secure");
        await Assert.That(normalizedCookie).Contains("httponly");
        await Assert.That(normalizedSessionCookie).Contains("httponly");
        await Assert.That(normalizedCookie).Contains("max-age=1800");
        await Assert.That(normalizedSessionCookie).Contains("max-age=1800");
    }

    [Test]
    public async Task SetupSecret_Post_WhenBrowserFacingRequestIsHttps_SetsSecureCookie()
    {
        using var handler = new ValidateSecretHandler(HttpStatusCode.OK, """{"valid":true}""");
        await using var app = await CreateAppAsync(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://localhost/bff/setup-secret")
        {
            Content = JsonContent.Create(new { secret = "candidate-secret" })
        };

        using var response = await app.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var cookie = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("setup-secret=", StringComparison.Ordinal));
        var sessionCookie = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("setup-secret-session=", StringComparison.Ordinal));
        var normalizedCookie = cookie.ToLowerInvariant();
        var normalizedSessionCookie = sessionCookie.ToLowerInvariant();
        await Assert.That(normalizedCookie).Contains("; secure");
        await Assert.That(normalizedSessionCookie).Contains("; secure");
        await Assert.That(normalizedCookie).Contains("httponly");
        await Assert.That(normalizedSessionCookie).Contains("httponly");
        await Assert.That(normalizedCookie).Contains("max-age=1800");
        await Assert.That(normalizedSessionCookie).Contains("max-age=1800");
    }

    [Test]
    public async Task SetupSecret_Post_WhenValid_DoesNotReflectTheSubmittedSecret()
    {
        using var handler = new ValidateSecretHandler(HttpStatusCode.OK, """{\"valid\":true}""");
        await using var app = await CreateAppAsync(handler);

        const string submittedSecret = "candidate-secret";
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://localhost/bff/setup-secret")
        {
            Content = JsonContent.Create(new { secret = submittedSecret })
        };
        using var response = await app.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).DoesNotContain(submittedSecret);
    }

    [Test]
    public async Task SetupSecret_Get_WhenPersistedSecretIsTrusted_RefreshesCookiesWithoutRevalidation()
    {
        using var handler = new ValidateSecretHandler(HttpStatusCode.OK, """{"valid":true}""");
        await using var app = await CreateAppAsync(
            handler,
            setupSecretResolver: new StaticSetupSecretResolver("candidate-secret"));

        using var response = await app.Client.GetAsync("/bff/setup-secret");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var cookies = response.Headers.GetValues("Set-Cookie").ToArray();
        await Assert.That(cookies.Single(value => value.StartsWith("setup-secret=", StringComparison.Ordinal))).Contains("max-age=1800");
        await Assert.That(cookies.Single(value => value.StartsWith("setup-secret-session=", StringComparison.Ordinal))).Contains("max-age=1800");
        await Assert.That(handler.CallCount).IsEqualTo(0);
    }

    [Test]
    public async Task SetupSecret_Sync_WhenPersistedSecretIsTrusted_BindsSessionWithoutRevalidation()
    {
        using var handler = new ValidateSecretHandler(HttpStatusCode.OK, """{"valid":true}""");
        await using var app = await CreateAppAsync(
            handler,
            setupSecretResolver: new StaticSetupSecretResolver("candidate-secret"),
            authenticatedUserId: "setup-user");

        using var response = await app.Client.PostAsJsonAsync("/bff/setup-secret/sync", new { secret = "" });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(handler.CallCount).IsEqualTo(0);
    }

    [Test]
    public async Task SetupSecret_Sync_WhenSecretComesFromDevelopmentConfiguration_ValidatesBeforeBinding()
    {
        using var handler = new ValidateSecretHandler(HttpStatusCode.OK, """{"valid":true}""");
        await using var app = await CreateAppAsync(
            handler,
            setupSecretResolver: new StaticSetupSecretResolver(
                "candidate-secret",
                SetupSecretSource.DevelopmentConfiguration),
            authenticatedUserId: "setup-user");

        using var response = await app.Client.PostAsJsonAsync("/bff/setup-secret/sync", new { secret = "" });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(handler.CallCount).IsEqualTo(1);
    }

    [Test]
    public async Task SetupSecret_Post_WhenRateLimitExceeded_ReturnsProblemDetails429()
    {
        using var handler = new ValidateSecretHandler(HttpStatusCode.OK, """{"valid":true}""");
        await using var app = await CreateAppAsync(handler, useRealSetupRateLimit: true);

        using var firstRequest = CreateSetupSecretRequest(app.Client, "first-secret", "rotated-xsrf-one");
        using var firstResponse = await app.Client.SendAsync(firstRequest);
        using var secondRequest = CreateSetupSecretRequest(
            app.Client, "second-secret", "rotated-xsrf-two", "rotated-setup-cookie");
        using var secondResponse = await app.Client.SendAsync(secondRequest);

        await Assert.That(firstResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(secondResponse.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
        await Assert.That(secondResponse.Headers.Contains("Retry-After")).IsTrue();
        var body = await secondResponse.Content.ReadAsStringAsync();
        await Assert.That(body).Contains("Too Many Requests");
        await Assert.That(body).Contains("Too many setup-secret attempts");
        await Assert.That(body).DoesNotContain("second-secret");
        await Assert.That(handler.CallCount).IsEqualTo(1);
    }

    private static async Task<TestBffApp> CreateAppAsync(
        ValidateSecretHandler handler,
        bool useRealSetupRateLimit = false,
        ISetupSecretResolver? setupSecretResolver = null,
        string? authenticatedUserId = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing"
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();
        builder.Services.AddLogging();
        builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");

        if (useRealSetupRateLimit)
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:DisableInTesting"] = "false",
                ["RateLimiting:SetupSecret:PermitLimit"] = "1",
                ["RateLimiting:SetupSecret:WindowSeconds"] = "60"
            });
            builder.Services.AddBffRateLimiting(builder.Configuration, builder.Environment);
        }
        else
        {
            builder.Services.AddRateLimiter(options =>
            {
                options.AddPolicy(RateLimitingExtensions.SetupSecretPolicy, _ =>
                    RateLimitPartition.GetNoLimiter<string>("test"));
            });
        }

        builder.Services.AddSingleton<SetupSecretSessionService>();
        builder.Services.AddSingleton<ISetupSecretSessionService>(sp => sp.GetRequiredService<SetupSecretSessionService>());
        builder.Services.AddSingleton<ISetupSecretCookieProtector, PassThroughSetupSecretCookieProtector>();
        builder.Services.AddSingleton<ISetupSecretResolver>(setupSecretResolver ?? new EmptySetupSecretResolver());
        builder.Services.AddSingleton(new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://api.example.test/")
        });
        builder.Services.AddSingleton<IInstanceOnboardingClient>(services =>
            new InstanceOnboardingClient(services.GetRequiredService<HttpClient>()));

        var app = builder.Build();
        app.UseRouting();
        app.UseRateLimiter();
        if (!string.IsNullOrWhiteSpace(authenticatedUserId))
        {
            app.Use(async (context, next) =>
            {
                context.User = new ClaimsPrincipal(
                    new ClaimsIdentity(
                    [new Claim("sub", authenticatedUserId)],
                    authenticationType: "Cookies"));
                await next(context);
            });
        }
        app.MapGet("/test-antiforgery", (HttpContext context, IAntiforgery antiforgery) =>
            Results.Json(new { token = antiforgery.GetAndStoreTokens(context).RequestToken }));
        app.MapSetupSecretEndpoints();
        await app.StartAsync();
        var client = app.GetTestClient();
        using var issued = await client.GetAsync("/test-antiforgery");
        using var body = System.Text.Json.JsonDocument.Parse(await issued.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", body.RootElement.GetProperty("token").GetString());
        client.DefaultRequestHeaders.Add("Cookie", string.Join("; ", issued.Headers.GetValues("Set-Cookie").Select(value => value.Split(';')[0])));
        return new TestBffApp(app, client);
    }

    private static HttpRequestMessage CreateSetupSecretRequest(
        HttpClient client,
        string secret,
        string antiforgeryCookie,
        string? setupSecretCookie = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/bff/setup-secret")
        {
            Content = JsonContent.Create(new { secret })
        };
        request.Headers.Add("Cookie", string.Join("; ", client.DefaultRequestHeaders.GetValues("Cookie")) + "; " + (setupSecretCookie is null
            ? $"XSRF-TOKEN={antiforgeryCookie}"
            : $"XSRF-TOKEN={antiforgeryCookie}; setup-secret={setupSecretCookie}"));
        return request;
    }

    private sealed class TestBffApp(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
        }
    }

    private sealed class ValidateSecretHandler(HttpStatusCode statusCode, string responseJson) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public string? CapturedRequestBody { get; private set; }
        public string? CapturedSetupSecretHeader { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            CapturedRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            CapturedSetupSecretHeader = request.Headers.TryGetValues("X-Setup-Secret", out var values)
                ? values.SingleOrDefault()
                : null;

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class PassThroughSetupSecretCookieProtector : ISetupSecretCookieProtector
    {
        public string Protect(string secret) => secret.Trim();

        public bool TryUnprotect(string? protectedValue, out string? secret)
        {
            secret = protectedValue?.Trim();
            return !string.IsNullOrWhiteSpace(secret);
        }
    }

    private sealed class EmptySetupSecretResolver : ISetupSecretResolver
    {
        public SetupSecretResolutionResult Resolve(
            HttpContext? httpContext = null,
            HttpRequestMessage? outboundRequest = null)
        {
            return SetupSecretResolutionResult.NotFound("test_secret_missing");
        }
    }

    private sealed class StaticSetupSecretResolver(
        string secret,
        SetupSecretSource source = SetupSecretSource.ServerSideSetupSession) : ISetupSecretResolver
    {
        public SetupSecretResolutionResult Resolve(
            HttpContext? httpContext = null,
            HttpRequestMessage? outboundRequest = null) =>
            SetupSecretResolutionResult.FoundFrom(source, secret);
    }
}
