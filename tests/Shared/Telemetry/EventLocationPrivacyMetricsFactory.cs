using System.Diagnostics.Metrics;
using Explore.Application.Telemetry;

namespace Explore.Tests.Shared.Telemetry;

public static class EventLocationPrivacyMetricsFactory
{
    /// <summary>
    /// Creates metrics over a meter factory owned by the caller's test, so parallel suites never
    /// observe each other's measurements through a shared process-wide meter.
    /// </summary>
    public static EventLocationPrivacyMetrics Create() => new(new IsolatedMeterFactory());

    private sealed class IsolatedMeterFactory : IMeterFactory
    {
        private readonly List<Meter> _meters = [];

        public Meter Create(MeterOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            var meter = new Meter(options);
            _meters.Add(meter);
            return meter;
        }

        public void Dispose()
        {
            foreach (Meter meter in _meters)
            {
                meter.Dispose();
            }

            _meters.Clear();
        }
    }
}
