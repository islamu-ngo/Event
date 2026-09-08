using System.Diagnostics.Metrics;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Telemetry;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Localization;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure.Localization;

public class RuntimeTranslationProviderFallbackTests
{
    private static readonly Guid TenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Test]
    public async Task ExportTranslations_WhenForceOfflineMode_ShortCircuitsToOfflineProvider()
    {
        using var metricsCapture = new MetricsCapture();
        var resolver = new MutableConfigResolver
        {
            Current = new TranslationConfiguration(
                TranslationManagementProviderEnum.Tolgee, "https://tolgee.test", "1", null, "en")
            {
                ForceOfflineMode = true
            }
        };

        var provider = BuildProvider(resolver);

        var result = await provider.ExportTranslationsAsync("en");

        // Offline bundles ship empty in v1 — the important invariant is "no exception and no Tolgee call".
        await Assert.That(result).IsNotNull();
        await Assert.That(resolver.ResolveCallCount).IsGreaterThanOrEqualTo(1);

        var measurement = await metricsCapture.SingleAsync(
            "islamu.translation.fetch_total",
            ("provider", nameof(OfflineTranslationProvider)),
            ("language", "en"),
            ("result", "hit_offline"));

        await Assert.That(measurement.Value).IsEqualTo(1);
    }

    [Test]
    public async Task ExportTranslations_WhenLiveProviderThrows_FallsBackToOfflineAndSwallowsException()
    {
        using var metricsCapture = new MetricsCapture();
        var resolver = new MutableConfigResolver
        {
            Current = new TranslationConfiguration(
                TranslationManagementProviderEnum.Tolgee, "https://tolgee.unreachable.invalid", "1", null, "en")
        };

        var provider = BuildProvider(resolver);

        // Tolgee with an unreachable host throws; runtime provider must not bubble the exception.
        var result = await provider.ExportTranslationsAsync("en");

        await Assert.That(result).IsNotNull();

        var errorMeasurement = await metricsCapture.SingleAsync(
            "islamu.translation.fetch_total",
            ("provider", nameof(TolgeeTranslationProvider)),
            ("language", "en"),
            ("result", "error"));
        var fallbackMeasurement = await metricsCapture.SingleAsync(
            "islamu.translation.fetch_total",
            ("provider", nameof(OfflineTranslationProvider)),
            ("language", "en"),
            ("result", "hit_offline"));

        await Assert.That(errorMeasurement.Value).IsEqualTo(1);
        await Assert.That(fallbackMeasurement.Value).IsEqualTo(1);
    }

    [Test]
    public async Task TestConnection_WhenOfflineProviderConfigured_RecordsConnectionMetric()
    {
        using var metricsCapture = new MetricsCapture();
        var resolver = new MutableConfigResolver
        {
            Current = new TranslationConfiguration(
                TranslationManagementProviderEnum.None, null, null, null, "en")
        };

        var provider = BuildProvider(resolver);

        var result = await provider.TestConnectionAsync();

        await Assert.That(result).IsTrue();

        var measurement = await metricsCapture.SingleAsync(
            "islamu.tms.connection_test_total",
            ("provider", nameof(OfflineTranslationProvider)),
            ("result", "success"));

        await Assert.That(measurement.Value).IsEqualTo(1);
    }

    [Test]
    public async Task ImportKeys_WhenLiveProviderThrows_RecordsFallbackMetricAndSwallowsException()
    {
        using var metricsCapture = new MetricsCapture();
        var resolver = new MutableConfigResolver
        {
            Current = new TranslationConfiguration(
                TranslationManagementProviderEnum.Tolgee, "https://tolgee.unreachable.invalid", "1", null, "en")
        };

        var provider = BuildProvider(resolver);

        await provider.ImportKeysAsync([
            new TranslationKeyImport(
                "lookup.tag.FIQH.full_name",
                new Dictionary<string, string> { ["en"] = "Fiqh" })
        ]);

        var measurement = await metricsCapture.SingleAsync("islamu.tms.fallback_activated_total");

        await Assert.That(measurement.Value).IsEqualTo(1);
        await Assert.That(measurement.Tags["provider"]?.ToString()).IsEqualTo(nameof(TolgeeTranslationProvider));
        await Assert.That(measurement.Tags["reason"]?.ToString()).IsEqualTo("network_error");
    }

    private static RuntimeTranslationProvider BuildProvider(ITranslationConfigResolver resolver)
    {
        var tolgeeClient = new HttpClient(new ThrowingHttpHandler()) { BaseAddress = new Uri("https://tolgee.unreachable.invalid") };
        var weblateClient = new HttpClient(new ThrowingHttpHandler()) { BaseAddress = new Uri("https://weblate.unreachable.invalid") };

        var tolgeeFactory = Substitute.For<IHttpClientFactory>();
        tolgeeFactory.CreateClient(Arg.Any<string>()).Returns(tolgeeClient);

        var weblateFactory = Substitute.For<IHttpClientFactory>();
        weblateFactory.CreateClient(Arg.Any<string>()).Returns(weblateClient);

        var secretResolver = CreateSecretResolver();
        var tenantContext = CreateTenantContext();
        var tolgee = new TolgeeTranslationProvider(tolgeeFactory, resolver, secretResolver, tenantContext, Substitute.For<ILogger<TolgeeTranslationProvider>>());
        var weblate = new WeblateTranslationProvider(weblateFactory, resolver, secretResolver, tenantContext, Substitute.For<ILogger<WeblateTranslationProvider>>());
        var offline = new OfflineTranslationProvider(Substitute.For<ILogger<OfflineTranslationProvider>>());
        var nullProvider = new NullTranslationProvider(Substitute.For<ILogger<NullTranslationProvider>>());

        return new RuntimeTranslationProvider(
            tolgee,
            weblate,
            offline,
            nullProvider,
            resolver,
            CreateTestMetrics(),
            Substitute.For<ILogger<RuntimeTranslationProvider>>());
    }

    private sealed class MutableConfigResolver : ITranslationConfigResolver
    {
        public TranslationConfiguration Current { get; set; } = new(
            TranslationManagementProviderEnum.None, null, null, null, "en");

        public int ResolveCallCount { get; private set; }

        public Task<TranslationConfiguration> ResolveAsync(CancellationToken ct = default)
        {
            ResolveCallCount++;
            return Task.FromResult(Current);
        }

        public void InvalidateCache(Guid? tenantId = null) { }
    }

    private static TranslationMetrics CreateTestMetrics()
    {
        var meter = new Meter(TranslationMetrics.MeterName);
        var factory = Substitute.For<IMeterFactory>();
        factory.Create(Arg.Any<MeterOptions>()).Returns(meter);
        return new TranslationMetrics(factory);
    }

    private static ISecretResolver CreateSecretResolver()
    {
        var resolver = Substitute.For<ISecretResolver>();
        resolver.ResolveAsync(SecretDefinitionRegistry.Keys.Localization.TmsApiKey, TenantId, Arg.Any<CancellationToken>()).Returns(SecretResolutionResult.Resolved(new ResolvedSecret(SecretDefinitionRegistry.Keys.Localization.TmsApiKey,
        "test-tms-key",
        SecretSourceType.EnvironmentVariable,
        SecretScope.Tenant,
        TenantId,
        DateTimeOffset.UtcNow)));
        return resolver;
    }

    private static ITenantContext CreateTenantContext()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(TenantId);
        return tenantContext;
    }

    private sealed class ThrowingHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("simulated network failure");
    }

    private sealed class MetricsCapture : IDisposable
    {
        private readonly MeterListener _listener;
        private readonly Lock _measurementsLock = new();
        private readonly List<Measurement> _measurements = [];

        public MetricsCapture()
        {
            _listener = new MeterListener();
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == TranslationMetrics.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };

            _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
            {
                lock (_measurementsLock)
                {
                    _measurements.Add(new Measurement(
                        instrument.Name,
                        measurement,
                        tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value)));
                }
            });

            _listener.Start();
        }

        public async Task<Measurement> SingleAsync(string instrumentName)
            => await SingleAsync(instrumentName, []);

        public async Task<Measurement> SingleAsync(string instrumentName, params (string Key, string Value)[] requiredTags)
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                Measurement[] snapshot;
                lock (_measurementsLock)
                {
                    snapshot = [.. _measurements];
                }

                var matches = snapshot
                    .Where(measurement => measurement.InstrumentName == instrumentName)
                    .Where(measurement => HasTags(measurement, requiredTags))
                    .ToList();

                if (matches.Count > 0)
                {
                    return matches[^1];
                }

                await Task.Delay(10);
            }

            lock (_measurementsLock)
            {
                return _measurements
                    .Where(measurement => measurement.InstrumentName == instrumentName)
                    .Where(measurement => HasTags(measurement, requiredTags))
                    .Last();
            }
        }

        private static bool HasTags(Measurement measurement, (string Key, string Value)[] requiredTags)
        {
            foreach (var (key, value) in requiredTags)
            {
                if (!measurement.Tags.TryGetValue(key, out var actual) || actual?.ToString() != value)
                    return false;
            }

            return true;
        }

        public void Dispose()
        {
            _listener.Dispose();
        }
    }

    private sealed record Measurement(
        string InstrumentName,
        long Value,
        IReadOnlyDictionary<string, object?> Tags);
}
