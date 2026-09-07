using Explore.API.Configuration;
using Explore.API.HealthChecks;
using Explore.Application.Contracts.Scheduling;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NSubstitute;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features;

public sealed class SchedulerHealthCheckTests
{
    [Test]
    public async Task RunningSchedulerWithNoErrorsIsHealthy()
    {
        var result = await CheckAsync(Snapshot());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
    }

    /// <summary>
    /// Scheduling switched off is an operator choice, not a fault, so it degrades rather than fails — matching how
    /// the other optional background subsystems report themselves.
    /// </summary>
    [Test]
    public async Task DisabledSchedulingIsDegradedRatherThanUnhealthy()
    {
        var result = await CheckAsync(Snapshot(), enabled: false);

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Degraded);
    }

    /// <summary>
    /// A paused scheduler runs no background work at all. Leaving that invisible to monitoring is how it becomes a
    /// multi-hour "why did email stop?" investigation.
    /// </summary>
    [Test]
    public async Task StandbySchedulerIsDegradedSoMonitoringSeesIt()
    {
        var result = await CheckAsync(Snapshot(inStandby: true));

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Degraded);
        await Assert.That(result.Description).Contains("standby");
    }

    /// <summary>
    /// Error-state triggers stop firing until an operator resets them, so the work they carry is silently not
    /// happening. That needs intervention, which is unhealthy rather than merely degraded.
    /// </summary>
    [Test]
    public async Task JobsInErrorStateFailReadiness()
    {
        var snapshot = Snapshot(jobs:
        [
            Job("healthy", SchedulerAdminStates.Active),
            Job("broken", SchedulerAdminStates.Error)
        ]);

        var result = await CheckAsync(snapshot);

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(result.Data["erroredJobCount"]).IsEqualTo(1);
    }

    [Test]
    public async Task ShutdownSchedulerFailsReadiness()
    {
        var result = await CheckAsync(Snapshot(shutdown: true));

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Unhealthy);
    }

    /// <summary>
    /// Configured on but not resolvable is a deployment fault, not an operator choice.
    /// </summary>
    [Test]
    public async Task EnabledButUnavailableSchedulerFailsReadiness()
    {
        var result = await CheckAsync(SchedulerRuntimeSnapshot.Unavailable);

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Unhealthy);
    }

    private static async Task<HealthCheckResult> CheckAsync(
        SchedulerRuntimeSnapshot snapshot,
        bool enabled = true)
    {
        var operations = Substitute.For<ISchedulerOperations>();
        operations.GetSnapshotAsync(Arg.Any<CancellationToken>()).Returns(snapshot);

        var options = Substitute.For<IOptionsMonitor<QuartzSchedulerSettings>>();
        options.CurrentValue.Returns(new QuartzSchedulerSettings { Enabled = enabled });

        return await new SchedulerHealthCheck(operations, options)
            .CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);
    }

    private static SchedulerRuntimeSnapshot Snapshot(
        bool inStandby = false,
        bool shutdown = false,
        IReadOnlyList<SchedulerJobSnapshot>? jobs = null) =>
        new(
            Available: true,
            SchedulerName: "islamu-event-scheduler",
            InstanceId: "node-1",
            Started: true,
            InStandbyMode: inStandby,
            Shutdown: shutdown,
            Clustered: false,
            SupportsPersistence: true,
            ExecutingJobCount: 0,
            Jobs: jobs ?? [Job("healthy", SchedulerAdminStates.Active)]);

    private static SchedulerJobSnapshot Job(string name, string triggerState) =>
        new(name, "DEFAULT", Owner: "platform", Description: null, Durable: true, Executing: false,
            Triggers: [new SchedulerTriggerSnapshot($"{name}-trigger", "DEFAULT", triggerState, null, null, null)]);
}
