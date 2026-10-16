using Explore.API.Scheduling;
using Explore.Application.Contracts.Scheduling;
using Explore.Application.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Quartz;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace ApiIntegrationTests.Features;

public sealed class SchedulerDiagnosticConfidentialityTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task JobAndListenerFailuresExportCategoriesWithoutPrivateIdentity(bool containment)
    {
        string canary = Guid.CreateVersion7().ToString("N");
        var collector = new Collector();
        using var serilog = new LoggerConfiguration().WriteTo.Sink(collector).CreateLogger();
        using var logs = LoggerFactory.Create(builder => builder.AddSerilog(serilog, dispose: false));
        var listener = new SchedulerTelemetryJobListener(
            Substitute.For<ISchedulerJobTelemetry>(), logs.CreateLogger<SchedulerTelemetryJobListener>(), TimeProvider.System);
        var context = Substitute.For<IJobExecutionContext>();
        if (containment)
            context.JobDetail.Returns(_ => throw new PrivateCredentialFailure(canary));
        else
            context.JobDetail.Returns(JobBuilder.Create<ProbeJob>().WithIdentity(canary, canary).Build());

        await listener.JobWasExecuted(context, new JobExecutionException(new PrivateCredentialFailure(canary)));

        LogEvent exported = collector.Events.Single();
        await Assert.That(((ScalarValue)exported.Properties["FailureCategory"]).Value)
            .IsEqualTo(containment ? "telemetry_listener_failed" : "scheduled_job_failed");
        await AssertPrivateValuesAbsentAsync(exported, canary);
        await Assert.That(exported.Properties.ContainsKey("JobName")).IsFalse();
        await Assert.That(exported.Properties.ContainsKey("JobGroup")).IsFalse();
    }

    [Test]
    [Arguments("schedule")]
    [Arguments("cancel")]
    [Arguments("unknown")]
    public async Task DeadlineRefusalPreservesFallbackWithoutExportingKeysOrProviderFailures(string operation)
    {
        string canary = Guid.CreateVersion7().ToString("N");
        var collector = new Collector();
        using var serilog = new LoggerConfiguration().WriteTo.Sink(collector).CreateLogger();
        using var logs = LoggerFactory.Create(builder => builder.AddSerilog(serilog, dispose: false));
        var scheduler = Substitute.For<ISchedulerFactory>();
        scheduler.GetScheduler(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<IScheduler>(new PrivateCredentialFailure(canary)));
        var dispatcher = new QuartzScheduledDeadlineDispatcher(
            scheduler, logs.CreateLogger<QuartzScheduledDeadlineDispatcher>());
        if (operation == "cancel")
            await Assert.That(await dispatcher.CancelAsync(
                ScheduledJobNames.InventoryHoldExpiry, canary, CancellationToken.None)).IsFalse();
        else
        {
            var result = await dispatcher.ScheduleAsync(new ScheduledDeadline(
                operation == "unknown" ? canary : ScheduledJobNames.InventoryHoldExpiry,
                canary, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["private"] = canary }),
                CancellationToken.None);
            await Assert.That(result.Scheduled).IsFalse();
            await Assert.That(result.FailureCategory).IsEqualTo(ScheduledDeadlineResult.SchedulerUnavailable);
        }

        LogEvent exported = collector.Events.Single();
        await Assert.That(((ScalarValue)exported.Properties["FailureCategory"]).Value)
            .IsEqualTo(operation == "unknown" ? "unknown_scheduled_job" : "scheduler_unavailable");
        await AssertPrivateValuesAbsentAsync(exported, canary);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DefaultAuditDiagnosticsOmitOperatorJobAndCallerCorrelation(bool succeeded)
    {
        string canary = Guid.CreateVersion7().ToString("N");
        var collector = new Collector();
        using var serilog = new LoggerConfiguration().WriteTo.Sink(collector).CreateLogger();
        using var logs = LoggerFactory.Create(builder => builder.AddSerilog(serilog, dispose: false));
        using var services = new ServiceCollection().AddMetrics().AddSingleton<BusinessMetrics>().BuildServiceProvider();
        var sink = new LoggingSchedulerAdminAuditSink(
            logs.CreateLogger<LoggingSchedulerAdminAuditSink>(), services.GetRequiredService<BusinessMetrics>());

        await sink.RecordAsync(new SchedulerAdminAuditRecord(
            SchedulerAdminAuditActions.TriggerJob, "operator-" + canary, "group-" + canary,
            "job-" + canary, succeeded, succeeded ? null : "forbidden", canary, DateTime.UtcNow),
            CancellationToken.None);

        LogEvent exported = collector.Events.Single();
        await Assert.That(((ScalarValue)exported.Properties["SchedulerTarget"]).Value).IsEqualTo("job");
        await Assert.That(exported.Level).IsEqualTo(succeeded ? LogEventLevel.Information : LogEventLevel.Warning);
        await AssertPrivateValuesAbsentAsync(exported, canary);
    }

    private static async Task AssertPrivateValuesAbsentAsync(LogEvent exported, string canary)
    {
        await Assert.That(exported.Exception).IsNull();
        string serialized = exported.RenderMessage() + string.Join(' ', exported.Properties.Select(p => p.Key + p.Value));
        await Assert.That(serialized).DoesNotContain(canary);
        await Assert.That(serialized).DoesNotContain(nameof(PrivateCredentialFailure));
    }

    private sealed class Collector : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    private sealed class PrivateCredentialFailure(string message) : Exception(message);
    private sealed class ProbeJob : IJob
    {
        public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
    }
}
