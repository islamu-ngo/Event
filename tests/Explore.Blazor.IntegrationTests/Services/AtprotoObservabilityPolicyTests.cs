using System.Diagnostics.Metrics;
using Explore.Blazor.Constants;
using Explore.Blazor.HealthChecks;
using Explore.Blazor.Services;
using Explore.Blazor.Services.Auth;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;

namespace Explore.Blazor.IntegrationTests.Services;

public sealed class AtprotoObservabilityPolicyTests
{
    [Test]
    public async Task HealthCheck_DisabledProviderIsHealthyAndDoesNotProbeReadiness()
    {
        var schemes = Substitute.For<IDynamicAuthSchemeManager>();
        schemes.GetRegisteredProviderSchemesAsync().Returns([AuthSchemeNames.Keycloak]);
        var readiness = Substitute.For<IBffProviderReadinessService>();
        var healthCheck = new AtprotoAuthenticationHealthCheck(schemes, readiness);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
        await Assert.That(result.Data).IsEquivalentTo(new Dictionary<string, object> { ["enabled"] = false });
        await readiness.DidNotReceiveWithAnyArgs().GetProviderReadinessAsync(default!, default);
    }

    [Test]
    public async Task HealthCheck_EnabledMisconfigurationIsUnavailableWithOnlyBoundedData()
    {
        var schemes = Substitute.For<IDynamicAuthSchemeManager>();
        schemes.GetRegisteredProviderSchemesAsync().Returns([AuthSchemeNames.Atproto]);
        var readiness = Substitute.For<IBffProviderReadinessService>();
        readiness.GetProviderReadinessAsync(AuthSchemeNames.Atproto, Arg.Any<CancellationToken>())
            .Returns(new BffProviderReadiness(false, "key_ring_unavailable"));
        var healthCheck = new AtprotoAuthenticationHealthCheck(schemes, readiness);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(result.Description).IsEqualTo("AT Protocol login is enabled but unavailable.");
        await Assert.That(result.Data).IsEquivalentTo(new Dictionary<string, object>
        {
            ["enabled"] = true,
            ["failureCode"] = "key_ring_unavailable"
        });
    }

    [Test]
    public async Task Metrics_NormalizeUntrustedFailureValueBeforePublishingLabels()
    {
        const string canary = "did:plc:user?token=secret&jwk=private provider-body";
        var measurements = new List<(string Instrument, IReadOnlyDictionary<string, object?> Tags)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == "Explore.Business"
                    && instrument.Name.StartsWith("atproto.authentication.", StringComparison.Ordinal))
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
            measurements.Add(("atproto.authentication.operations", tags.ToArray().ToDictionary(pair => pair.Key, pair => pair.Value))));
        listener.SetMeasurementEventCallback<double>((instrument, _, tags, _) =>
            measurements.Add((instrument.Name, tags.ToArray().ToDictionary(pair => pair.Key, pair => pair.Value))));
        listener.Start();

        new AtprotoAuthenticationMetrics().RecordReadiness(false, canary, TimeSpan.FromMilliseconds(5));

        await Assert.That(measurements).Contains(entry =>
            entry.Instrument == "atproto.authentication.operations"
            && Equals(entry.Tags["operation"], "readiness")
            && Equals(entry.Tags["outcome"], "internal_failure"));
        await Assert.That(measurements).Contains(entry => entry.Instrument == "atproto.authentication.duration");
        await Assert.That(string.Join('|', measurements.SelectMany(entry => entry.Tags.Values))).DoesNotContain(canary);
    }
}
