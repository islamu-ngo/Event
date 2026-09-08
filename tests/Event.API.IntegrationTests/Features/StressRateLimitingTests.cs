using System.Net;
using System.Text;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

/// <summary>
/// Rate limiting stress tests using the Stress host profile.
/// Verifies repeated requests remain within expected behavior and, when throttling occurs,
/// that the runtime emits standard 429 metadata.
/// </summary>
[ClassDataSource<StressApiFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("StressDb")]
public class StressRateLimitingTests(StressApiFixture fixture)
{
    private readonly StressApiFixture _fixture = fixture;

    [Test]
    public async Task AuthenticatedEndpoint_RepeatedRequests_ShouldNotReturnServerErrors()
    {
        await _fixture.ResetDatabaseAsync();

        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var tenant = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);

        var userId = Guid.NewGuid();
        var observedStatuses = new List<HttpStatusCode>();

        var url = $"/api/admin/custom-property-projections/status?tenantId={tenant.TenantId}";

        for (var i = 0; i < 10; i++)
        {
            var request = _fixture.CreateAuthenticatedRequest(HttpMethod.Get, url, userId);

            var response = await _fixture.Client.SendAsync(request);
            observedStatuses.Add(response.StatusCode);
        }

        var unexpectedStatuses = observedStatuses
            .Where(status => status != HttpStatusCode.OK && status != HttpStatusCode.TooManyRequests)
            .ToList();

        await Assert.That(unexpectedStatuses).IsEmpty();
        await Assert.That(observedStatuses.Any(status => status == HttpStatusCode.OK)).IsTrue();
    }

    [Test]
    public async Task RateLimited_ShouldReturnRetryAfterHeader()
    {
        await _fixture.ResetDatabaseAsync();

        for (var index = 0; index < 3; index++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/instanceonboarding/validate-secret")
            {
                Content = new StringContent("{\"secret\":\"invalid-setup-secret\"}", Encoding.UTF8, "application/json")
            };
            using var response = await _fixture.Client.SendAsync(request);

            if (index < 2)
            {
                await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.TooManyRequests);
                continue;
            }

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
            await Assert.That(response.Headers.Contains("Retry-After")).IsTrue();
        }
    }

    // TODO: Re-enable once OpenFeature SDK ChannelClosedException on shutdown is resolved.
    // The test itself passes but WebApplicationFactory.DisposeAsync -> OpenFeature.Api.ShutdownAsync
    // throws ChannelClosedException during teardown, causing TUnit to report the test as failed.
    [Skip("Category: Stress. Removal: enable when OpenFeature SDK shutdown no longer throws ChannelClosedException during WebApplicationFactory disposal.")]
    [Test]
    public async Task RateLimited_ShouldReturnProblemDetailsBody()
    {
        await _fixture.ResetDatabaseAsync();

        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var tenant = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);

        var userId = Guid.NewGuid();
        HttpResponseMessage? rateLimitedResponse = null;

        var url = $"/api/admin/custom-property-projections/status?tenantId={tenant.TenantId}";

        for (var i = 0; i < 10; i++)
        {
            var request = _fixture.CreateAuthenticatedRequest(HttpMethod.Get, url, userId);

            var response = await _fixture.Client.SendAsync(request);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rateLimitedResponse = response;
                break;
            }
        }

        if (rateLimitedResponse is not null)
        {
            var content = await rateLimitedResponse.Content.ReadAsStringAsync();
            var json = JsonDocument.Parse(content);

            await Assert.That(json.RootElement.TryGetProperty("status", out var status)).IsTrue();
            await Assert.That(status.GetInt32()).IsEqualTo(429);

            await Assert.That(json.RootElement.TryGetProperty("title", out _)).IsTrue();
        }
    }

    [Test]
    public async Task GetEndpoint_Unauthenticated_ShouldNotBeRateLimited_ByWritePolicy()
    {
        // GET endpoints are AllowAnonymous and not subject to Write policy
        for (var i = 0; i < 10; i++)
        {
            var response = await _fixture.Client.GetAsync("/api/event");
            // GET should not hit write rate limit
            await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.TooManyRequests);
        }
    }

    [Test]
    public async Task OpenGraphImagePolicy_WithRetryAfter_IsClassifiedAsGlobal()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/api/event/public/example/og-image";

        var policyName = RateLimitingExtensions.InferPolicyName(context, hasRetryAfter: true);

        await Assert.That(policyName).IsEqualTo(RateLimitingExtensions.GlobalPolicy);
    }

    [Test]
    public async Task OpenGraphImagePolicy_WithoutRetryAfter_IsClassifiedAsOpenGraphConcurrency()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/api/event/public/example/og-image";

        var policyName = RateLimitingExtensions.InferPolicyName(context, hasRetryAfter: false);

        await Assert.That(policyName).IsEqualTo(RateLimitingExtensions.EventOpenGraphImagePolicy);
    }

    [Test]
    public async Task OpenGraphImage_WhenDifferentSlugIsAlreadyRendering_Returns429_ThenFirstRequestCompletes()
    {
        await _fixture.ResetDatabaseAsync();

        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var tenant = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);
        var events = await EventScenarioSeed.SeedMultiplePublishedEventsAsync(
            db,
            tenant.ActorId,
            tenant.TenantId,
            count: 2);

        var firstUrl = $"/api/event/public/event-{events[0].PublicCode}/og-image";
        var secondUrl = $"/api/event/public/event-{events[1].PublicCode}/og-image";
        var timeout = TimeSpan.FromSeconds(10);
        using var firstRequestCancellation = new CancellationTokenSource(timeout);
        using var secondRequestCancellation = new CancellationTokenSource(timeout);
        var firstRequest = _fixture.Client.GetAsync(firstUrl, firstRequestCancellation.Token);
        using var secondClient = _fixture.Factory.CreateClient();

        HttpResponseMessage? firstResponse = null;
        try
        {
            HttpResponseMessage secondResponse;
            try
            {
                await _fixture.WaitForFirstOpenGraphRenderAsync().WaitAsync(timeout);
                secondResponse = await secondClient.GetAsync(secondUrl, secondRequestCancellation.Token);
            }
            finally
            {
                _fixture.ReleaseFirstOpenGraphRender();
            }

            using (secondResponse)
            {
                firstResponse = await firstRequest;
                await Assert.That(firstResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
                await Assert.That(secondResponse.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
                await Assert.That(secondResponse.Headers.GetValues("X-RateLimit-Limit").Single()).IsEqualTo("1");
                await Assert.That(secondResponse.Headers.GetValues("X-RateLimit-Remaining").Single()).IsEqualTo("0");
                await Assert.That(secondResponse.Headers.Contains("Retry-After")).IsFalse();

                using var problem = JsonDocument.Parse(await secondResponse.Content.ReadAsStringAsync());
                await Assert.That(problem.RootElement.GetProperty("code").GetString())
                    .IsEqualTo(ApiProblemCodes.RateLimited);
                var detail = problem.RootElement.GetProperty("detail").GetString();
                await Assert.That(detail).IsEqualTo("Rate limit exceeded. Please try again later.");
                await Assert.That(detail!.Contains("Retry-After", StringComparison.OrdinalIgnoreCase)).IsFalse();
            }
        }
        finally
        {
            firstRequestCancellation.Cancel();
            if (firstResponse is null)
            {
                firstResponse = await firstRequest;
            }

            firstResponse?.Dispose();
        }
    }

    [Test]
    public async Task SetupSecretBudget_IsSharedAcrossSetupEndpointAndCanonicalProviderPatches_WhileBearerWritesRemainIndependent()
    {
        await _fixture.ResetDatabaseAsync();

        using (var request = new HttpRequestMessage(HttpMethod.Post, "/api/instanceonboarding/validate-secret")
        {
            Content = new StringContent("{\"secret\":\"invalid-setup-secret\"}", Encoding.UTF8, "application/json")
        })
        {
            using var response = await _fixture.Client.SendAsync(request);

            await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.TooManyRequests);
        }

        using (var request = new HttpRequestMessage(HttpMethod.Patch, "/api/instance/settings/auth-provider")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        })
        {
            request.Headers.Add("X-Setup-Secret", "invalid-setup-secret");
            using var response = await _fixture.Client.SendAsync(request);

            await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.TooManyRequests);
        }

        using (var request = new HttpRequestMessage(HttpMethod.Patch, "/api/instance/settings/auth-provider")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        })
        {
            request.Headers.Add("X-Setup-Secret", "invalid-setup-secret");
            using var response = await _fixture.Client.SendAsync(request);

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
            await Assert.That(response.Headers.GetValues("X-RateLimit-Limit").Single()).IsEqualTo("2");
        }

        var actorId = Guid.NewGuid();
        for (var i = 0; i < 3; i++)
        {
            using var request = _fixture.CreateInstanceAdminRequest(
                HttpMethod.Patch,
                "/api/instance/settings/auth-provider",
                actorId);
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            using var response = await _fixture.Client.SendAsync(request);

            await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.TooManyRequests);
        }

        using (var request = _fixture.CreateInstanceAdminRequest(
                   HttpMethod.Patch,
                   "/api/instance/settings/auth-provider",
                   actorId))
        {
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            using var response = await _fixture.Client.SendAsync(request);

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
            await Assert.That(response.Headers.GetValues("X-RateLimit-Limit").Single()).IsEqualTo("3");
        }
    }

    [Test]
    public async Task Complete_WithSetupSecretBudgetExhausted_ReachesSecretValidation()
    {
        await _fixture.ResetDatabaseAsync();

        for (var i = 0; i < 2; i++)
        {
            using var validationRequest = new HttpRequestMessage(HttpMethod.Post, "/api/instanceonboarding/validate-secret")
            {
                Content = new StringContent("{\"secret\":\"invalid-setup-secret\"}", Encoding.UTF8, "application/json")
            };
            using var validationResponse = await _fixture.Client.SendAsync(validationRequest);

            await Assert.That(validationResponse.StatusCode).IsNotEqualTo(HttpStatusCode.TooManyRequests);
        }

        using var request = _fixture.CreateInstanceAdminRequest(
            HttpMethod.Post,
            "/api/instanceonboarding/complete");
        request.Headers.Add("X-Setup-Secret", "invalid-setup-secret");
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        using var response = await _fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }
}
