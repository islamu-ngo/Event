using Explore.API.BackgroundServices;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.DTOs.Management;
using Explore.Application.Features.Management.Requests.Commands;
using Explore.Application.Management;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace ApiIntegrationTests.Features;

public sealed class StartupWorkerDiagnosticConfidentialityTests
{
    [Test]
    [Arguments("success")]
    [Arguments("pending")]
    [Arguments("failure")]
    [Arguments("cancel")]
    public async Task RegistrationOutcomePreservesWorkerDispositionWithoutPrivateDiagnostics(string outcome)
    {
        string canary = Guid.CreateVersion7().ToString("N");
        var collector = new Collector();
        using var serilog = new LoggerConfiguration().WriteTo.Sink(collector).CreateLogger();
        using var logs = LoggerFactory.Create(builder => builder.AddSerilog(serilog, dispose: false));
        using var stop = new CancellationTokenSource();
        var handled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = Substitute.For<ICommandHandler<TriggerManagedControlPlaneRegistrationCommand, TriggerManagedRegistrationResultDto>>();
        handler.ExecuteAsync(Arg.Any<TriggerManagedControlPlaneRegistrationCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                handled.TrySetResult();
                if (outcome != "success")
                    stop.Cancel();
                return outcome switch
                {
                    "failure" => Task.FromException<TriggerManagedRegistrationResultDto>(new PrivateCredentialFailure(canary)),
                    "cancel" => Task.FromException<TriggerManagedRegistrationResultDto>(new OperationCanceledException(call.Arg<CancellationToken>())),
                    _ => Task.FromResult(new TriggerManagedRegistrationResultDto(
                        outcome == "success", canary, canary, Guid.CreateVersion7()))
                };
            });
        using var services = new ServiceCollection().AddSingleton(handler).BuildServiceProvider();
        using var worker = new ManagedControlPlaneRegistrationWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ManagedControlPlaneOptions { Enabled = true }),
            logs.CreateLogger<ManagedControlPlaneRegistrationWorker>());

        await worker.StartAsync(stop.Token);
        await handled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
        await worker.StopAsync(CancellationToken.None);

        if (outcome == "cancel")
        {
            await Assert.That(collector.Events).IsEmpty();
            return;
        }
        LogEvent exported = collector.Events.Single();
        await AssertPrivateValuesAbsentAsync(exported, canary);
        await Assert.That(exported.Level).IsEqualTo(
            outcome == "success" ? LogEventLevel.Information : LogEventLevel.Warning);
        if (outcome != "success")
            await Assert.That(((ScalarValue)exported.Properties["FailureCategory"]).Value)
                .IsEqualTo(outcome == "failure" ? "registration_retry_failed" : "registration_pending");
    }

    [Test]
    [Arguments("success")]
    [Arguments("partial")]
    [Arguments("failure")]
    [Arguments("cancel")]
    public async Task EventTypeSyncExportsOnlyAggregateCountsOrFixedFailureCategory(string outcome)
    {
        string canary = Guid.CreateVersion7().ToString("N");
        var collector = new Collector();
        using var serilog = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(collector).CreateLogger();
        using var logs = LoggerFactory.Create(builder => builder.AddSerilog(serilog, dispose: false));
        using var stop = new CancellationTokenSource();
        var handled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sync = Substitute.For<IWebhookProviderEventTypeSyncService>();
        sync.SyncAsync(Arg.Any<CancellationToken>()).Returns(call =>
        {
            handled.TrySetResult();
            if (outcome == "cancel")
            {
                stop.Cancel();
                return Task.FromException<WebhookProviderEventTypeSyncResult>(
                    new OperationCanceledException(call.Arg<CancellationToken>()));
            }
            return outcome switch
            {
                "failure" => Task.FromException<WebhookProviderEventTypeSyncResult>(new PrivateCredentialFailure(canary)),
                "partial" => Task.FromResult(WebhookProviderEventTypeSyncResult.Completed(2,
                    [new WebhookProviderEventTypeSyncFailure(canary, canary, true, canary)])),
                _ => Task.FromResult(WebhookProviderEventTypeSyncResult.Success(2))
            };
        });
        using var services = new ServiceCollection().AddSingleton(sync).BuildServiceProvider();
        using var worker = new SvixWebhookEventTypeSyncWorker(
            services.GetRequiredService<IServiceScopeFactory>(), logs.CreateLogger<SvixWebhookEventTypeSyncWorker>());

        await worker.StartAsync(stop.Token);
        await handled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        LogEvent exported = collector.Events.Single();
        await AssertPrivateValuesAbsentAsync(exported, canary);
        if (outcome == "failure")
            await Assert.That(((ScalarValue)exported.Properties["FailureCategory"]).Value)
                .IsEqualTo("event_type_sync_failed");
        else if (outcome != "cancel")
        {
            await Assert.That(((ScalarValue)exported.Properties["SyncedCount"]).Value).IsEqualTo(2);
            if (outcome == "partial")
                await Assert.That(((ScalarValue)exported.Properties["FailureCount"]).Value).IsEqualTo(1);
        }
        else
            await Assert.That(exported.Level).IsEqualTo(LogEventLevel.Debug);
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
}
