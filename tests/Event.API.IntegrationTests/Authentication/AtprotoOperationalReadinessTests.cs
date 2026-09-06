// ABOUTME: Verifies operational ATProto health over the real BFF, private API and PostgreSQL store.
// ABOUTME: Uses an injected clock and reversible database outage to separate readiness from liveness.

extern alias bff;

using System.Net;
using Explore.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Http;
using BffAuth = bff::Explore.Blazor.Services.Auth;

namespace Event.API.IntegrationTests.Authentication;

[NotInParallel("AtprotoTransientApi")]
public sealed class AtprotoOperationalReadinessTests
{
    [Test]
    public async Task ConcurrentColdReadinessChecksShareOneProbeAndPreserveLoginAdmission()
    {
        await using var fixture = new AtprotoRelationalLoginFixture { DisableRateLimiting = false };
        await fixture.InitializeAsync();
        using var barrier = new ProbeBarrier();
        await using var bff = fixture.CreateBff();
        await using var host = bff.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.PostConfigure<HttpClientFactoryOptions>(BffAuth.ApiBackedAtprotoTransientStore.HttpClientName,
                options => options.HttpMessageHandlerBuilderActions.Add(handler =>
                {
                    barrier.InnerHandler = handler.PrimaryHandler;
                    handler.PrimaryHandler = barrier;
                }))));
        var readiness = host.Services.GetRequiredService<BffAuth.IBffProviderReadinessService>();
        var first = readiness.GetProviderReadinessAsync(bff::Explore.Blazor.Constants.AuthSchemeNames.Atproto, CancellationToken.None);
        await barrier.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var cancelledWaiter = new CancellationTokenSource();
        var cancelled = readiness.GetProviderReadinessAsync(bff::Explore.Blazor.Constants.AuthSchemeNames.Atproto, cancelledWaiter.Token);
        cancelledWaiter.Cancel();
        await Assert.That(async () => await cancelled).Throws<OperationCanceledException>();
        var followers = Enumerable.Range(0, 64).Select(_ => readiness.GetProviderReadinessAsync(
            bff::Explore.Blazor.Constants.AuthSchemeNames.Atproto, CancellationToken.None)).ToArray();
        // Observe real outbound requests while transport is held, not calls to a mocked readiness collaborator.
        int concurrentAttempts = barrier.Attempts;
        barrier.Release.TrySetResult();
        var results = await Task.WhenAll(followers.Prepend(first));
        await Assert.That(concurrentAttempts).IsEqualTo(1);
        await Assert.That(results.All(result => result.IsReady)).IsTrue();
        var store = host.Services.GetRequiredService<BffAuth.ApiBackedAtprotoTransientStore>();
        await Assert.That(await store.CreateAsync("oauth_state", Guid.CreateVersion7().ToString("N"), fixture.TenantId,
            [1, 2, 3], DateTimeOffset.UtcNow.AddMinutes(1))).IsTrue();
    }

    [Test]
    [Arguments("atproto")]
    [Arguments("local")]
    [Arguments("keycloak")]
    public async Task HttpReadinessExpiresCachedSuccessDuringStoreOutage_WithoutFailingOtherProvidersOrLiveness(string primaryProvider)
    {
        var clock = new ProbeClock();
        await using var fixture = new AtprotoRelationalLoginFixture { Clock = clock };
        await fixture.InitializeAsync();
        await using var bff = fixture.CreateBff();
        await using var host = bff.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Authentication:Provider"] = primaryProvider }));
            builder.ConfigureTestServices(services =>
            services.PostConfigure<HealthCheckServiceOptions>(options =>
            {
                // Exercise the actual named check and HTTP writer; unrelated OIDC/cache availability is not this assertion.
                foreach (var registration in options.Registrations
                    .Where(registration => registration.Name != "atproto-authentication" && !registration.Tags.Contains("live")).ToArray())
                    options.Registrations.Remove(registration);
            }));
        });
        using var client = host.CreateClient(new()
        {
            BaseAddress = new Uri(AtprotoRelationalLoginFixture.CanonicalOrigin), AllowAutoRedirect = false
        });
        using var healthy = await client.GetAsync("/health");
        await Assert.That(healthy.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(host.Services.GetRequiredService<bff::Explore.Blazor.Services.IDynamicAuthSchemeManager>()
            .GetActivePrimaryProvider()).IsEqualTo(primaryProvider);
        await using var scope = fixture.Api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE islamu_event.atproto_transient_records RENAME TO readiness_store_unavailable");
        try
        {
            using var cachedSuccess = await client.GetAsync("/health");
            await Assert.That(cachedSuccess.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(await cachedSuccess.Content.ReadAsStringAsync()).DoesNotContain("state_store_unavailable");
            clock.Advance(TimeSpan.FromSeconds(11));
            using var unavailable = await client.GetAsync("/health");
            await Assert.That(unavailable.StatusCode).IsEqualTo(primaryProvider == "atproto"
                ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
            string output = await unavailable.Content.ReadAsStringAsync();
            await Assert.That(output.Contains("state_store_unavailable", StringComparison.Ordinal)).IsTrue();
            await Assert.That(output.Contains("readiness_store_unavailable", StringComparison.Ordinal)).IsFalse();
            await Assert.That(output.Contains("atproto_transient_records", StringComparison.Ordinal)).IsFalse();
            using var alive = await client.GetAsync("/alive");
            await Assert.That(alive.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE islamu_event.readiness_store_unavailable RENAME TO atproto_transient_records");
        }
        using var cachedFailure = await client.GetAsync("/health");
        await Assert.That(await cachedFailure.Content.ReadAsStringAsync()).Contains("state_store_unavailable");
        clock.Advance(TimeSpan.FromSeconds(11));
        using var recovered = await client.GetAsync("/health");
        await Assert.That(recovered.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    private sealed class ProbeBarrier : DelegatingHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int attempts;
        public int Attempts => Volatile.Read(ref attempts);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath == "/api/auth/atproto/transient/probe")
            {
                Interlocked.Increment(ref attempts);
                Started.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return await base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class ProbeClock : TimeProvider
    {
        private long offsetTicks;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.AddTicks(Interlocked.Read(ref offsetTicks));
        public void Advance(TimeSpan duration) => Interlocked.Add(ref offsetTicks, duration.Ticks);
    }
}
