using Event.Standalone.IntegrationTests.Fixtures;
using Explore.API.BackgroundServices;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quartz;

namespace Event.Standalone.IntegrationTests;

// The isolated native deployment fixture binds and restores process environment authority.
[NotInParallel]
public sealed class HostedWorkerActivationTests
{
    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(30);

    [Test]
    public async Task ActualStandaloneAiWorkerReadsNativeQueueAndCancelsOnHostShutdown()
    {
        using var deployment = new NativeEmailOptionalStandaloneFixture();
        await using var factory = new NonTestingStandaloneWebApplicationFactory(deployment, schedulerEnabled: false);
        Task reading = factory.Queue.Reading;
        Task cancelled = factory.Queue.Cancelled;

        using var client = factory.CreateClient();
        await reading.WaitAsync(SignalTimeout);
        await Assert.That(factory.Services.GetRequiredService<IHostEnvironment>().EnvironmentName).IsEqualTo("Staging");
        AiAssistantRunWorker worker = factory.Services.GetServices<IHostedService>()
            .OfType<AiAssistantRunWorker>().Single();
        await Assert.That(worker.ExecuteTask).IsNotNull();
        await Assert.That(worker.ExecuteTask!.IsCompleted).IsFalse();

        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = factory.Services.GetRequiredService<IHostApplicationLifetime>()
            .ApplicationStopped.Register(() => stopped.TrySetResult());
        Task stopSignal = stopped.Task.WaitAsync(SignalTimeout);
        await factory.DisposeAsync().AsTask().WaitAsync(SignalTimeout);
        await cancelled.WaitAsync(SignalTimeout);
        await stopSignal;

        await Assert.That(worker.ExecuteTask.IsCompleted).IsTrue();
        await Assert.That(worker.ExecuteTask.IsFaulted).IsFalse();
        await Assert.That(factory.OutboundRequests.IsEmpty).IsTrue();
    }

    [Test]
    public async Task ActualNonTestingSchedulerStartsMapsOperatorSurfacesAndShutsDown()
    {
        using var deployment = new NativeEmailOptionalStandaloneFixture();
        await using var factory = new NonTestingStandaloneWebApplicationFactory(deployment, schedulerEnabled: true);
        Task started = factory.Scheduler.Started;
        Task shutdown = factory.Scheduler.Shutdown;

        using var client = factory.CreateClient();
        await started.WaitAsync(SignalTimeout);
        await Assert.That(factory.Services.GetRequiredService<IHostEnvironment>().EnvironmentName).IsEqualTo("Staging");
        IScheduler scheduler = await factory.Services.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await Assert.That(scheduler.IsStarted).IsTrue();
        await Assert.That(scheduler.IsShutdown).IsFalse();

        RouteEndpoint[] endpoints = factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToArray();
        RouteEndpoint status = endpoints.Single(endpoint =>
            endpoint.RoutePattern.RawText?.TrimStart('/') == "admin/scheduler");
        await Assert.That(status.Metadata.GetMetadata<IExcludeFromDescriptionMetadata>()?.ExcludeFromDescription)
            .IsEqualTo(true);
        await Assert.That(endpoints.Any(endpoint =>
            endpoint.RoutePattern.RawText?.TrimStart('/').StartsWith("quartz", StringComparison.OrdinalIgnoreCase) == true))
            .IsTrue();

        await factory.DisposeAsync().AsTask().WaitAsync(SignalTimeout);
        await shutdown.WaitAsync(SignalTimeout);
        await Assert.That(scheduler.IsShutdown).IsTrue();
        await Assert.That(factory.OutboundRequests.IsEmpty).IsTrue();
    }
}
