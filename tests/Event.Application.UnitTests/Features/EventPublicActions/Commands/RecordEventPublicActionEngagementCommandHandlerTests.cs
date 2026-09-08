using System.Diagnostics.Metrics;
using Explore.Application.Telemetry;
using Explore.Domain.Enums;
using NSubstitute;
using TUnit.Core;

namespace Event.Application.UnitTests.Features.EventPublicActions.Commands;

[NotInParallel("BusinessMetricsMeter")]
public sealed class RecordEventPublicActionEngagementCommandHandlerTests
{
    [Test]
    public async Task Handle_RecordsBoundedRedirectIssuedMetric()
    {
        using var metricsCapture = new MetricsCapture();
        using var metrics = CreateMetrics();
        var handler = new Explore.Application.Features.EventPublicActions.Handlers.Commands.RecordEventPublicActionEngagementCommandHandler(metrics);

        await handler.Handle(
            new Explore.Application.Features.EventPublicActions.Requests.Commands.RecordEventPublicActionEngagementCommand(
                EventPublicActionKindEnum.ExternalRegistration,
                "event_detail"),
            CancellationToken.None);

        var measurement = await metricsCapture.SingleAsync("explore.event_public_actions.engagements");

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
    public async Task Handle_BoundsUnexpectedSurfaceToOther()
    {
        using var metricsCapture = new MetricsCapture();
        using var metrics = CreateMetrics();
        var handler = new Explore.Application.Features.EventPublicActions.Handlers.Commands.RecordEventPublicActionEngagementCommandHandler(metrics);

        await handler.Handle(
            new Explore.Application.Features.EventPublicActions.Requests.Commands.RecordEventPublicActionEngagementCommand(
                EventPublicActionKindEnum.Livestream,
                "https://example.invalid/events/123?ref=secret"),
            CancellationToken.None);

        var measurement = await metricsCapture.SingleAsync("explore.event_public_actions.engagements");

        await Assert.That(measurement.Tags["action_kind"]?.ToString()).IsEqualTo("livestream");
        await Assert.That(measurement.Tags["surface"]?.ToString()).IsEqualTo("other");
        await Assert.That(measurement.Tags["outcome"]?.ToString()).IsEqualTo("redirect_issued");
        await Assert.That(string.Join(" ", measurement.Tags.Values.Select(value => value?.ToString()))).DoesNotContain("secret");
    }

    private static BusinessMetrics CreateMetrics()
    {
        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(new Meter(BusinessMetrics.MeterName));
        return new BusinessMetrics(meterFactory);
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

        public async Task<Measurement> SingleAsync(string instrumentName)
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
                    .ToList();

                if (matches.Count > 0)
                {
                    return matches.Single();
                }

                await Task.Delay(10);
            }

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
