using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Models;
using Explore.Domain.Enums;
using Explore.Infrastructure.Analytics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

public class RuntimeAnalyticsProviderTests
{
    [Test]
    public async Task TrackAsync_ResolvesProvider_WhenEnabled()
    {
        var resolver = new MutableAnalyticsConfigResolver
        {
            Current = new AnalyticsConfiguration
            {
                Provider = AnalyticsProviderEnum.Posthog,
                IsEnabled = true,
                ApiKey = "public-key",
                EndpointUrl = "https://analytics.example.com"
            }
        };

        var provider = CreateRuntimeProvider(resolver);

        await provider.TrackAsync("user-1", "signup_completed");

        await Assert.That(resolver.ResolveCallCount).IsEqualTo(2);
    }

    [Test]
    public async Task IsFeatureEnabledAsync_ForProviderWithoutFeatureFlags_ReturnsFalse()
    {
        var resolver = new MutableAnalyticsConfigResolver
        {
            Current = new AnalyticsConfiguration
            {
                Provider = AnalyticsProviderEnum.Plausible,
                IsEnabled = true,
                ApiKey = "example.com",
                EndpointUrl = "https://plausible.example.com"
            }
        };

        var provider = CreateRuntimeProvider(resolver);

        var enabled = await provider.IsFeatureEnabledAsync("new_ui", "user-1");

        await Assert.That(enabled).IsFalse();
    }

    [Test]
    public async Task ResolveProvider_UsesCacheAcrossCalls()
    {
        var resolver = new MutableAnalyticsConfigResolver
        {
            Current = new AnalyticsConfiguration
            {
                Provider = AnalyticsProviderEnum.None,
                IsEnabled = false
            }
        };

        var provider = CreateRuntimeProvider(resolver);

        await provider.IdentifyAsync("user-1");
        await provider.PageViewAsync("user-1", "/home");

        await Assert.That(resolver.ResolveCallCount).IsEqualTo(2);
    }

    private static RuntimeAnalyticsProvider CreateRuntimeProvider(MutableAnalyticsConfigResolver resolver)
    {
        var postHogClient = new HttpClient(new StaticOkHandler()) { BaseAddress = new Uri("https://posthog") };
        var plausibleClient = new HttpClient(new StaticOkHandler()) { BaseAddress = new Uri("https://plausible") };
        var rybbitClient = new HttpClient(new StaticOkHandler()) { BaseAddress = new Uri("https://rybbit") };
        var rudderClient = new HttpClient(new StaticOkHandler()) { BaseAddress = new Uri("https://rudder") };

        var postHogFactory = Substitute.For<IHttpClientFactory>();
        postHogFactory.CreateClient(Arg.Any<string>()).Returns(postHogClient);

        var plausibleFactory = Substitute.For<IHttpClientFactory>();
        plausibleFactory.CreateClient(Arg.Any<string>()).Returns(plausibleClient);

        var rybbitFactory = Substitute.For<IHttpClientFactory>();
        rybbitFactory.CreateClient(Arg.Any<string>()).Returns(rybbitClient);

        var rudderFactory = Substitute.For<IHttpClientFactory>();
        rudderFactory.CreateClient(Arg.Any<string>()).Returns(rudderClient);

        var postHog = new PostHogAnalyticsProvider(postHogFactory, resolver, Substitute.For<ILogger<PostHogAnalyticsProvider>>());
        var plausible = new PlausibleAnalyticsProvider(plausibleFactory, resolver, Substitute.For<ILogger<PlausibleAnalyticsProvider>>());
        var rybbit = new RybbitAnalyticsProvider(rybbitFactory, resolver, Substitute.For<ILogger<RybbitAnalyticsProvider>>());
        var rudderStack = new RudderStackAnalyticsProvider(rudderFactory, resolver, Substitute.For<ILogger<RudderStackAnalyticsProvider>>());
        var nullProvider = new NullAnalyticsProvider(Substitute.For<ILogger<NullAnalyticsProvider>>());

        var cache = new MemoryCache(new MemoryCacheOptions());

        return new RuntimeAnalyticsProvider(
            postHog,
            plausible,
            rybbit,
            rudderStack,
            nullProvider,
            resolver,
            cache,
            Substitute.For<ILogger<RuntimeAnalyticsProvider>>());
    }

    private sealed class MutableAnalyticsConfigResolver : IAnalyticsConfigResolver
    {
        public AnalyticsConfiguration Current { get; set; } = new();

        public int ResolveCallCount { get; private set; }

        public Task<AnalyticsConfiguration> ResolveAsync(CancellationToken cancellationToken = default)
        {
            ResolveCallCount++;
            return Task.FromResult(Current);
        }

        public void InvalidateCache(Guid? tenantId = null)
        {
        }
    }

    private sealed class StaticOkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }
}
