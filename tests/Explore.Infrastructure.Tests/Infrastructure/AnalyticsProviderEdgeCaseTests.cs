using System.Net;
using System.Text;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Models;
using Explore.Infrastructure.Analytics;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

public class AnalyticsProviderEdgeCaseTests
{
    [Test]
    public async Task PostHog_TrackAsync_WhenDisabled_DoesNotCallHttpApi()
    {
        var handler = new RecordingMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new HttpClient(handler);
        client.BaseAddress = new Uri("https://analytics.example.com");
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(client);
        var resolver = new StubAnalyticsConfigResolver(new AnalyticsConfiguration
        {
            Provider = Explore.Domain.Enums.AnalyticsProviderEnum.Posthog,
            IsEnabled = true,
            ApiKey = "public-key",
            EndpointUrl = "https://analytics.example.com"
        });
        var provider = new PostHogAnalyticsProvider(factory, resolver, Substitute.For<ILogger<PostHogAnalyticsProvider>>());

        resolver.SetEnabled(false);
        await provider.TrackAsync("user-1", "signup_completed");

        await Assert.That(handler.CallCount).IsEqualTo(0);
    }

    [Test]
    public async Task PostHog_IsFeatureEnabledAsync_WhenFeatureValueIsNotBoolean_ReturnsFalse()
    {
        const string responseJson = "{" +
                                    "\"featureFlags\":{" +
                                    "\"new_ui\":\"variant-a\"" +
                                    "}" +
                                    "}";

        var handler = new RecordingMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            });

        var client = new HttpClient(handler);
        client.BaseAddress = new Uri("https://analytics.example.com");
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(client);
        var resolver = new StubAnalyticsConfigResolver(new AnalyticsConfiguration
        {
            Provider = Explore.Domain.Enums.AnalyticsProviderEnum.Posthog,
            IsEnabled = true,
            ApiKey = "public-key",
            PersonalApiKey = "personal-key",
            EndpointUrl = "https://analytics.example.com"
        });
        var provider = new PostHogAnalyticsProvider(factory, resolver, Substitute.For<ILogger<PostHogAnalyticsProvider>>());

        var result = await provider.IsFeatureEnabledAsync("new_ui", "user-1");

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task PostHog_GetFeatureFlagPayloadAsync_WithoutPersonalApiKey_ReturnsNull()
    {
        var handler = new RecordingMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new HttpClient(handler);
        client.BaseAddress = new Uri("https://analytics.example.com");
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(client);
        var resolver = new StubAnalyticsConfigResolver(new AnalyticsConfiguration
        {
            Provider = Explore.Domain.Enums.AnalyticsProviderEnum.Posthog,
            IsEnabled = true,
            ApiKey = "public-key",
            EndpointUrl = "https://analytics.example.com"
        });
        var provider = new PostHogAnalyticsProvider(factory, resolver, Substitute.For<ILogger<PostHogAnalyticsProvider>>());

        var payload = await provider.GetFeatureFlagPayloadAsync("new_ui", "user-1");

        await Assert.That(payload).IsNull();
        await Assert.That(handler.CallCount).IsEqualTo(0);
    }

    [Test]
    public async Task Plausible_TrackAsync_WhenDisabled_DoesNotCallEventsApi()
    {
        var handler = new RecordingMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new HttpClient(handler);
        client.BaseAddress = new Uri("https://plausible.example.com");
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(client);
        var resolver = new StubAnalyticsConfigResolver(new AnalyticsConfiguration
        {
            Provider = Explore.Domain.Enums.AnalyticsProviderEnum.Plausible,
            IsEnabled = true,
            ApiKey = "example.com",
            EndpointUrl = "https://plausible.example.com"
        });
        var provider = new PlausibleAnalyticsProvider(factory, resolver, Substitute.For<ILogger<PlausibleAnalyticsProvider>>());

        resolver.SetEnabled(false);
        await provider.TrackAsync("user-1", "signup_completed");

        await Assert.That(handler.CallCount).IsEqualTo(0);
    }

    [Test]
    public async Task Plausible_TrackAsync_WhenApiKeyMissing_DoesNotCallEventsApi()
    {
        var handler = new RecordingMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new HttpClient(handler);
        client.BaseAddress = new Uri("https://plausible.example.com");
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(client);
        var resolver = new StubAnalyticsConfigResolver(new AnalyticsConfiguration
        {
            Provider = Explore.Domain.Enums.AnalyticsProviderEnum.Plausible,
            IsEnabled = true,
            ApiKey = string.Empty,
            EndpointUrl = "https://plausible.example.com"
        });
        var provider = new PlausibleAnalyticsProvider(factory, resolver, Substitute.For<ILogger<PlausibleAnalyticsProvider>>());

        await provider.TrackAsync("user-1", "signup_completed");

        await Assert.That(handler.CallCount).IsEqualTo(0);
    }

    private sealed class StubAnalyticsConfigResolver : IAnalyticsConfigResolver
    {
        private readonly AnalyticsConfiguration _config;

        public StubAnalyticsConfigResolver(AnalyticsConfiguration config)
        {
            _config = config;
        }

        public Task<AnalyticsConfiguration> ResolveAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_config);
        }

        public void SetEnabled(bool enabled)
        {
            _config.IsEnabled = enabled;
        }

        public void InvalidateCache(Guid? tenantId = null)
        {
        }
    }

    private sealed class RecordingMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _factory;

        public RecordingMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> factory)
        {
            _factory = factory;
        }

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_factory(request));
        }
    }
}
