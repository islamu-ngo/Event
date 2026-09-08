using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Explore.Application.Contracts.Admissions;
using Explore.Domain.Enums;

namespace Explore.Application.Telemetry;

public sealed class AdmissionCheckInMetrics : IAdmissionCheckInTelemetry, IDisposable
{
    public const string MeterName = "Explore.AdmissionCheckIn";
    public const string OperationDurationInstrument = "explore.admission.check_in.duration";
    public const string OperationsInstrument = "explore.admission.check_in.operations";
    public const string BatchSizeInstrument = "explore.admission.check_in.batch_size";
    public const string SaturationInstrument = "explore.admission.check_in.saturation";
    public const string LimiterRejectionsInstrument = "explore.admission.check_in.limiter_rejections";
    public const string BacklogInstrument = "explore.admission.check_in.backlog";
    public const string InfrastructureInstrument = "explore.admission.check_in.infrastructure";

    private readonly Meter _meter = new(MeterName);
    private readonly Histogram<double> _operationDuration;
    private readonly Counter<long> _operations;
    private readonly Histogram<int> _batchSize;
    private readonly Counter<long> _saturation;
    private readonly Counter<long> _limiterRejections;
    private readonly ConcurrentDictionary<BacklogState, long> _backlog = [];
    private readonly ConcurrentDictionary<AdmissionCheckInInfrastructureKind, AdmissionCheckInInfrastructureStatus>
        _infrastructure = [];

    public AdmissionCheckInMetrics()
    {
        _operationDuration = _meter.CreateHistogram<double>(
            OperationDurationInstrument,
            unit: "ms",
            description: "Admission operation duration by bounded action, authority, target type, and outcome.");
        _operations = _meter.CreateCounter<long>(
            OperationsInstrument,
            unit: "{operation}",
            description: "Admission operations by bounded action, authority, target type, and outcome.");
        _batchSize = _meter.CreateHistogram<int>(
            BatchSizeInstrument,
            unit: "{scan}",
            description: "Admission batch size by bounded authority and target type.");
        _saturation = _meter.CreateCounter<long>(
            SaturationInstrument,
            unit: "{event}",
            description: "Admission batch or queue saturation observations.");
        _limiterRejections = _meter.CreateCounter<long>(
            LimiterRejectionsInstrument,
            unit: "{rejection}",
            description: "Admission rate-limiter rejections by bounded policy, authority, and target type.");
        _meter.CreateObservableGauge(
            BacklogInstrument,
            ObserveBacklog,
            unit: "{item}",
            description: "Current admission operational backlog by bounded work kind and target type.");
        _meter.CreateObservableGauge(
            InfrastructureInstrument,
            ObserveInfrastructure,
            unit: "{state}",
            description: "Current admission dependency health by bounded dependency kind and status.");
    }

    public void RecordOperation(
        AdmissionCheckInAction action,
        AdmissionCheckInAuthorityKind authorityKind,
        AdmissionTargetTypeEnum? targetType,
        AdmissionCheckInTelemetryOutcome outcome,
        double durationMilliseconds)
    {
        if (!double.IsFinite(durationMilliseconds) || durationMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(durationMilliseconds));
        }

        var tags = new TagList
        {
            { "action", Action(action) },
            { "authority_kind", Authority(authorityKind) },
            { "target_type", TargetType(targetType) },
            { "outcome", Outcome(outcome) }
        };
        _operationDuration.Record(durationMilliseconds, tags);
        _operations.Add(1, tags);
        RecordInfrastructureState(
            AdmissionCheckInInfrastructureKind.AdmissionPath,
            outcome == AdmissionCheckInTelemetryOutcome.Unavailable
                ? AdmissionCheckInInfrastructureStatus.Unhealthy
                : AdmissionCheckInInfrastructureStatus.Healthy);
    }

    public void RecordBatch(
        AdmissionCheckInAuthorityKind authorityKind,
        AdmissionTargetTypeEnum? targetType,
        int batchSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(batchSize);
        _batchSize.Record(
            batchSize,
            new KeyValuePair<string, object?>("authority_kind", Authority(authorityKind)),
            new KeyValuePair<string, object?>("target_type", TargetType(targetType)));
    }

    public void RecordSaturation(
        AdmissionCheckInSaturationKind kind,
        AdmissionCheckInTelemetryOutcome outcome) =>
        _saturation.Add(
            1,
            new KeyValuePair<string, object?>("kind", SaturationKind(kind)),
            new KeyValuePair<string, object?>("outcome", Outcome(outcome)));

    public void RecordRateLimiterRejection(
        AdmissionCheckInLimiterPolicy policy,
        AdmissionCheckInAuthorityKind authorityKind,
        AdmissionTargetTypeEnum? targetType)
    {
        _limiterRejections.Add(
            1,
            new KeyValuePair<string, object?>("policy", LimiterPolicy(policy)),
            new KeyValuePair<string, object?>("authority_kind", Authority(authorityKind)),
            new KeyValuePair<string, object?>("target_type", TargetType(targetType)));
        RecordSaturation(AdmissionCheckInSaturationKind.RateLimiter, AdmissionCheckInTelemetryOutcome.Rejected);
    }

    public void RecordBacklog(
        AdmissionCheckInBacklogKind kind,
        AdmissionTargetTypeEnum? targetType,
        long depth)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(depth);
        _backlog[new BacklogState(BacklogKind(kind), TargetType(targetType))] = depth;
    }

    public void RecordInfrastructureState(
        AdmissionCheckInInfrastructureKind kind,
        AdmissionCheckInInfrastructureStatus status) =>
        _infrastructure[kind] = status;

    public void Dispose() => _meter.Dispose();

    private IEnumerable<Measurement<long>> ObserveBacklog() => _backlog
        .Select(pair => new Measurement<long>(
            pair.Value,
            new KeyValuePair<string, object?>("kind", pair.Key.Kind),
            new KeyValuePair<string, object?>("target_type", pair.Key.TargetType)));

    private IEnumerable<Measurement<long>> ObserveInfrastructure() => _infrastructure
        .Select(pair => new Measurement<long>(
            1,
            new KeyValuePair<string, object?>("dependency_kind", InfrastructureKind(pair.Key)),
            new KeyValuePair<string, object?>("status", InfrastructureStatus(pair.Value))));

    private static string Action(AdmissionCheckInAction value) => value switch
    {
        AdmissionCheckInAction.CheckIn => "check_in",
        AdmissionCheckInAction.Undo => "undo",
        _ => "unknown"
    };

    private static string Authority(AdmissionCheckInAuthorityKind value) => value switch
    {
        AdmissionCheckInAuthorityKind.Staff => "staff",
        AdmissionCheckInAuthorityKind.Scanner => "scanner",
        _ => "unknown"
    };

    private static string TargetType(AdmissionTargetTypeEnum? value) => value switch
    {
        AdmissionTargetTypeEnum.Event => "event",
        AdmissionTargetTypeEnum.EventDay => "day",
        AdmissionTargetTypeEnum.EventSession => "session",
        _ => "unknown"
    };

    private static string Outcome(AdmissionCheckInTelemetryOutcome value) => value switch
    {
        AdmissionCheckInTelemetryOutcome.CheckedIn => "checked_in",
        AdmissionCheckInTelemetryOutcome.AlreadyCheckedIn => "already_checked_in",
        AdmissionCheckInTelemetryOutcome.Undone => "undone",
        AdmissionCheckInTelemetryOutcome.NotCheckedIn => "not_checked_in",
        AdmissionCheckInTelemetryOutcome.Rejected => "rejected",
        AdmissionCheckInTelemetryOutcome.Unavailable => "unavailable",
        _ => "unknown"
    };

    private static string SaturationKind(AdmissionCheckInSaturationKind value) => value switch
    {
        AdmissionCheckInSaturationKind.RateLimiter => "rate_limiter",
        AdmissionCheckInSaturationKind.BatchLimit => "batch_limit",
        AdmissionCheckInSaturationKind.Queue => "queue",
        _ => "unknown"
    };

    private static string LimiterPolicy(AdmissionCheckInLimiterPolicy value) => value switch
    {
        AdmissionCheckInLimiterPolicy.StaffCheckIn => "staff_check_in",
        AdmissionCheckInLimiterPolicy.ScannerCapability => "scanner_capability",
        AdmissionCheckInLimiterPolicy.ScannerCheckIn => "scanner_check_in",
        _ => "unknown"
    };

    private static string BacklogKind(AdmissionCheckInBacklogKind value) => value switch
    {
        AdmissionCheckInBacklogKind.Transaction => "transaction",
        AdmissionCheckInBacklogKind.Audit => "audit",
        _ => "unknown"
    };

    private static string InfrastructureKind(AdmissionCheckInInfrastructureKind value) => value switch
    {
        AdmissionCheckInInfrastructureKind.AdmissionPath => "admission_path",
        _ => "unknown"
    };

    private static string InfrastructureStatus(AdmissionCheckInInfrastructureStatus value) => value switch
    {
        AdmissionCheckInInfrastructureStatus.Healthy => "healthy",
        AdmissionCheckInInfrastructureStatus.Degraded => "degraded",
        AdmissionCheckInInfrastructureStatus.Unhealthy => "unhealthy",
        _ => "unknown"
    };

    private readonly record struct BacklogState(string Kind, string TargetType);
}
