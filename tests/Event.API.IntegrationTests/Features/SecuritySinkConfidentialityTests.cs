using System.Text.Json;
using Explore.API.HealthChecks;
using Explore.API.BackgroundServices;
using Explore.API.Scheduling;
using Explore.API.Telemetry;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Scheduling;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.DTOs.Management;
using Explore.Application.Features.Management.Requests.Commands;
using Explore.Application.Management;
using Explore.Application.Models.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Quartz;
using Serilog.Core;
using Serilog.Events;

namespace Event.Api.IntegrationTests.Features;

// Exercise production UseSerilog ownership of the process-wide static logger
// without racing another test host's initialization or disposal.
[NotInParallel]
public sealed class SecuritySinkConfidentialityTests
{
    [Test]
    [Arguments("Microsoft.AspNetCore.Hosting.Diagnostics")]
    [Arguments("Microsoft.IdentityModel.Protocols.OpenIdConnect")]
    [Arguments("System.Net.Http.HttpClient.Provider")]
    [Arguments("Grpc.Net.Client.Internal.GrpcCall")]
    [Arguments("Microsoft.EntityFrameworkCore.Database.Command")]
    [Arguments("Microsoft.Extensions.Hosting.Internal.Host")]
    [Arguments("Microsoft.Hosting.Lifetime")]
    public async Task FrameworkDiagnosticsAreExcludedWhileNativeEventsReachConfiguredSink(string category)
    {
        string canary = Guid.CreateVersion7().ToString("N");
        var collector = new Collector();
        using var host = CreateLogHost(collector);
        var factory = host.Services.GetRequiredService<ILoggerFactory>();

        factory.CreateLogger(category).LogWarning(
            new PrivateProviderCredentialFailure(canary), "Framework detail {Detail}", canary);
        factory.CreateLogger("Explore.API.NativeProbe").LogWarning(
            "Native diagnostic. FailureCategory={FailureCategory}", "native_probe");

        LogEvent exported = collector.Events.Single();
        await Assert.That(exported.Exception).IsNull();
        await Assert.That(((ScalarValue)exported.Properties["SourceContext"]).Value)
            .IsEqualTo("Explore.API.NativeProbe");
        await Assert.That(((ScalarValue)exported.Properties["FailureCategory"]).Value)
            .IsEqualTo("native_probe");
        await Assert.That(exported.RenderMessage() + string.Join(' ', exported.Properties.Values))
            .DoesNotContain(canary);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task SchedulerFailureAndListenerContainmentReachOnlyBoundedConfiguredSink(
        bool containment,
        bool captureScopes)
    {
        string canary = Guid.CreateVersion7().ToString("N");
        var collector = new Collector();
        using var host = CreateLogHost(collector, captureScopes);
        await Assert.That(host.Services.GetRequiredService<IOptions<LoggerFilterOptions>>().Value.CaptureScopes)
            .IsEqualTo(captureScopes);
        var scopeLogger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("InheritedScope");
        using var inheritedScope = scopeLogger.BeginScope(new Dictionary<string, object>
        {
            ["SourceContext"] = canary,
            ["FailureCategory"] = canary,
            ["ListenerMethod"] = canary
        });
        var logger = host.Services.GetRequiredService<ILogger<SchedulerTelemetryJobListener>>();
        var listener = new SchedulerTelemetryJobListener(Substitute.For<ISchedulerJobTelemetry>(), logger, TimeProvider.System);
        var context = Substitute.For<IJobExecutionContext>();
        if (containment)
            context.JobDetail.Returns(_ => throw new PrivateProviderCredentialFailure(canary));
        else
            context.JobDetail.Returns(JobBuilder.Create<ProbeJob>().WithIdentity(canary, canary).Build());
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["PrivateScope"] = canary,
            ["FailureCategory"] = canary,
            ["ListenerMethod"] = canary
        });
        using var formattedScope = logger.BeginScope("FailureCategory={FailureCategory}", canary);

        await listener.JobWasExecuted(context, new JobExecutionException(new PrivateProviderCredentialFailure(canary)));

        await Assert.That(collector.Events.Count).IsEqualTo(1);
        LogEvent exported = collector.Events.Single();
        await Assert.That(exported.Exception).IsNull();
        await Assert.That(((ScalarValue)exported.Properties["FailureCategory"]).Value)
            .IsEqualTo(containment ? "telemetry_listener_failed" : "scheduled_job_failed");
        await Assert.That(((ScalarValue)exported.Properties["SourceContext"]).Value)
            .IsEqualTo(typeof(SchedulerTelemetryJobListener).FullName);
        await Assert.That(exported.Level).IsEqualTo(containment ? LogEventLevel.Warning : LogEventLevel.Error);
        if (containment)
            await Assert.That(((ScalarValue)exported.Properties["ListenerMethod"]).Value)
                .IsEqualTo(nameof(SchedulerTelemetryJobListener.JobWasExecuted));
        string serialized = exported.RenderMessage() +
            string.Join(' ', exported.Properties.Select(property => property.Key + property.Value));
        await Assert.That(serialized).DoesNotContain(canary);
        await Assert.That(serialized).DoesNotContain(nameof(PrivateProviderCredentialFailure));
        await Assert.That(exported.Properties.ContainsKey("PrivateScope")).IsFalse();
        await Assert.That(exported.Properties.ContainsKey("JobName")).IsFalse();
        await Assert.That(exported.Properties.ContainsKey("JobGroup")).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativeStorageReadinessExportsFixedResolutionCategoryNotSubtypeOrContent(bool policyFailure)
    {
        string canary = Guid.CreateVersion7().ToString("N");
        var policies = Substitute.For<IStoragePolicyResolver>();
        var providers = Substitute.For<IFileStorageProviderResolver>();
        policies.ResolveAsync(null, Arg.Any<CancellationToken>()).Returns(_ => policyFailure
            ? Task.FromException<ResolvedStoragePolicy>(new PrivatePolicyCredentialFailure(canary))
            : Task.FromException<ResolvedStoragePolicy>(new PrivateProviderCredentialFailure(canary)));
        var result = await new StorageReadinessHealthCheck(policies, providers).CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(result.Exception).IsNull();
        await Assert.That(result.Data["reason"]).IsEqualTo(policyFailure ? "policy_unavailable" : "provider_unavailable");
        string serialized = JsonSerializer.Serialize(result.Data) + result.Description;
        await Assert.That(serialized).DoesNotContain(canary);
        await Assert.That(serialized).DoesNotContain(nameof(PrivateProviderCredentialFailure));
        await Assert.That(serialized).DoesNotContain(nameof(PrivatePolicyCredentialFailure));
    }

    [Test]
    [Arguments("schedule")]
    [Arguments("cancel")]
    [Arguments("unknown")]
    public async Task DeadlineFailurePreservesSweepFallbackWithoutExportingPrivateKeys(string operation)
    {
        string canary = Guid.CreateVersion7().ToString("N");
        var collector = new Collector();
        using var host = CreateLogHost(collector);
        var logger = host.Services.GetRequiredService<ILogger<QuartzScheduledDeadlineDispatcher>>();
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["PrivateScope"] = canary,
            ["FailureCategory"] = canary
        });
        var scheduler = Substitute.For<ISchedulerFactory>();
        scheduler.GetScheduler(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<IScheduler>(new PrivateProviderCredentialFailure(canary)));
        var dispatcher = new QuartzScheduledDeadlineDispatcher(
            scheduler, logger);
        if (operation == "cancel")
            await Assert.That(await dispatcher.CancelAsync(ScheduledJobNames.InventoryHoldExpiry, canary, CancellationToken.None))
                .IsFalse();
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
        await Assert.That(exported.Exception).IsNull();
        await Assert.That(((ScalarValue)exported.Properties["FailureCategory"]).Value)
            .IsEqualTo(operation == "unknown" ? "unknown_scheduled_job" : "scheduler_unavailable");
        await Assert.That(exported.RenderMessage() + string.Join(' ', exported.Properties.Values))
            .DoesNotContain(canary);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ManagedRegistrationFailureRetainsRetryDispositionWithoutPrivateProviderDiagnostics(bool thrown)
    {
        string canary = Guid.CreateVersion7().ToString("N");
        var collector = new Collector();
        using var host = CreateLogHost(collector);
        var logger = host.Services.GetRequiredService<ILogger<ManagedControlPlaneRegistrationWorker>>();
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["PrivateScope"] = canary,
            ["FailureCategory"] = canary
        });
        using var stop = new CancellationTokenSource();
        var handler = Substitute.For<ICommandHandler<TriggerManagedControlPlaneRegistrationCommand, TriggerManagedRegistrationResultDto>>();
        handler.ExecuteAsync(Arg.Any<TriggerManagedControlPlaneRegistrationCommand>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                stop.Cancel();
                return thrown
                    ? Task.FromException<TriggerManagedRegistrationResultDto>(new PrivateProviderCredentialFailure(canary))
                    : Task.FromResult(new TriggerManagedRegistrationResultDto(false, canary, canary, Guid.CreateVersion7()));
            });
        using var services = new ServiceCollection().AddSingleton(handler).BuildServiceProvider();
        using var worker = new ManagedControlPlaneRegistrationWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ManagedControlPlaneOptions { Enabled = true }),
            logger);

        // Cancellation is triggered by the exact handler completion, before the retry delay is entered.
        await worker.StartAsync(stop.Token);
        try
        {
            await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
        await worker.StopAsync(CancellationToken.None);

        LogEvent exported = collector.Events.Single();
        await Assert.That(exported.Exception).IsNull();
        await Assert.That(((ScalarValue)exported.Properties["FailureCategory"]).Value)
            .IsEqualTo(thrown ? "registration_retry_failed" : "registration_pending");
        string serialized = exported.RenderMessage() + string.Join(' ', exported.Properties.Select(p => p.Key + p.Value));
        await Assert.That(serialized).DoesNotContain(canary);
        await Assert.That(serialized).DoesNotContain(nameof(PrivateProviderCredentialFailure));
        await Assert.That(exported.Properties.ContainsKey("AttemptId")).IsFalse();
    }

    [Test]
    [Arguments("failure")]
    [Arguments("partial")]
    [Arguments("success")]
    [Arguments("cancel")]
    public async Task SvixStartupSyncExportsOnlyCategoryOrAggregateCounts(string outcome)
    {
        string canary = Guid.CreateVersion7().ToString("N");
        var collector = new Collector();
        using var host = CreateLogHost(collector);
        var logger = host.Services.GetRequiredService<ILogger<SvixWebhookEventTypeSyncWorker>>();
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["PrivateScope"] = canary,
            ["FailureCategory"] = canary,
            ["SyncedCount"] = canary,
            ["FailureCount"] = canary
        });
        using var stop = new CancellationTokenSource();
        var sync = Substitute.For<IWebhookProviderEventTypeSyncService>();
        sync.SyncAsync(Arg.Any<CancellationToken>()).Returns(call =>
        {
            if (outcome == "cancel")
            {
                stop.Cancel();
                return Task.FromException<WebhookProviderEventTypeSyncResult>(
                    new OperationCanceledException(call.Arg<CancellationToken>()));
            }
            if (outcome == "failure")
                return Task.FromException<WebhookProviderEventTypeSyncResult>(
                    new PrivateProviderCredentialFailure(canary));
            return Task.FromResult(outcome == "success"
                ? WebhookProviderEventTypeSyncResult.Success(2)
                : WebhookProviderEventTypeSyncResult.Completed(2,
                    [new WebhookProviderEventTypeSyncFailure(canary, canary, true, canary)]));
        });
        using var services = new ServiceCollection().AddSingleton(sync).BuildServiceProvider();
        using var worker = new SvixWebhookEventTypeSyncWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            logger);
        await worker.StartAsync(stop.Token);
        await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);
        var exported = collector.Events.Single();
        await Assert.That(exported.Exception).IsNull();
        string serialized = exported.RenderMessage() + string.Join(' ', exported.Properties.Select(p => p.Key + p.Value));
        await Assert.That(serialized).DoesNotContain(canary);
        await Assert.That(serialized).DoesNotContain(nameof(PrivateProviderCredentialFailure));
        await Assert.That(exported.Properties.ContainsKey("FailureType")).IsFalse();
        await Assert.That(exported.Level).IsEqualTo(outcome switch
        {
            "cancel" => LogEventLevel.Debug,
            "success" => LogEventLevel.Information,
            _ => LogEventLevel.Warning
        });
        if (outcome == "failure")
            await Assert.That(((ScalarValue)exported.Properties["FailureCategory"]).Value)
                .IsEqualTo("event_type_sync_failed");
        else if (outcome != "cancel")
        {
            await Assert.That(((ScalarValue)exported.Properties["SyncedCount"]).Value).IsEqualTo(2);
            if (outcome == "partial")
                await Assert.That(((ScalarValue)exported.Properties["FailureCount"]).Value).IsEqualTo(1);
        }
    }

    private static IHost CreateLogHost(Collector collector, bool captureScopes = true) =>
        new HostBuilder()
            .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Serilog:MinimumLevel:Default"] = "Debug" }))
            .ConfigureServices(services =>
            {
                services.AddSingleton<ILogEventSink>(collector);
                services.Configure<LoggerFilterOptions>(options => options.CaptureScopes = captureScopes);
            })
            .UseApiDiagnosticLogging()
            .Build();

    private sealed class Collector : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    private sealed class ProbeJob : IJob
    {
        public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
    }

    private sealed class PrivateProviderCredentialFailure(string canary) : InvalidOperationException(canary);
    private sealed class PrivatePolicyCredentialFailure(string canary) : ArgumentException(canary);
}
