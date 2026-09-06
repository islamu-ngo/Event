// ABOUTME: Exercises backend-independent ATProto challenge validation at the BFF HTTP boundary.
// ABOUTME: Keeps antiforgery, rate limits and pre-readiness admission outside the relational login preflight tests.

using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CarpaNet.Identity;
using Explore.Blazor.Authentication;
using Explore.Blazor.Constants;
using Explore.Blazor.IntegrationTests.Fixtures;
using Explore.Blazor.Services;
using Explore.Blazor.Services.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Explore.Blazor.IntegrationTests.Endpoints;

public sealed class AtprotoAuthenticationFlowTests
{
    private const string CanonicalOrigin = "https://events.example.com";
    private static readonly Guid TenantId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000001");

    [Test]
    public async Task ChallengeIsPostOnlyAndRejectsMissingAntiforgery()
    {
        await using var factory = CreateFactory();
        var endpoint = GetChallengeEndpoint(factory);
        await Assert.That(endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.SequenceEqual(["POST"])).IsTrue();
        var response = await InvokeChallengeAsync(factory, endpoint, "{\"handle\":\"alice.example\",\"classification\":\"person\"}");
        await Assert.That(response.StatusCode).IsEqualTo(StatusCodes.Status400BadRequest);
        await Assert.That(response.Body).Contains("Antiforgery validation failed");
    }

    [Test]
    public async Task AtprotoChallengeAndCallbackUseRegisteredBoundedHttpRateLimitPolicy()
    {
        await using (var factory = CreateFactory(enableRealAtprotoRateLimit: true))
        {
            using var client = CreateClient(factory);
            using var firstBody = new StringContent("{\"handle\":\"alice.example\",\"classification\":\"person\"}", Encoding.UTF8, "application/json");
            using var first = await client.PostAsync("/auth/atproto/challenge", firstBody);
            using var secondBody = new StringContent("{\"handle\":\"alice.example\",\"classification\":\"person\"}", Encoding.UTF8, "application/json");
            using var second = await client.PostAsync("/auth/atproto/challenge", secondBody);
            await Assert.That(first.StatusCode).IsNotEqualTo(HttpStatusCode.TooManyRequests);
            await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
        }
        await using (var factory = CreateFactory(enableRealAtprotoRateLimit: true))
        {
            using var client = CreateClient(factory);
            using var first = await client.GetAsync("/signin-atproto");
            using var second = await client.GetAsync("/signin-atproto");
            await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
            await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
        }
    }

    [Test]
    public async Task ConfiguredKeycloakPendingRejectsAtprotoChallenge()
    {
        await using var factory = CreateFactory(bypassAntiforgery: true, onboardingStatus: new(false,
            "ConfiguredAdministratorPending", "ConfiguredAdministrator", "Keycloak", 1, BffOnboardingDisposition.ConfiguredAdministratorPending));
        var response = await InvokeChallengeAsync(factory, GetChallengeEndpoint(factory), "{\"handle\":\"alice.example\",\"classification\":\"person\"}");
        await Assert.That(response.StatusCode).IsEqualTo(StatusCodes.Status403Forbidden);
        await Assert.That(response.Location).IsNull();
    }

    [Test]
    public async Task CanonicalActorTargetChallengeRejectsHalfOrEmptyPairBeforeOAuthStateCreation()
    {
        await using var factory = CreateFactory();
        Guid actor = Guid.CreateVersion7();
        Guid stamp = Guid.CreateVersion7();
        foreach (string payload in new[]
        {
            JsonSerializer.Serialize(new { handle = "alice.example", classification = "person", canonicalActorId = actor }),
            JsonSerializer.Serialize(new { handle = "alice.example", classification = "person", expectedCanonicalActorConcurrencyStamp = stamp }),
            JsonSerializer.Serialize(new { handle = "alice.example", classification = "person", canonicalActorId = Guid.Empty, expectedCanonicalActorConcurrencyStamp = stamp })
        })
        {
            var response = await ChallengeWithAntiforgeryAsync(factory, payload);
            await Assert.That(response.StatusCode).IsEqualTo(StatusCodes.Status400BadRequest);
            await Assert.That(response.Location).IsNull();
        }
    }

    private static WebApplicationFactory<Program> CreateFactory(bool bypassAntiforgery = false,
        bool enableRealAtprotoRateLimit = false, BffOnboardingStatus? onboardingStatus = null) =>
        new BlazorBffWebApplicationFactory().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Atproto:PublicUrl", CanonicalOrigin);
            builder.UseSetting("Atproto:CallbackPath", "/signin-atproto");
            builder.UseSetting("Explore:MultiTenancy:DefaultTenantId", TenantId.ToString("D"));
            builder.UseSetting("Explore:MultiTenancy:DefaultTenant", "default");
            if (enableRealAtprotoRateLimit)
            {
                builder.UseSetting("RateLimiting:DisableInTesting", "false");
                builder.UseSetting("RateLimiting:AtprotoAuthentication:PermitLimit", "1");
                builder.UseSetting("RateLimiting:AtprotoAuthentication:WindowSeconds", "60");
            }
            builder.ConfigureTestServices(services =>
            {
                services.Configure<AtprotoClientKeyOptions>(options => options.OAuthClientPrivateJwks = CreatePrivateJwks());
                services.AddAuthentication().AddScheme<AtprotoAuthenticationOptions, AtprotoAuthenticationHandler>(AuthSchemeNames.Atproto, _ => { });
                services.RemoveAll<IAtprotoOAuthTransportFactory>();
                services.AddSingleton<IAtprotoOAuthTransportFactory>(new RejectedDiscoveryTransport());
                if (bypassAntiforgery)
                {
                    services.RemoveAll<IBffSelfCallTokenService>();
                    var selfCall = Substitute.For<IBffSelfCallTokenService>();
                    selfCall.Validate(Arg.Any<HttpContext>()).Returns(true);
                    services.AddSingleton(selfCall);
                }
                services.RemoveAll<IBffOnboardingStatusProvider>();
                var onboarding = Substitute.For<IBffOnboardingStatusProvider>();
                onboarding.GetStatusAsync(Arg.Any<CancellationToken>()).Returns(onboardingStatus
                    ?? new(true, "Completed", "Interactive", null, 1, BffOnboardingDisposition.Completed));
                services.AddSingleton(onboarding);
            });
        });

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) => factory.CreateClient(new()
    {
        AllowAutoRedirect = false, BaseAddress = new(CanonicalOrigin), HandleCookies = true
    });

    private static async Task<EndpointResponse> ChallengeWithAntiforgeryAsync(WebApplicationFactory<Program> factory, string payload)
    {
        using var client = CreateClient(factory);
        using var status = await client.GetAsync("/auth/status");
        await Assert.That(status.StatusCode).IsEqualTo(HttpStatusCode.OK);
        string[] cookies = status.Headers.GetValues("Set-Cookie").ToArray();
        client.DefaultRequestHeaders.Add("Cookie", string.Join("; ", cookies.Select(cookie => cookie.Split(';', 2)[0])));
        string xsrf = cookies.Single(cookie => cookie.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal)).Split(';', 2)[0]["XSRF-TOKEN=".Length..];
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/atproto/challenge")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-CSRF-TOKEN", Uri.UnescapeDataString(xsrf));
        using var response = await client.SendAsync(request);
        return new((int)response.StatusCode, response.Headers.Location?.OriginalString, await response.Content.ReadAsStringAsync());
    }

    private static RouteEndpoint GetChallengeEndpoint(WebApplicationFactory<Program> factory) => factory.Services.GetServices<EndpointDataSource>()
        .SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().Single(endpoint => endpoint.RoutePattern.RawText == "/auth/atproto/challenge");

    private static async Task<EndpointResponse> InvokeChallengeAsync(WebApplicationFactory<Program> factory, RouteEndpoint endpoint, string payload)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.SetEndpoint(endpoint);
        context.Request.Method = HttpMethods.Post;
        context.Request.Scheme = "https";
        context.Request.Host = new("events.example.com");
        context.Request.Path = "/auth/atproto/challenge";
        context.Request.ContentType = "application/json";
        byte[] bytes = Encoding.UTF8.GetBytes(payload);
        context.Request.ContentLength = bytes.Length;
        context.Request.Body = new MemoryStream(bytes);
        context.Response.Body = new MemoryStream();
        await endpoint.RequestDelegate!(context);
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        return new(context.Response.StatusCode, context.Response.Headers.Location.ToString() is { Length: > 0 } location ? location : null,
            await reader.ReadToEndAsync());
    }

    private static string CreatePrivateJwks()
    {
        using var signing = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var key = signing.ExportParameters(true);
        string Encode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return JsonSerializer.Serialize(new { keys = new[] { new
        {
            kty = "EC", crv = "P-256", x = Encode(key.Q.X!), y = Encode(key.Q.Y!), d = Encode(key.D!), kid = "oauth-active", use = "sig", alg = "ES256", status = "active"
        } } });
    }

    private sealed class RejectedDiscoveryTransport : IAtprotoOAuthTransportFactory, IDnsResolver
    {
        public HttpMessageHandler CreatePrimaryHandler(Explore.Atproto.Transport.AtprotoOutboundPolicy policy, TimeSpan connectTimeout) =>
            throw new InvalidOperationException("Invalid input must fail before external discovery.");
        public IDnsResolver CreateDnsResolver() => this;
        public Task<IReadOnlyList<string>> GetTxtRecordsAsync(string name, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(name == "_atproto.alice.example" ? ["did=did:plc:alice"] : []);
    }

    private sealed record EndpointResponse(int StatusCode, string? Location, string Body);
}
