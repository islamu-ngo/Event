using Aspire.Hosting.ApplicationModel;
using Explore.AppHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Event.Distributed.IntegrationTests;

public sealed partial class AppHostRuntimeTests
{
    [Test]
    public async Task BuiltApplicationDisposalSettlesExecutionBeforeRemovingRoot()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var owner = await OwnedAppHostFixture.CreateAsync("Split", deadline.Token);
        await owner.DisposeAsync();
        await Assert.That(owner.ExecutionSettled).IsTrue();
        await Assert.That(Directory.Exists(owner.Root)).IsFalse();
    }

    [Test]
    public async Task MissingExecutionObservationRetainsBuiltOwnerRoot()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        bool verified = false;
        try
        {
            await using var owner = await OwnedAppHostFixture.CreateAsync("Split", false, "SingleTenant", null, builder =>
            {
                builder.Services.RemoveAll<TestingApplicationExecution>();
                return Task.CompletedTask;
            }, deadline.Token);
            var failure = await Assert.That(async () => await owner.DisposeAsync()).Throws<AggregateException>();
            await Assert.That(failure!.Flatten().InnerExceptions.Any(exception => exception is TimeoutException)).IsTrue();
            await Assert.That(owner.ExecutionSettled).IsFalse();
            await Assert.That(Directory.Exists(owner.Root)).IsTrue();
            verified = true;
        }
        catch (AggregateException) when (verified) { }
    }

    [Test]
    public async Task DuplicateExecutionObservationRetainsRootWhileStartupIsHeld()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var service = new DelayedStartupService(failDisposal: false);
        bool verified = false;
        try
        {
            await using var owner = await OwnedAppHostFixture.CreateAsync("Split", false, "SingleTenant", null, builder =>
            {
                var observer = (TestingApplicationExecution)builder.Services
                    .Single(descriptor => descriptor.ServiceType == typeof(TestingApplicationExecution)).ImplementationInstance!;
                observer.Observe(Task.CompletedTask);
                builder.Services.Insert(0, ServiceDescriptor.Singleton<IHostedService>(_ => service));
                return Task.CompletedTask;
            }, deadline.Token);
            try
            {
                Task startup = owner.StartAsync(deadline.Token);
                await await Task.WhenAny(service.Entered.Task, startup).WaitAsync(deadline.Token);
                var resource = owner.Application.Services.GetRequiredService<DistributedApplicationModel>()
                    .Resources.Single(resource => resource.Name == "postgres");
                await owner.Application.ResourceNotifications.PublishUpdateAsync(resource,
                    snapshot => snapshot with { State = new ResourceStateSnapshot(KnownResourceStates.FailedToStart, null) });
                var failure = await Assert.That(async () => await startup).Throws<AggregateException>();
                await Assert.That(failure!.Flatten().InnerExceptions.Any(exception =>
                    exception.Message == "test_application_execution_already_observed")).IsTrue();
                await Assert.That(async () => await owner.DisposeAsync()).Throws<AggregateException>();
                await Assert.That(owner.ExecutionSettled).IsFalse();
                await Assert.That(service.StartingExited.Task.IsCompleted).IsFalse();
                await Assert.That(Directory.Exists(owner.Root)).IsTrue();
                verified = true;
            }
            finally
            {
                service.Release.TrySetResult();
                if (service.Entered.Task.IsCompleted)
                    await service.StartingExited.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
        catch (AggregateException) when (verified) { }
    }

    [Test]
    public async Task FailedResourceJoinsDelayedStartupAndRetainsDisposalFailure()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var service = new DelayedStartupService();
        OwnedAppHostFixture? owner = null;
        bool verified = false;
        try
        {
            await using var owned = await OwnedAppHostFixture.CreateAsync("Split", false, "SingleTenant", null, builder =>
            {
                builder.Services.Insert(0, ServiceDescriptor.Singleton<IHostedService>(_ => service));
                return Task.CompletedTask;
            }, deadline.Token);
            owner = owned;
            try
            {
                Task startup = owner.StartAsync(deadline.Token);
                await await Task.WhenAny(service.Entered.Task, startup).WaitAsync(deadline.Token);
                var resource = owner.Application.Services.GetRequiredService<DistributedApplicationModel>()
                    .Resources.Single(resource => resource.Name == "postgres");
                await owner.Application.ResourceNotifications.PublishUpdateAsync(resource,
                    snapshot => snapshot with { State = new ResourceStateSnapshot(KnownResourceStates.FailedToStart, null) });
                await await Task.WhenAny(service.Cancelled.Task, startup).WaitAsync(deadline.Token);
                await Assert.That(service.Cancelled.Task.IsCompleted).IsTrue();
                await Assert.That(startup.IsCompleted).IsFalse();
                service.Release.TrySetResult();
                var startupFailure = await Assert.That(async () => await startup).Throws<InvalidOperationException>();
                await Assert.That(startupFailure!.Message).IsEqualTo("test_resource_start_failed:postgres");
                var failure = await Assert.That(async () => await owner.DisposeAsync()).Throws<AggregateException>();
                var causes = failure!.Flatten().InnerExceptions.Select(exception => exception.Message).ToArray();
                await Assert.That(causes.Contains("test_resource_start_failed:postgres", StringComparer.Ordinal)).IsTrue();
                await Assert.That(causes.Contains("assurance_disposal_fault", StringComparer.Ordinal)).IsTrue();
                await Assert.That(service.Disposed).IsTrue();
                await Assert.That(owner.ExecutionSettled).IsTrue();
                await Assert.That(Directory.Exists(owner.Root)).IsTrue();
                verified = true;
            }
            finally
            {
                service.Release.TrySetResult();
                if (service.Entered.Task.IsCompleted)
                    await service.StartingExited.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
        catch (AggregateException) when (verified) { }
        finally
        {
            if (owner is not null && owner.ExecutionSettled && Directory.Exists(owner.Root))
            {
                new TestingResourceIsolation(owner.RunId, owner.Root).VerifyOwnership();
                Directory.Delete(owner.Root, recursive: true);
            }
        }
    }

    [Test]
    public async Task HealthyApplicationRetainsHostedStopFailure()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        using var service = new FaultingStopService();
        OwnedAppHostFixture? owner = null;
        bool verified = false;
        try
        {
            await using var owned = await OwnedAppHostFixture.CreateAsync("Split", false, "SingleTenant", null, builder =>
            {
                builder.Services.AddSingleton<IHostedService>(_ => service);
                return Task.CompletedTask;
            }, deadline.Token);
            owner = owned;
            await owner.StartAsync(deadline.Token);
            await Assert.That(service.Started).IsTrue();
            var failure = await Assert.That(async () => await owner.DisposeAsync()).Throws<AggregateException>();
            await Assert.That(failure!.Flatten().InnerExceptions.Any(exception =>
                exception.Message == "assurance_stop_fault")).IsTrue();
            await Assert.That(service.Disposed).IsTrue();
            await Assert.That(Directory.Exists(owner.Root)).IsTrue();
            verified = true;
        }
        catch (AggregateException) when (verified) { }
        finally
        {
            if (owner is not null && owner.ExecutionSettled && Directory.Exists(owner.Root))
            {
                new TestingResourceIsolation(owner.RunId, owner.Root).VerifyOwnership();
                Directory.Delete(owner.Root, recursive: true);
            }
        }
    }

    private sealed class DelayedStartupService(bool failDisposal = true) : IHostedLifecycleService, IDisposable
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource StartingExited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Disposed { get; private set; }

        public async Task StartingAsync(CancellationToken cancellationToken)
        {
            using var registration = cancellationToken.Register(() => Cancelled.TrySetResult());
            Entered.TrySetResult();
            try
            {
                await Release.Task;
                cancellationToken.ThrowIfCancellationRequested();
            }
            finally { StartingExited.TrySetResult(); }
        }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void Dispose()
        {
            if (Disposed)
                return;
            Disposed = true;
            if (failDisposal)
                throw new InvalidOperationException("assurance_disposal_fault");
        }
    }

    private sealed class FaultingStopService : IHostedService, IDisposable
    {
        public bool Started { get; private set; }
        public bool Disposed { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Started = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException("assurance_stop_fault"));

        public void Dispose() => Disposed = true;
    }
}
