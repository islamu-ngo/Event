using System.Diagnostics.Metrics;
using System.Text.RegularExpressions;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Telemetry;
using Explore.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace ApplicationUnitTests.Telemetry;

[NotInParallel("AdmissionCheckInMetricsMeter")]
public sealed class AdmissionCheckInMetricsTests
{
    [Test]
    public async Task ProductionMeterPipelineCollectsEveryAdmissionOperationalSignalWithClosedLabels()
    {
        var exporter = new CapturingMetricExporter();
        var builder = Host.CreateApplicationBuilder();
        builder.ConfigureOpenTelemetry();
        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics.AddReader(new BaseExportingMetricReader(exporter)));

        using ServiceProvider services = builder.Services.BuildServiceProvider();
        MeterProvider meterProvider = services.GetRequiredService<MeterProvider>();
        using var metrics = new AdmissionCheckInMetrics();

        metrics.RecordOperation(
            AdmissionCheckInAction.CheckIn,
            AdmissionCheckInAuthorityKind.Scanner,
            AdmissionTargetTypeEnum.EventSession,
            AdmissionCheckInTelemetryOutcome.Rejected,
            12.5);
        metrics.RecordBatch(
            AdmissionCheckInAuthorityKind.Staff,
            AdmissionTargetTypeEnum.EventDay,
            25);
        metrics.RecordRateLimiterRejection(
            AdmissionCheckInLimiterPolicy.ScannerCheckIn,
            AdmissionCheckInAuthorityKind.Scanner,
            AdmissionTargetTypeEnum.EventSession);
        metrics.RecordBacklog(
            AdmissionCheckInBacklogKind.Audit,
            AdmissionTargetTypeEnum.Event,
            7);
        metrics.RecordInfrastructureState(
            AdmissionCheckInInfrastructureKind.AdmissionPath,
            AdmissionCheckInInfrastructureStatus.Unhealthy);

        meterProvider.ForceFlush();

        IReadOnlyList<CollectedMetric> measurements = exporter.Measurements;
        await Assert.That(measurements.Select(metric => metric.Name)).IsEquivalentTo([
            AdmissionCheckInMetrics.OperationDurationInstrument,
            AdmissionCheckInMetrics.OperationsInstrument,
            AdmissionCheckInMetrics.BatchSizeInstrument,
            AdmissionCheckInMetrics.SaturationInstrument,
            AdmissionCheckInMetrics.LimiterRejectionsInstrument,
            AdmissionCheckInMetrics.BacklogInstrument,
            AdmissionCheckInMetrics.InfrastructureInstrument]);

        foreach (CollectedMetric measurement in measurements)
        {
            await Assert.That(measurement.MeterName).IsEqualTo(AdmissionCheckInMetrics.MeterName);
            string[] allowed = measurement.Name switch
            {
                AdmissionCheckInMetrics.OperationDurationInstrument or AdmissionCheckInMetrics.OperationsInstrument =>
                    ["action", "authority_kind", "target_type", "outcome"],
                AdmissionCheckInMetrics.BatchSizeInstrument => ["authority_kind", "target_type"],
                AdmissionCheckInMetrics.SaturationInstrument => ["kind", "outcome"],
                AdmissionCheckInMetrics.LimiterRejectionsInstrument => ["policy", "authority_kind", "target_type"],
                AdmissionCheckInMetrics.BacklogInstrument => ["kind", "target_type"],
                AdmissionCheckInMetrics.InfrastructureInstrument => ["dependency_kind", "status"],
                _ => []
            };
            await Assert.That(measurement.Tags.Keys).IsEquivalentTo(allowed);
            await Assert.That(measurement.Tags.Keys.Any(Forbidden)).IsFalse();
        }

        await Assert.That(measurements.Single(metric => metric.Name == AdmissionCheckInMetrics.OperationsInstrument)
            .Tags["outcome"]).IsEqualTo("rejected");
        await Assert.That(measurements.Single(metric => metric.Name == AdmissionCheckInMetrics.LimiterRejectionsInstrument)
            .Tags["policy"]).IsEqualTo("scanner_check_in");
        await Assert.That(measurements.Single(metric => metric.Name == AdmissionCheckInMetrics.BacklogInstrument)
            .Tags["kind"]).IsEqualTo("audit");
        await Assert.That(measurements.Single(metric => metric.Name == AdmissionCheckInMetrics.InfrastructureInstrument)
            .Tags["status"]).IsEqualTo("unhealthy");
    }

    [Test]
    public async Task AlertRulesParseAllRequiredOperationalThresholds()
    {
        string rulesPath = Path.Combine(RepositoryRoot(), "src", "Explore.AppHost", "Config", "admission-check-in-alerts.yaml");
        IReadOnlyDictionary<string, AlertRule> alerts = ParseAlertRules(await File.ReadAllTextAsync(rulesPath));

        await Assert.That(alerts["AdmissionCheckInLatencyP95High"].Expression)
            .IsEqualTo("histogram_quantile(0.95, sum by (le) (rate(explore_admission_check_in_duration_milliseconds_bucket[15m]))) > 250");
        await Assert.That(alerts["AdmissionCheckInLatencyP95High"].Duration).IsEqualTo("15m");
        await Assert.That(alerts["AdmissionCheckInLatencyP99High"].Expression)
            .IsEqualTo("histogram_quantile(0.99, sum by (le) (rate(explore_admission_check_in_duration_milliseconds_bucket[15m]))) > 500");
        await Assert.That(alerts["AdmissionCheckInLatencyP99High"].Duration).IsEqualTo("15m");
        await Assert.That(alerts["AdmissionCheckInRejectionsSustained"].Expression)
            .Contains("explore_admission_check_in_operations_total{outcome=\"rejected\"}")
            .And.Contains("> 0.05");
        await Assert.That(alerts["AdmissionCheckInRejectionsSustained"].Duration).IsEqualTo("15m");
        await Assert.That(alerts["AdmissionCheckInLimiterSaturation"].Expression)
            .IsEqualTo("sum(increase(explore_admission_check_in_limiter_rejections_total[15m])) > 0");
        await Assert.That(alerts["AdmissionCheckInBacklogHigh"].Expression)
            .IsEqualTo("max(explore_admission_check_in_backlog) > 50");
        await Assert.That(alerts["AdmissionCheckInInfrastructureOutage"].Expression)
            .IsEqualTo("explore_admission_check_in_infrastructure{dependency_kind=\"admission_path\",status=\"unhealthy\"} == 1");
        await Assert.That(alerts["AdmissionCheckInInfrastructureOutage"].Duration).IsNull();
    }

    [Test]
    public async Task LocalFullCompositionExportsApiMetricsIntoPrometheusOtlpReceiver()
    {
        string appHost = await File.ReadAllTextAsync(Path.Combine(
            RepositoryRoot(),
            "src",
            "Explore.AppHost",
            "AppHost.cs"));

        await Assert.That(appHost).Contains("\"OTEL_EXPORTER_OTLP_ENDPOINT\"");
        await Assert.That(appHost).Contains("/api/v1/otlp");
        await Assert.That(appHost).Contains(
            "\"OTEL_EXPORTER_OTLP_PROTOCOL\", \"http/protobuf\"");
        await Assert.That(appHost).Contains(".WaitFor(resources.Prometheus)");
    }

    private static bool Forbidden(string key) =>
        new[] { "tenant", "event_id", "target_id", "ticket", "actor", "capability", "credential",
                "digest", "device", "reason", "participant", "order" }
            .Any(term => key.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyDictionary<string, AlertRule> ParseAlertRules(string yaml) =>
        Regex.Matches(yaml, @"(?ms)^\s*- alert: (?<name>\S+)\s*\r?\n\s*expr: >-\s*\r?\n\s*(?<expression>[^\r\n]+)(?:\s*\r?\n\s*for: (?<duration>\S+))?")
            .ToDictionary(
                match => match.Groups["name"].Value,
                match => new AlertRule(
                    match.Groups["expression"].Value.Trim(),
                    match.Groups["duration"].Success ? match.Groups["duration"].Value : null),
                StringComparer.Ordinal);

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Explore.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private sealed class CapturingMetricExporter : BaseExporter<Metric>
    {
        private readonly Lock _gate = new();
        private readonly List<CollectedMetric> _measurements = [];

        public IReadOnlyList<CollectedMetric> Measurements
        {
            get
            {
                lock (_gate)
                {
                    return _measurements.ToArray();
                }
            }
        }

        public override ExportResult Export(in Batch<Metric> batch)
        {
            lock (_gate)
            {
                foreach (Metric metric in batch)
                {
                    if (metric.MeterName != AdmissionCheckInMetrics.MeterName)
                    {
                        continue;
                    }

                    foreach (ref readonly MetricPoint point in metric.GetMetricPoints())
                    {
                        var tags = new Dictionary<string, string>(StringComparer.Ordinal);
                        foreach (KeyValuePair<string, object?> tag in point.Tags)
                        {
                            tags[tag.Key] = tag.Value?.ToString() ?? string.Empty;
                        }

                        _measurements.Add(new CollectedMetric(metric.Name, metric.MeterName, tags));
                    }
                }
            }

            return ExportResult.Success;
        }
    }

    private sealed record CollectedMetric(
        string Name,
        string MeterName,
        IReadOnlyDictionary<string, string> Tags);

    private sealed record AlertRule(string Expression, string? Duration);
}
