using System.Diagnostics.Metrics;

namespace Explore.Application.Telemetry;

public sealed class ProjectionMetrics
{
    public const string MeterName = "Explore.Projections";

    private readonly Counter<long> _rebuildTotal;
    private readonly Counter<long> _rebuildFailuresTotal;
    private readonly Histogram<double> _rebuildDuration;
    private readonly Counter<long> _drainTotal;
    private readonly Counter<long> _drainFailuresTotal;
    private readonly Histogram<double> _drainDuration;
    private readonly Counter<long> _drainedScopesTotal;
    private readonly Counter<long> _inlineUpdatesTotal;
    private readonly Counter<long> _dirtyScopeSkipsTotal;
    private readonly Counter<long> _quotaExceededTotal;

    public ProjectionMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);

        _rebuildTotal = meter.CreateCounter<long>(
            "explore.projections.rebuild_total",
            unit: "{rebuild}",
            description: "Total projection rebuild operations");

        _rebuildFailuresTotal = meter.CreateCounter<long>(
            "explore.projections.rebuild_failures_total",
            unit: "{failure}",
            description: "Total projection rebuild failures (rows that failed during rebuild)");

        _rebuildDuration = meter.CreateHistogram<double>(
            "explore.projections.rebuild_duration_seconds",
            unit: "s",
            description: "Projection rebuild duration in seconds");

        _drainTotal = meter.CreateCounter<long>(
            "explore.projections.drain_total",
            unit: "{drain}",
            description: "Total dirty-scope drain operations");

        _drainFailuresTotal = meter.CreateCounter<long>(
            "explore.projections.drain_failures_total",
            unit: "{failure}",
            description: "Total dirty-scope drain failures");

        _drainDuration = meter.CreateHistogram<double>(
            "explore.projections.drain_duration_seconds",
            unit: "s",
            description: "Dirty-scope drain duration in seconds");

        _drainedScopesTotal = meter.CreateCounter<long>(
            "explore.projections.drained_scopes_total",
            unit: "{scope}",
            description: "Total individual dirty scopes drained across all drain operations");

        _inlineUpdatesTotal = meter.CreateCounter<long>(
            "explore.projections.inline_updates_total",
            unit: "{update}",
            description: "Total inline projection updater operations completed without dirty-scope deferral");

        _dirtyScopeSkipsTotal = meter.CreateCounter<long>(
            "explore.projections.dirty_scope_skips_total",
            unit: "{skip}",
            description: "Total projection updater operations deferred into the dirty-scope backlog");

        _quotaExceededTotal = meter.CreateCounter<long>(
            "explore.projections.quota_exceeded_total",
            unit: "{rejection}",
            description: "Total projection operation quota rejections by bounded quota key and scope");
    }

    public void RecordRebuild(string tenantId, string projectionType, long rowsProcessed, long rowsFailed, double durationSeconds, bool lockAcquired)
    {
        var tags = new[]
        {
            new KeyValuePair<string, object?>("tenant_id", tenantId),
            new KeyValuePair<string, object?>("projection_type", projectionType),
            new KeyValuePair<string, object?>("lock_acquired", lockAcquired.ToString().ToLowerInvariant())
        };

        _rebuildTotal.Add(1, tags);
        _rebuildDuration.Record(durationSeconds, tags);

        if (rowsFailed > 0)
            _rebuildFailuresTotal.Add(rowsFailed, tags);
    }

    public void RecordDrain(string tenantId, string projectionType, int drainedCount, double durationSeconds)
    {
        var tags = new[]
        {
            new KeyValuePair<string, object?>("tenant_id", tenantId),
            new KeyValuePair<string, object?>("projection_type", projectionType)
        };

        _drainTotal.Add(1, tags);
        _drainDuration.Record(durationSeconds, tags);

        if (drainedCount > 0)
            _drainedScopesTotal.Add(drainedCount, tags);
    }

    public void RecordDrainFailure(string tenantId, string projectionType)
    {
        _drainFailuresTotal.Add(1,
            new KeyValuePair<string, object?>("tenant_id", tenantId),
            new KeyValuePair<string, object?>("projection_type", projectionType));
    }

    public void RecordInlineUpdate(string tenantId, string projectionType, string operation, long count = 1)
    {
        if (count <= 0)
            return;

        _inlineUpdatesTotal.Add(count,
            new KeyValuePair<string, object?>("tenant_id", tenantId),
            new KeyValuePair<string, object?>("projection_type", projectionType),
            new KeyValuePair<string, object?>("operation", operation));
    }

    public void RecordDirtyScopeSkip(string tenantId, string projectionType, string operation, string reason)
    {
        _dirtyScopeSkipsTotal.Add(1,
            new KeyValuePair<string, object?>("tenant_id", tenantId),
            new KeyValuePair<string, object?>("projection_type", projectionType),
            new KeyValuePair<string, object?>("operation", operation),
            new KeyValuePair<string, object?>("reason", reason));
    }

    public void RecordQuotaExceeded(string tenantId, string projectionType, string quotaKey, string scope)
    {
        _quotaExceededTotal.Add(1,
            new KeyValuePair<string, object?>("tenant_id", tenantId),
            new KeyValuePair<string, object?>("projection_type", projectionType),
            new KeyValuePair<string, object?>("quota_key", quotaKey),
            new KeyValuePair<string, object?>("scope", scope));
    }
}
