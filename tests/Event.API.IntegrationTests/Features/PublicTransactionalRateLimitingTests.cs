using System.Net;
using Explore.API.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed class PublicTransactionalRateLimitingTests
{
    [Test]
    public async Task PublicTransactionalPolicy_WhenEnabled_ThrottlesAtConfiguredLimit()
    {
        await using var host = await RateLimitedApi.StartAsync(disableInTesting: false);

        using var first = await host.Client.PostAsync("/public-transactional", content: null);
        using var throttled = await host.Client.PostAsync("/public-transactional", content: null);

        await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(throttled.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
        var retryAfter = throttled.Headers.GetValues("Retry-After").Single();
        await Assert.That(int.TryParse(retryAfter, out var retryAfterSeconds)).IsTrue();
        await Assert.That(retryAfterSeconds).IsGreaterThan(0);
        await Assert.That(throttled.Headers.GetValues("X-RateLimit-Limit").Single()).IsEqualTo("1");
        await Assert.That(throttled.Headers.GetValues("X-RateLimit-Remaining").Single()).IsEqualTo("0");
    }

    [Test]
    public async Task PublicTransactionalPolicy_WhenTestingDisablesRateLimiting_DoesNotThrottle()
    {
        await using var host = await RateLimitedApi.StartAsync(disableInTesting: true);

        using var first = await host.Client.PostAsync("/public-transactional", content: null);
        using var second = await host.Client.PostAsync("/public-transactional", content: null);

        await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    private sealed class RateLimitedApi(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;

        public static async Task<RateLimitedApi> StartAsync(bool disableInTesting)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = "Testing"
            });
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:DisableInTesting"] = disableInTesting.ToString(),
                ["RateLimiting:PublicTransactional:PermitLimit"] = "1",
                ["RateLimiting:PublicTransactional:WindowSeconds"] = "60"
            });
            builder.Services.AddProblemDetails();
            builder.Services.AddApiRateLimiting(builder.Configuration, builder.Environment);

            var app = builder.Build();
            app.UseRateLimiter();
            app.MapPost("/public-transactional", () => Results.Ok())
                .RequireRateLimiting(RateLimitingExtensions.PublicTransactionalPolicy);

            await app.StartAsync();
            return new RateLimitedApi(app, app.GetTestClient());
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
        }
    }
}
