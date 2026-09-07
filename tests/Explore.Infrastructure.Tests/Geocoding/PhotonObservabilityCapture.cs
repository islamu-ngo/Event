using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace Explore.Infrastructure.Tests.Geocoding;

internal sealed class PhotonObservabilityCapture : ILoggerProvider, IMeterFactory
{
    private readonly MeterListener _meterListener;
    private readonly ActivityListener _activityListener;
    private readonly List<Meter> _meters = [];

    public PhotonObservabilityCapture()
    {
        LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder => builder.AddProvider(this));
        _meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (IsPhotonName(instrument.Meter.Name) || IsPhotonName(instrument.Name))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            }
        };
        _meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            Measurements.Add(new CapturedMeasurement(instrument.Name, value, tags.ToArray())));
        _meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            Measurements.Add(new CapturedMeasurement(instrument.Name, value, tags.ToArray())));
        _meterListener.Start();

        _activityListener = new ActivityListener
        {
            ShouldListenTo = source => IsPhotonName(source.Name),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => Activities.Add(CapturedActivity.From(activity))
        };
        ActivitySource.AddActivityListener(_activityListener);
    }

    public ILoggerFactory LoggerFactory { get; }

    public List<CapturedLog> Logs { get; } = [];

    public List<CapturedMeasurement> Measurements { get; } = [];

    public List<CapturedActivity> Activities { get; } = [];

    public ILogger CreateLogger(string categoryName) => new CaptureLogger(categoryName, Logs);

    public Meter Create(MeterOptions options)
    {
        var meter = new Meter(options);
        _meters.Add(meter);
        return meter;
    }

    public string ObservableText() => string.Join('|',
        Logs.Select(log => log.Observable)
            .Concat(Measurements.Select(measurement => measurement.Observable))
            .Concat(Activities.Select(activity => activity.Observable)));

    public void Dispose()
    {
        _activityListener.Dispose();
        _meterListener.Dispose();
        LoggerFactory.Dispose();
        foreach (Meter meter in _meters)
        {
            meter.Dispose();
        }
    }

    private static bool IsPhotonName(string value) =>
        value.Contains("photon", StringComparison.OrdinalIgnoreCase)
        || value.Contains("geocod", StringComparison.OrdinalIgnoreCase);

    private sealed class CaptureLogger(string category, List<CapturedLog> sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            IReadOnlyList<KeyValuePair<string, object?>> properties =
                state is IEnumerable<KeyValuePair<string, object?>> values ? values.ToArray() : [];
            sink.Add(new CapturedLog(
                category,
                formatter(state, exception),
                properties,
                exception?.ToString()));
        }
    }
}

internal sealed record CapturedLog(
    string Category,
    string Message,
    IReadOnlyList<KeyValuePair<string, object?>> Properties,
    string? Exception)
{
    public string Observable => string.Join('|', Category, Message, Exception,
        string.Join('|', Properties.Select(item => $"{item.Key}={item.Value}")));
}

internal sealed record CapturedMeasurement(
    string InstrumentName,
    double Value,
    IReadOnlyList<KeyValuePair<string, object?>> Tags)
{
    public string Observable => string.Join('|', InstrumentName, Value,
        string.Join('|', Tags.Select(item => $"{item.Key}={item.Value}")));
}

internal sealed record CapturedActivity(
    string Source,
    string Operation,
    IReadOnlyList<KeyValuePair<string, string?>> Tags,
    IReadOnlyList<string> Events,
    string? StatusDescription)
{
    public string Observable => string.Join('|', Source, Operation, StatusDescription,
        string.Join('|', Tags.Select(item => $"{item.Key}={item.Value}")),
        string.Join('|', Events));

    public static CapturedActivity From(Activity activity) => new(
        activity.Source.Name,
        activity.OperationName,
        activity.TagObjects.Select(tag => new KeyValuePair<string, string?>(tag.Key, tag.Value?.ToString())).ToArray(),
        activity.Events.Select(item => item.Name).ToArray(),
        activity.StatusDescription);
}
