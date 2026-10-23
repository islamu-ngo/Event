using System.Net;
using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Models;
using Explore.Application.Settings;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Analytics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class AnalyticsDiagnosticConfidentialityTests
{
    [Test]
    public async Task ConfigurationResolutionRetainsTypedStateWithoutTenantIdentity()
    {
        var tenantId = Guid.NewGuid();
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var settings = Substitute.For<IHierarchicalSettingsResolver>();
        settings.ResolveAsync<string>(GovernanceSettingKeys.Analytics.Provider,
                Arg.Any<SettingContext>(), Arg.Any<CancellationToken>())
            .Returns("none");
        var secrets = Substitute.For<ISecretResolver>();
        secrets.ResolveAsync(SecretDefinitionRegistry.Keys.Analytics.PersonalApiKey,
                tenantId, Arg.Any<CancellationToken>())
            .Returns(SecretResolutionResult.Unconfigured);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var logger = new TestListLogger<AnalyticsConfigResolver>();
        var resolver = new AnalyticsConfigResolver(settings, tenant, cache, secrets, logger);

        var configuration = await resolver.ResolveAsync();

        await Assert.That(configuration.IsEnabled).IsFalse();
        await Assert.That(logger.Entries.Count).IsEqualTo(1);
        await AssertSafeEntries(logger.Entries, tenantId.ToString());
        await Assert.That(logger.Entries.Single().State.Any(property =>
            property.Key == "Provider" && Equals(property.Value, configuration.Provider))).IsTrue();
        await Assert.That(logger.Entries.Single().State.Any(property =>
            property.Key == "Enabled" && Equals(property.Value, configuration.IsEnabled))).IsTrue();
    }

    [Test]
    public async Task DisabledAnalyticsRetainsOperationDiagnosticsWithoutCallerValues()
    {
        var logger = new TestListLogger<NullAnalyticsProvider>();
        var provider = new NullAnalyticsProvider(logger);
        var canary = $"{Guid.NewGuid():N}\r\nforged-event";

        await provider.IdentifyAsync(canary, new Dictionary<string, object> { [canary] = canary });
        await provider.TrackAsync(canary, canary);
        await provider.PageViewAsync(canary, canary);
        await provider.GroupIdentifyAsync(canary, canary);
        await Assert.That(await provider.IsFeatureEnabledAsync(canary, canary)).IsFalse();
        await Assert.That(await provider.GetFeatureFlagPayloadAsync(canary, canary)).IsNull();

        await Assert.That(logger.Entries.Count).IsEqualTo(4);
        await AssertSafeEntries(logger.Entries, canary);
        await Assert.That(logger.Entries.Select(entry => entry.Message).Distinct().Count()).IsEqualTo(4);
    }

    [Test]
    public async Task ProviderFailuresRetainCategoriesWithoutExceptionOrRequestContent()
    {
        var canary = $"{Guid.NewGuid():N}\r\nforged-event";
        using var handler = new FailingTransport(canary);
        using var factory = new ClientFactory(handler);
        var posthogLog = new TestListLogger<PostHogAnalyticsProvider>();
        var plausibleLog = new TestListLogger<PlausibleAnalyticsProvider>();
        var rybbitLog = new TestListLogger<RybbitAnalyticsProvider>();
        var rudderLog = new TestListLogger<RudderStackAnalyticsProvider>();
        var posthog = new PostHogAnalyticsProvider(factory, Configuration(AnalyticsProviderEnum.Posthog, canary), posthogLog);
        var plausible = new PlausibleAnalyticsProvider(factory, Configuration(AnalyticsProviderEnum.Plausible, canary), plausibleLog);
        var rybbit = new RybbitAnalyticsProvider(factory, Configuration(AnalyticsProviderEnum.Rybbit, canary), rybbitLog);
        var rudder = new RudderStackAnalyticsProvider(factory, Configuration(AnalyticsProviderEnum.RudderStack, canary), rudderLog);

        await posthog.TrackAsync(canary, canary);
        await Assert.That(await posthog.IsFeatureEnabledAsync(canary, canary)).IsFalse();
        await Assert.That(await posthog.GetFeatureFlagPayloadAsync(canary, canary)).IsNull();
        await plausible.TrackAsync(canary, canary);
        await rybbit.PageViewAsync(canary, canary);
        await rudder.GroupIdentifyAsync(canary, canary);

        var entries = posthogLog.Entries.Concat(plausibleLog.Entries)
            .Concat(rybbitLog.Entries).Concat(rudderLog.Entries).ToArray();
        await Assert.That(entries.Length).IsEqualTo(6);
        await Assert.That(entries.All(entry => entry.Level == LogLevel.Warning)).IsTrue();
        await AssertSafeEntries(entries, canary);
        await Assert.That(entries.Select(entry => entry.Message).Distinct().Count()).IsEqualTo(6);
        await Assert.That(handler.RequestCount).IsEqualTo(6);
    }

    [Test]
    public async Task RuntimeResolutionFailurePreservesDisabledFallbackWithoutExceptionExports()
    {
        var canary = $"{Guid.NewGuid():N}\r\nforged-event";
        using var handler = new FailingTransport(canary);
        using var factory = new ClientFactory(handler);
        var resolver = new ThrowingResolver(canary);
        var runtimeLog = new TestListLogger<RuntimeAnalyticsProvider>();
        var nullLog = new TestListLogger<NullAnalyticsProvider>();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var runtime = new RuntimeAnalyticsProvider(
            new PostHogAnalyticsProvider(factory, resolver, new TestListLogger<PostHogAnalyticsProvider>()),
            new PlausibleAnalyticsProvider(factory, resolver, new TestListLogger<PlausibleAnalyticsProvider>()),
            new RybbitAnalyticsProvider(factory, resolver, new TestListLogger<RybbitAnalyticsProvider>()),
            new RudderStackAnalyticsProvider(factory, resolver, new TestListLogger<RudderStackAnalyticsProvider>()),
            new NullAnalyticsProvider(nullLog), resolver, cache, runtimeLog);

        await runtime.TrackAsync(canary, canary);
        await Assert.That(await runtime.IsFeatureEnabledAsync(canary, canary)).IsFalse();
        await Assert.That(await runtime.GetFeatureFlagPayloadAsync(canary, canary)).IsNull();

        await Assert.That(runtimeLog.Entries.Count).IsEqualTo(3);
        await Assert.That(nullLog.Entries.Count).IsEqualTo(1);
        await AssertSafeEntries(runtimeLog.Entries.Concat(nullLog.Entries), canary);
    }

    private static async Task AssertSafeEntries(IEnumerable<TestLogEntry> entries, string canary)
    {
        foreach (var entry in entries)
        {
            await Assert.That(entry.Exception).IsNull();
            var exported = entry.Message + JsonSerializer.Serialize(entry.State);
            await Assert.That(exported.Contains(canary, StringComparison.Ordinal)).IsFalse();
            await Assert.That(exported.Contains(canary[..32], StringComparison.Ordinal)).IsFalse();
            await Assert.That(entry.Message.Contains('\r') || entry.Message.Contains('\n')).IsFalse();
        }
    }

    private static IAnalyticsConfigResolver Configuration(AnalyticsProviderEnum provider, string canary) =>
        new SelectedConfiguration(new AnalyticsConfiguration
        {
            Provider = provider,
            IsEnabled = true,
            ApiKey = canary[..32],
            PersonalApiKey = canary[..32],
            EndpointUrl = $"https://{canary[..32]}.example.test"
        });

    private sealed class SelectedConfiguration(AnalyticsConfiguration configuration) : IAnalyticsConfigResolver
    {
        public Task<AnalyticsConfiguration> ResolveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(configuration);

        public void InvalidateCache(Guid? tenantId = null) { }
    }

    private sealed class ThrowingResolver(string canary) : IAnalyticsConfigResolver
    {
        public Task<AnalyticsConfiguration> ResolveAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<AnalyticsConfiguration>(new InvalidOperationException(canary));

        public void InvalidateCache(Guid? tenantId = null) { }
    }

    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory, IDisposable
    {
        private readonly List<HttpClient> _clients = [];
        public HttpClient CreateClient(string name)
        {
            var client = new HttpClient(handler, disposeHandler: false);
            _clients.Add(client);
            return client;
        }

        public void Dispose()
        {
            foreach (var client in _clients) client.Dispose();
        }
    }

    private sealed class FailingTransport(string canary) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromException<HttpResponseMessage>(
                new HttpRequestException(canary, new InvalidOperationException(canary), HttpStatusCode.ServiceUnavailable));
        }
    }
}
