using Explore.API.Scheduling;
using Explore.Application.Contracts.Scheduling;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Quartz;
using TUnit.Core;

namespace ApiIntegrationTests.Features;

public sealed class SchedulerTelemetryJobListenerTests
{
    [Test]
    public async Task ASuccessfulExecutionIsRecordedWithItsJobIdentityAndOutcome()
    {
        var telemetry = Substitute.For<ISchedulerJobTelemetry>();
        var listener = CreateListener(telemetry);
        IJobExecutionContext context = CreateContext(
            ScheduledJobNames.EmailDispatchDrain,
            QuartzSchedulerKeys.RecurringGroup);

        await listener.JobToBeExecuted(context);
        await listener.JobWasExecuted(context, jobException: null);

        telemetry.Received(1).RecordSchedulerJobExecution(
            ScheduledJobNames.EmailDispatchDrain,
            QuartzSchedulerKeys.RecurringGroup,
            SchedulerJobOutcomes.Succeeded,
            Arg.Any<double>());
    }

    [Test]
    public async Task AFailedExecutionIsRecordedAsFailedRatherThanGoingUnreported()
    {
        var telemetry = Substitute.For<ISchedulerJobTelemetry>();
        var listener = CreateListener(telemetry);
        IJobExecutionContext context = CreateContext(
            ScheduledJobNames.InventoryHoldExpiry,
            QuartzSchedulerKeys.OnDemandGroup);

        await listener.JobToBeExecuted(context);
        await listener.JobWasExecuted(
            context,
            new JobExecutionException(new InvalidOperationException("database unavailable")));

        telemetry.Received(1).RecordSchedulerJobExecution(
            ScheduledJobNames.InventoryHoldExpiry,
            QuartzSchedulerKeys.OnDemandGroup,
            SchedulerJobOutcomes.Failed,
            Arg.Any<double>());
    }

    /// <summary>
    /// A vetoed job never ran. Recording it as a success would make a trigger listener that silently
    /// suppresses a job look indistinguishable from that job running fine.
    /// </summary>
    [Test]
    public async Task AVetoedExecutionIsRecordedUnderItsOwnOutcome()
    {
        var telemetry = Substitute.For<ISchedulerJobTelemetry>();
        var listener = CreateListener(telemetry);
        IJobExecutionContext context = CreateContext(
            ScheduledJobNames.StorageReconciliation,
            QuartzSchedulerKeys.RecurringGroup);

        await listener.JobExecutionVetoed(context);

        telemetry.Received(1).RecordSchedulerJobExecution(
            ScheduledJobNames.StorageReconciliation,
            QuartzSchedulerKeys.RecurringGroup,
            SchedulerJobOutcomes.Vetoed,
            Arg.Any<double>());
    }

    /// <summary>
    /// Measured from the listener's own start hook, so the duration reflects the execution rather than a
    /// value the job had to remember to report.
    /// </summary>
    [Test]
    public async Task ExecutionDurationIsMeasuredAcrossTheExecution()
    {
        var telemetry = Substitute.For<ISchedulerJobTelemetry>();
        var listener = CreateListener(telemetry);
        IJobExecutionContext context = CreateContext(
            ScheduledJobNames.EmailDispatchDrain,
            QuartzSchedulerKeys.RecurringGroup);

        await listener.JobToBeExecuted(context);
        await Task.Delay(TimeSpan.FromMilliseconds(25));
        await listener.JobWasExecuted(context, jobException: null);

        telemetry.Received(1).RecordSchedulerJobExecution(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Is<double>(seconds => seconds > 0));
    }

    /// <summary>
    /// The guarantee this listener exists under: Quartz documents that an unhandled listener exception can
    /// disrupt the scheduling cycle, so a broken telemetry sink must cost a metric — never every job in the
    /// process. Each listener method is exercised against a sink that throws on every call.
    /// </summary>
    [Test]
    public async Task AThrowingTelemetrySinkNeverEscapesAnyListenerMethod()
    {
        var telemetry = Substitute.For<ISchedulerJobTelemetry>();
        telemetry
            .When(sink => sink.RecordSchedulerJobExecution(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<double>()))
            .Do(_ => throw new InvalidOperationException("metrics pipeline unavailable"));
        var listener = CreateListener(telemetry);
        IJobExecutionContext context = CreateContext(
            ScheduledJobNames.EmailDispatchDrain,
            QuartzSchedulerKeys.RecurringGroup);

        await listener.JobToBeExecuted(context);
        await listener.JobWasExecuted(context, jobException: null);
        await listener.JobWasExecuted(context, new JobExecutionException(new TimeoutException()));
        await listener.JobExecutionVetoed(context);

        // Reaching this line is the assertion: every call returned instead of throwing.
        telemetry.ReceivedWithAnyArgs(3).RecordSchedulerJobExecution(default, default, default, default);
    }

    /// <summary>
    /// Containment must also survive a fault in the context itself, which is what the listener touches
    /// before it ever reaches the telemetry sink.
    /// </summary>
    [Test]
    public async Task AThrowingJobContextNeverEscapesAnyListenerMethod()
    {
        var listener = CreateListener(Substitute.For<ISchedulerJobTelemetry>());
        var context = Substitute.For<IJobExecutionContext>();
        context.JobDetail.Returns(_ => throw new InvalidOperationException("scheduler state unavailable"));
        context.When(ctx => ctx.Put(Arg.Any<object>(), Arg.Any<object>()))
            .Do(_ => throw new InvalidOperationException("scheduler state unavailable"));

        await listener.JobToBeExecuted(context);
        await listener.JobWasExecuted(context, jobException: null);
        await listener.JobExecutionVetoed(context);

        await Assert.That(listener.Name).IsEqualTo("explore-scheduler-telemetry");
    }

    private static SchedulerTelemetryJobListener CreateListener(ISchedulerJobTelemetry telemetry)
        => new(telemetry, NullLogger<SchedulerTelemetryJobListener>.Instance);

    /// <summary>
    /// Backs the context's <c>Put</c>/<c>Get</c> with a real dictionary so the listener's duration hand-off
    /// between <c>JobToBeExecuted</c> and <c>JobWasExecuted</c> is genuinely exercised.
    /// </summary>
    private static IJobExecutionContext CreateContext(string jobName, string jobGroup)
    {
        var state = new Dictionary<object, object>();
        var context = Substitute.For<IJobExecutionContext>();
        context.CancellationToken.Returns(CancellationToken.None);
        context.JobDetail.Returns(JobBuilder.Create<NoOpTelemetryProbeJob>()
            .WithIdentity(jobName, jobGroup)
            .Build());
        context.When(ctx => ctx.Put(Arg.Any<object>(), Arg.Any<object>()))
            .Do(callInfo => state[callInfo.ArgAt<object>(0)] = callInfo.ArgAt<object>(1));
        context.Get(Arg.Any<object>())
            .Returns(callInfo => state.GetValueOrDefault(callInfo.ArgAt<object>(0)));
        return context;
    }

    private sealed class NoOpTelemetryProbeJob : IJob
    {
        public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
    }
}
