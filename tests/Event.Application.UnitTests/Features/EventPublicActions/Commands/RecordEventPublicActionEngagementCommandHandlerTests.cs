using System.Diagnostics.Metrics;
using Explore.Application.Telemetry;
using Explore.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Core;

namespace Event.Application.UnitTests.Features.EventPublicActions.Commands;

[NotInParallel("BusinessMetricsMeter")]
public sealed class RecordEventPublicActionEngagementCommandHandlerTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExecuteAsync_RecordsSynchronousMetricEvenWithCancelledCaller(bool cancelled)
    {
        using var metricsCapture = new MetricsCapture();
        using var provider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        using var metrics = new BusinessMetrics(provider.GetRequiredService<IMeterFactory>());
        var handler = new Explore.Application.Features.EventPublicActions.Handlers.Commands.RecordEventPublicActionEngagementCommandHandler(metrics);

        await handler.ExecuteAsync(
            new Explore.Application.Features.EventPublicActions.Requests.Commands.RecordEventPublicActionEngagementCommand(
                EventPublicActionKindEnum.ExternalRegistration,
                "event_detail"),
            new CancellationToken(cancelled));

        var measurement = metricsCapture.Single("explore.event_public_actions.engagements");

        await Assert.That(measurement.Value).IsEqualTo(1);
        await Assert.That(measurement.Tags.Keys).DoesNotContain("tenant_id");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("user_id");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("event_id");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("action_id");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("url");
        await Assert.That(measurement.Tags["action_kind"]?.ToString()).IsEqualTo("external_registration");
        await Assert.That(measurement.Tags["surface"]?.ToString()).IsEqualTo("event_detail");
        await Assert.That(measurement.Tags["outcome"]?.ToString()).IsEqualTo("redirect_issued");
    }

    [Test]
    public async Task ExecuteAsync_BoundsUnexpectedSurfaceToOther()
    {
        using var metricsCapture = new MetricsCapture();
        using var provider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        using var metrics = new BusinessMetrics(provider.GetRequiredService<IMeterFactory>());
        var handler = new Explore.Application.Features.EventPublicActions.Handlers.Commands.RecordEventPublicActionEngagementCommandHandler(metrics);

        await handler.ExecuteAsync(
            new Explore.Application.Features.EventPublicActions.Requests.Commands.RecordEventPublicActionEngagementCommand(
                EventPublicActionKindEnum.Livestream,
                "https://example.invalid/events/123?ref=secret"),
            CancellationToken.None);

        var measurement = metricsCapture.Single("explore.event_public_actions.engagements");

        await Assert.That(measurement.Tags["action_kind"]?.ToString()).IsEqualTo("livestream");
        await Assert.That(measurement.Tags["surface"]?.ToString()).IsEqualTo("other");
        await Assert.That(measurement.Tags["outcome"]?.ToString()).IsEqualTo("redirect_issued");
        await Assert.That(string.Join(" ", measurement.Tags.Values.Select(value => value?.ToString()))).DoesNotContain("secret");
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
                if (instrument.Meter.Name == BusinessMetrics.MeterName)
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

        public Measurement Single(string instrumentName)
        {
            lock (_measurementsLock)
            {
                return _measurements
                    .Where(measurement => measurement.InstrumentName == instrumentName)
                    .Single();
            }
        }

        public void Dispose()
        {
            _listener.Dispose();
        }
    }

    private sealed record Measurement(string InstrumentName, long Value, IReadOnlyDictionary<string, object?> Tags);
}
