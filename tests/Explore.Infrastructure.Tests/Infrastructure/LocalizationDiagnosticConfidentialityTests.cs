using System.Diagnostics.Metrics;
using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Telemetry;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Localization;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class LocalizationDiagnosticConfidentialityTests
{
    [Test]
    public async Task TmsFailuresAndCacheFallbackKeepUsefulCategoriesWithoutProviderOrLanguageContent()
    {
        var canary = $"{Guid.NewGuid():N}\r\nprivate-provider";
        var tenantId = Guid.NewGuid();
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var secrets = Substitute.For<ISecretResolver>();
        secrets.ResolveAsync(SecretDefinitionRegistry.Keys.Localization.TmsApiKey, tenantId, Arg.Any<CancellationToken>())
            .Returns(SecretResolutionResult.Resolved(new ResolvedSecret(
                SecretDefinitionRegistry.Keys.Localization.TmsApiKey, Guid.NewGuid().ToString("N"),
                SecretSourceType.EnvironmentVariable, SecretScope.Tenant, tenantId, DateTimeOffset.UtcNow)));
        using var transport = new FailingTransport(canary);
        using var http = new ClientFactory(transport);
        var configuration = new SelectedConfiguration();
        var tolgeeLog = new TestListLogger<TolgeeTranslationProvider>();
        var weblateLog = new TestListLogger<WeblateTranslationProvider>();
        var runtimeLog = new TestListLogger<RuntimeTranslationProvider>();
        var offlineLog = new TestListLogger<OfflineTranslationProvider>();
        var resolverLog = new TestListLogger<TranslationResolver>();
        var tolgee = new TolgeeTranslationProvider(http, configuration, secrets, tenant, tolgeeLog);
        var weblate = new WeblateTranslationProvider(http, configuration, secrets, tenant, weblateLog);
        var offline = new OfflineTranslationProvider(offlineLog);
        using var meters = new IsolatedMeterFactory();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var runtime = new RuntimeTranslationProvider(tolgee, weblate, offline,
            new NullTranslationProvider(new TestListLogger<NullTranslationProvider>()),
            configuration, new TranslationMetrics(meters), runtimeLog);
        var resolver = new TranslationResolver(runtime, configuration, tenant, cache, resolverLog);

        await Assert.That(await tolgee.TestConnectionAsync()).IsFalse();
        configuration.Provider = TranslationManagementProviderEnum.Weblate;
        await Assert.That(await weblate.TestConnectionAsync()).IsFalse();
        configuration.Provider = TranslationManagementProviderEnum.Tolgee;
        await runtime.ImportKeysAsync([new TranslationKeyImport("ui.test", new Dictionary<string, string> { ["en"] = canary })]);
        await Assert.That((await runtime.ExportTranslationsAsync(canary)).Count()).IsEqualTo(0);
        var english = (await runtime.ExportTranslationsAsync("en")).ToArray();
        await Assert.That(english.Length).IsGreaterThan(0);
        await Assert.That(await resolver.ResolveAsync(canary, canary)).IsEqualTo(canary);
        await resolver.InvalidateLanguageAsync(canary);

        await Assert.That(tolgeeLog.Entries.Count).IsEqualTo(1);
        await Assert.That(weblateLog.Entries.Count).IsEqualTo(1);
        await Assert.That(runtimeLog.Entries.Count).IsEqualTo(4);
        await Assert.That(runtimeLog.Entries.Any(entry => entry.Arguments.Contains("network_error"))).IsTrue();
        await Assert.That(resolverLog.Entries.Count).IsEqualTo(2);
        var entries = tolgeeLog.Entries.Concat(weblateLog.Entries).Concat(runtimeLog.Entries)
            .Concat(offlineLog.Entries).Concat(resolverLog.Entries);
        foreach (var entry in entries)
        {
            await Assert.That(entry.Exception).IsNull();
            var finalOutput = entry.Message + JsonSerializer.Serialize(entry.State);
            foreach (var privateValue in new[] { canary[..32], tenantId.ToString() })
                await Assert.That(finalOutput.Contains(privateValue, StringComparison.Ordinal)).IsFalse();
        }
    }

    [Test]
    public async Task UnknownLanguageCannotEnterOfflineOrDisabledDiagnostics()
    {
        var canary = $"{Guid.NewGuid():N}\r\nforged-event";
        var offlineLog = new TestListLogger<OfflineTranslationProvider>();
        var nullLog = new TestListLogger<NullTranslationProvider>();
        var offline = new OfflineTranslationProvider(offlineLog);
        var disabled = new NullTranslationProvider(nullLog);

        await Assert.That((await offline.ExportTranslationsAsync(canary)).Count()).IsEqualTo(0);
        offline.InvalidateLanguage(canary);
        await Assert.That((await disabled.ExportTranslationsAsync(canary)).Count()).IsEqualTo(0);
        var english = (await offline.ExportTranslationsAsync("en")).ToArray();

        await Assert.That(english.Length).IsGreaterThan(0);
        await Assert.That(offlineLog.Entries.Count).IsGreaterThanOrEqualTo(3);
        await Assert.That(nullLog.Entries.Count).IsEqualTo(1);
        foreach (var entry in offlineLog.Entries.Concat(nullLog.Entries))
        {
            await Assert.That(entry.Exception).IsNull();
            var exported = entry.Message + JsonSerializer.Serialize(entry.State);
            await Assert.That(exported.Contains(canary[..32], StringComparison.Ordinal)).IsFalse();
            await Assert.That(entry.Message.Contains('\r') || entry.Message.Contains('\n')).IsFalse();
        }
        await Assert.That(offlineLog.Entries.Any(entry => entry.Arguments.Contains(english.Length))).IsTrue();
    }

    [Test]
    public async Task TranslationMetricCollectorRetainsCountsAndRegisteredCulturesWithoutPrivateLabels()
    {
        using var factory = new IsolatedMeterFactory();
        var measurements = new List<(string Name, double Value, KeyValuePair<string, object?>[] Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, current) =>
        {
            if (ReferenceEquals(instrument.Meter, factory.Meter))
                current.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            measurements.Add((instrument.Name, value, tags.ToArray())));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            measurements.Add((instrument.Name, value, tags.ToArray())));
        listener.Start();
        var metrics = new TranslationMetrics(factory);
        var canary = $"{Guid.NewGuid():N}\r\nprivate-label";

        metrics.RecordFetch(nameof(OfflineTranslationProvider), canary, "error");
        metrics.RecordFetchDuration(nameof(OfflineTranslationProvider), canary, 0.25);
        metrics.RecordLanguageChange(canary, "fr");
        metrics.RecordConnectionTest(nameof(OfflineTranslationProvider), "success");
        metrics.RecordFallbackActivated(nameof(OfflineTranslationProvider), "network_error");
        metrics.RecordStaticBundleOperation("export_from_tms", canary, "provider_error");
        metrics.RecordFetch(nameof(OfflineTranslationProvider), "en", "hit_offline");

        await Assert.That(measurements.Count).IsEqualTo(7);
        await Assert.That(measurements.Count(measurement => measurement.Value == 1)).IsEqualTo(6);
        await Assert.That(measurements.Any(measurement => measurement.Value == 0.25)).IsTrue();
        await Assert.That(JsonSerializer.Serialize(measurements.Select(measurement => measurement.Tags))
            .Contains(canary[..32], StringComparison.Ordinal)).IsFalse();
        await Assert.That(measurements.Any(measurement =>
            measurement.Tags.Contains(new KeyValuePair<string, object?>("language", "en")))).IsTrue();
        await Assert.That(measurements.Any(measurement =>
            measurement.Tags.Contains(new KeyValuePair<string, object?>("to", "fr")))).IsTrue();
        await Assert.That(measurements.Any(measurement =>
            measurement.Tags.Contains(new KeyValuePair<string, object?>("result", "provider_error")))).IsTrue();
    }

    private sealed class IsolatedMeterFactory : IMeterFactory
    {
        public Meter Meter { get; } = new(TranslationMetrics.MeterName);
        public Meter Create(MeterOptions options) => Meter;
        public void Dispose() => Meter.Dispose();
    }

    private sealed class SelectedConfiguration : ITranslationConfigResolver
    {
        public TranslationManagementProviderEnum Provider { get; set; } = TranslationManagementProviderEnum.Tolgee;
        public Task<TranslationConfiguration> ResolveAsync(CancellationToken ct = default) =>
            Task.FromResult(new TranslationConfiguration(Provider, "https://tms.example.test", "1", "component", "en"));
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
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException(canary, new IOException(canary)));
    }
}
