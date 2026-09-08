namespace Explore.Application.Telemetry;

using System.Diagnostics;
using System.Diagnostics.Metrics;

public sealed class SetupLiveTelemetry : IDisposable
{
    public const string InstrumentationName = "ISLAMU.Event.Setup.Live";
    private static readonly ActivitySource Activities = new(InstrumentationName);
    private readonly Meter _meter;
    private readonly Counter<long> _operations;
    private readonly Histogram<double> _duration;

    public SetupLiveTelemetry(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(InstrumentationName);
        _operations = _meter.CreateCounter<long>(
            "islamu.setup.live.operation.count",
            unit: "{operation}");
        _duration = _meter.CreateHistogram<double>(
            "islamu.setup.live.operation.duration",
            unit: "ms");
    }

    public Operation Start(string operation, long? requestBytes = null) =>
        new(this, operation, requestBytes);

    public void Dispose() => _meter.Dispose();

    public sealed class Operation : IDisposable
    {
        private readonly SetupLiveTelemetry _owner;
        private readonly string _operation;
        private readonly long? _requestBytes;
        private readonly long _started = Stopwatch.GetTimestamp();
        private readonly Activity? _activity;
        private bool _completed;

        internal Operation(
            SetupLiveTelemetry owner,
            string operation,
            long? requestBytes)
        {
            _owner = owner;
            _operation = operation;
            _requestBytes = requestBytes;
            _activity = Activities.StartActivity("setup.live.operation");
            _activity?.SetTag("operation", operation);
            if (requestBytes.HasValue)
                _activity?.SetTag("request.bytes", requestBytes.Value);
        }

        public void Complete(string outcome)
        {
            if (_completed)
                return;
            _completed = true;
            _activity?.SetTag("outcome", outcome);
            TagList tags = new()
            {
                { "operation", _operation },
                { "outcome", outcome }
            };
            _owner._operations.Add(1, tags);
            _owner._duration.Record(
                Stopwatch.GetElapsedTime(_started).TotalMilliseconds,
                tags);
            _activity?.Dispose();
        }

        public void Dispose()
        {
            if (!_completed)
                Complete("unavailable");
        }
    }
}
