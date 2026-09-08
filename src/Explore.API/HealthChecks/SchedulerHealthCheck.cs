using Explore.API.Configuration;
using Explore.Application.Contracts.Scheduling;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Explore.API.HealthChecks;

/// <summary>
/// Readiness for the scheduler that runs every other background subsystem — email dispatch, retention sweeps,
/// storage reconciliation. Without it, an instance whose scheduler is paused or whose triggers have fallen into the
/// error state reports fully healthy while no background work happens at all, which is precisely the failure an
/// operator needs monitoring to catch.
/// </summary>
public sealed class SchedulerHealthCheck(
    ISchedulerOperations schedulerOperations,
    IOptionsMonitor<QuartzSchedulerSettings> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var settings = options.CurrentValue;

        if (!settings.Enabled)
        {
            return HealthCheckResult.Degraded(
                "Background scheduling is intentionally disabled.",
                data: new Dictionary<string, object> { ["enabled"] = false });
        }

        var snapshot = await schedulerOperations.GetSnapshotAsync(cancellationToken);
        var erroredJobs = snapshot.Jobs
            .Count(job => job.Triggers.Any(trigger =>
                string.Equals(trigger.State, SchedulerAdminStates.Error, StringComparison.Ordinal)));

        var data = new Dictionary<string, object>
        {
            ["enabled"] = true,
            ["available"] = snapshot.Available,
            ["started"] = snapshot.Started,
            ["inStandby"] = snapshot.InStandbyMode,
            ["shutdown"] = snapshot.Shutdown,
            ["clustered"] = snapshot.Clustered,
            ["supportsPersistence"] = snapshot.SupportsPersistence,
            ["jobCount"] = snapshot.Jobs.Count,
            ["erroredJobCount"] = erroredJobs,
            ["executingJobCount"] = snapshot.ExecutingJobCount
        };

        // Configured on but not resolvable means the host never actually composed a scheduler — a deployment
        // fault rather than an operator choice, so it fails readiness instead of degrading.
        if (!snapshot.Available)
        {
            return HealthCheckResult.Unhealthy(
                "Background scheduling is enabled but no scheduler is running in this host.",
                data: data);
        }

        if (snapshot.Shutdown)
        {
            return HealthCheckResult.Unhealthy("The scheduler has shut down and cannot fire triggers.", data: data);
        }

        // Error-state triggers stop firing until an operator clears them, so the work they carry is silently not
        // happening. That is unhealthy rather than degraded: it needs intervention, not just awareness.
        if (erroredJobs > 0)
        {
            return HealthCheckResult.Unhealthy(
                $"{erroredJobs} scheduled job(s) have triggers in the error state and will not fire until reset.",
                data: data);
        }

        // Standby is a deliberate operator action, but leaving it invisible to monitoring is how a paused
        // scheduler becomes a multi-hour "why did email stop?" investigation.
        if (snapshot.InStandbyMode || !snapshot.Started)
        {
            return HealthCheckResult.Degraded(
                "The scheduler is in standby, so no triggers are firing.",
                data: data);
        }

        return HealthCheckResult.Healthy("The scheduler is running and no triggers are in the error state.", data);
    }
}
