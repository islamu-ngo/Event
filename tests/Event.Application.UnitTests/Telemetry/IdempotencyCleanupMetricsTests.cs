using System.Diagnostics.Metrics;
using Explore.Application.Telemetry;
using Microsoft.Extensions.DependencyInjection;

namespace ApplicationUnitTests.Telemetry;

public sealed class IdempotencyCleanupMetricsTests
{
    [Test]
    public async Task CleanupMetricsPreserveCountsWithOnlyClosedDimensionValues()
    {
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var factory = services.GetRequiredService<IMeterFactory>();
        var meter = factory.Create(new MeterOptions(BusinessMetrics.MeterName));
        var measurements = new List<(long Value, KeyValuePair<string, object?>[] Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, subscription) =>
        {
            if (ReferenceEquals(instrument.Meter, meter)
                && instrument.Name.StartsWith("explore.idempotency.cleanup_", StringComparison.Ordinal))
                subscription.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            measurements.Add((value, tags.ToArray())));
        listener.Start();
        using var metrics = new BusinessMetrics(factory);
        string privateValue = Guid.CreateVersion7().ToString("N");

        metrics.RecordIdempotencyCleanupRun(privateValue, privateValue);
        metrics.RecordIdempotencyCleanupRows(2, privateValue, privateValue);
        metrics.RecordIdempotencyCleanupRun("scrub", "succeeded");
        metrics.RecordIdempotencyCleanupRows(3, "scrub", "scrubbed");
        metrics.RecordIdempotencyCleanupRows(5, "dry_run", "eligible");

        await Assert.That(measurements.Count).IsEqualTo(5);
        await Assert.That(measurements.Sum(item => item.Value)).IsEqualTo(12);
        foreach (var tag in measurements.Take(2).SelectMany(item => item.Tags))
            await Assert.That(tag.Value).IsEqualTo("unknown");
        foreach (var tag in measurements.Skip(2).SelectMany(item => item.Tags))
            await Assert.That(tag.Value).IsNotEqualTo("unknown");
        await Assert.That(measurements.SelectMany(item => item.Tags).Select(tag => tag.Key).Distinct().Order())
            .IsEquivalentTo(new[] { "mode", "outcome" });
    }
}
