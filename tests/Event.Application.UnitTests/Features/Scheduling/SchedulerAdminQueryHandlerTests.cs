using Explore.Application.Contracts.Scheduling;
using Explore.Application.Features.Scheduling.Handlers.Queries;
using Explore.Application.Features.Scheduling.Requests.Queries;
using NSubstitute;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Application.UnitTests.Features.Scheduling;

public sealed class SchedulerAdminQueryHandlerTests
{
    [Test]
    public async Task Overview_WhenSchedulerRuns_ReportsRunningStateAndSummaryCounts()
    {
        var operations = Substitute.For<ISchedulerOperations>();
        operations.GetSnapshotAsync(Arg.Any<CancellationToken>()).Returns(Snapshot(
            started: true,
            inStandby: false,
            jobs:
            [
                Job("email-dispatch-drain", Trigger(SchedulerAdminStates.Active)),
                Job("idempotency-cleanup", Trigger(SchedulerAdminStates.Paused))
            ]));

        var result = await CreateOverviewHandler(operations, readOnly: false)
            .Handle(new GetSchedulerAdminOverviewQuery(), CancellationToken.None);

        await Assert.That(result.State).IsEqualTo(SchedulerAdminStates.Running);
        await Assert.That(result.Available).IsTrue();
        await Assert.That(result.ReadOnly).IsFalse();
        await Assert.That(result.JobCount).IsEqualTo(2);
        await Assert.That(result.PausedJobCount).IsEqualTo(1);
    }

    [Test]
    public async Task Overview_WhenSchedulerInStandby_ReportsStandbyState()
    {
        var operations = Substitute.For<ISchedulerOperations>();
        operations.GetSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(Snapshot(started: true, inStandby: true, jobs: []));

        var result = await CreateOverviewHandler(operations, readOnly: false)
            .Handle(new GetSchedulerAdminOverviewQuery(), CancellationToken.None);

        await Assert.That(result.State).IsEqualTo(SchedulerAdminStates.Standby);
    }

    /// <summary>
    /// A host with scheduling switched off must be distinguishable from a scheduler with no work, otherwise an
    /// operator reads a disabled instance as a healthy idle one.
    /// </summary>
    [Test]
    public async Task Overview_WhenSchedulerUnavailable_ReportsDisabledRatherThanEmpty()
    {
        var operations = Substitute.For<ISchedulerOperations>();
        operations.GetSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(SchedulerRuntimeSnapshot.Unavailable);

        var result = await CreateOverviewHandler(operations, readOnly: true)
            .Handle(new GetSchedulerAdminOverviewQuery(), CancellationToken.None);

        await Assert.That(result.Available).IsFalse();
        await Assert.That(result.State).IsEqualTo(SchedulerAdminStates.Disabled);
        await Assert.That(result.ReadOnly).IsTrue();
        await Assert.That(result.JobCount).IsEqualTo(0);
    }

    [Test]
    public async Task Jobs_CollapseTriggerStatesAndAggregateFireTimeline()
    {
        var earlier = new DateTimeOffset(2026, 8, 16, 10, 0, 0, TimeSpan.Zero);
        var later = new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
        var previous = new DateTimeOffset(2026, 8, 16, 8, 0, 0, TimeSpan.Zero);

        var operations = Substitute.For<ISchedulerOperations>();
        operations.GetSnapshotAsync(Arg.Any<CancellationToken>()).Returns(Snapshot(
            started: true,
            inStandby: false,
            jobs:
            [
                Job(
                    "multi-trigger-job",
                    Trigger(SchedulerAdminStates.Active, next: later, prev: null),
                    Trigger(SchedulerAdminStates.Active, next: earlier, prev: previous))
            ]));

        var jobs = await new GetSchedulerAdminJobsQueryHandler(operations, EmptyRegistry())
            .Handle(new GetSchedulerAdminJobsQuery(), CancellationToken.None);

        var job = jobs.Single();
        await Assert.That(job.State).IsEqualTo(SchedulerAdminStates.Active);
        await Assert.That(job.NextFireTimeUtc).IsEqualTo(earlier);
        await Assert.That(job.PreviousFireTimeUtc).IsEqualTo(previous);
    }

    [Test]
    public async Task Jobs_WhenEveryTriggerIsPaused_ReportPausedJob()
    {
        var operations = Substitute.For<ISchedulerOperations>();
        operations.GetSnapshotAsync(Arg.Any<CancellationToken>()).Returns(Snapshot(
            started: true,
            inStandby: false,
            jobs:
            [
                Job("paused-job", Trigger(SchedulerAdminStates.Paused), Trigger(SchedulerAdminStates.Paused))
            ]));

        var jobs = await new GetSchedulerAdminJobsQueryHandler(operations, EmptyRegistry())
            .Handle(new GetSchedulerAdminJobsQuery(), CancellationToken.None);

        await Assert.That(jobs.Single().State).IsEqualTo(SchedulerAdminStates.Paused);
    }

    /// <summary>
    /// A durable job with no trigger is fired on demand by runtime code, not stalled, so it must not be presented
    /// as a job whose schedule has gone missing.
    /// </summary>
    [Test]
    public async Task Jobs_WhenJobHasNoTrigger_ReportOnDemand()
    {
        var operations = Substitute.For<ISchedulerOperations>();
        operations.GetSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(Snapshot(started: true, inStandby: false, jobs: [Job("event-reminder-dispatch")]));

        var jobs = await new GetSchedulerAdminJobsQueryHandler(operations, EmptyRegistry())
            .Handle(new GetSchedulerAdminJobsQuery(), CancellationToken.None);

        await Assert.That(jobs.Single().State).IsEqualTo(SchedulerAdminStates.OnDemand);
    }

    [Test]
    public async Task Jobs_WhenTriggerIsInError_ErrorWinsOverHealthyTriggers()
    {
        var operations = Substitute.For<ISchedulerOperations>();
        operations.GetSnapshotAsync(Arg.Any<CancellationToken>()).Returns(Snapshot(
            started: true,
            inStandby: false,
            jobs:
            [
                Job("failing-job", Trigger(SchedulerAdminStates.Active), Trigger(SchedulerAdminStates.Error))
            ]));

        var jobs = await new GetSchedulerAdminJobsQueryHandler(operations, EmptyRegistry())
            .Handle(new GetSchedulerAdminJobsQuery(), CancellationToken.None);

        await Assert.That(jobs.Single().State).IsEqualTo(SchedulerAdminStates.Error);
    }

    private static GetSchedulerAdminOverviewQueryHandler CreateOverviewHandler(
        ISchedulerOperations operations,
        bool readOnly)
    {
        var policy = Substitute.For<ISchedulerAdminPolicy>();
        policy.IsEnabled.Returns(true);
        policy.IsReadOnly.Returns(readOnly);
        return new GetSchedulerAdminOverviewQueryHandler(operations, EmptyRegistry(), policy);
    }

    private static IScheduledJobRegistry EmptyRegistry()
    {
        var registry = Substitute.For<IScheduledJobRegistry>();
        registry.ListJobs().Returns([]);
        return registry;
    }

    private static SchedulerRuntimeSnapshot Snapshot(
        bool started,
        bool inStandby,
        IReadOnlyList<SchedulerJobSnapshot> jobs) =>
        new(
            Available: true,
            SchedulerName: "islamu-event-scheduler",
            InstanceId: "node-1",
            Started: started,
            InStandbyMode: inStandby,
            Shutdown: false,
            Clustered: false,
            SupportsPersistence: true,
            ExecutingJobCount: 0,
            Jobs: jobs);

    private static SchedulerJobSnapshot Job(string name, params SchedulerTriggerSnapshot[] triggers) =>
        new(name, "DEFAULT", Owner: "platform", Description: null, Durable: true, Executing: false, Triggers: triggers);

    private static SchedulerTriggerSnapshot Trigger(
        string state,
        DateTimeOffset? next = null,
        DateTimeOffset? prev = null) =>
        new($"{state}-trigger-{Guid.NewGuid():N}", "DEFAULT", state, "cron: 0 * * * * ?", next, prev);
}
