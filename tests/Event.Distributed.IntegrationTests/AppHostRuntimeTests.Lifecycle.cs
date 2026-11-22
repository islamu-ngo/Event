using Aspire.Hosting.ApplicationModel;
using Explore.AppHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Event.Distributed.IntegrationTests;

public sealed partial class AppHostRuntimeTests
{
    [Test]
    public async Task BuiltApplicationDoesNotCreateResourcesBeforeStart()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var owner = await OwnedAppHostFixture.CreateAsync("Split", deadline.Token);
        var containers = await OwnedAppHostFixture.RunDockerAsync(
            ["ps", "--no-trunc", "-aq", "--filter", $"label=islamu.assurance.owner={owner.RunId}"], deadline.Token);
        await Assert.That(containers.ExitCode).IsEqualTo(0);
        await Assert.That(containers.Output.Trim()).IsEqualTo(string.Empty);
        foreach (string volume in owner.OwnedVolumeNames)
        {
            var inspected = await OwnedAppHostFixture.RunDockerAsync(["volume", "inspect", volume], deadline.Token);
            await Assert.That(inspected.ExitCode).IsNotEqualTo(0);
            await Assert.That(inspected.Error.Contains("no such volume", StringComparison.OrdinalIgnoreCase)).IsTrue();
        }
    }

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
    public async Task MissingExecutionObservationFailsBeforeBuildingHost()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        Exception? failure = null;
        try
        {
            await using var owner = await OwnedAppHostFixture.CreateAsync("Split", false, "SingleTenant", null, builder =>
            {
                builder.Services.RemoveAll<TestingApplicationExecution>();
                return Task.CompletedTask;
            }, deadline.Token);
        }
        catch (Exception exception) { failure = exception; }
        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.ToString().Contains("test_application_execution_missing", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task DuplicateExecutionObservationFailsBeforeStartup()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var service = new DelayedStartupService(failDisposal: false);
        OwnedAppHostFixture? owner = null;
        Exception? failure = null;
        try
        {
            owner = await OwnedAppHostFixture.CreateAsync("Split", false, "SingleTenant", null, builder =>
            {
                var observer = (TestingApplicationExecution)builder.Services
                    .Single(descriptor => descriptor.ServiceType == typeof(TestingApplicationExecution)).ImplementationInstance!;
                observer.Observe(Task.CompletedTask);
                builder.Services.Insert(0, ServiceDescriptor.Singleton<IHostedService>(_ => service));
                return Task.CompletedTask;
            }, deadline.Token);
            await owner.StartAsync(deadline.Token);
        }
        catch (Exception exception) { failure = exception; }
        finally
        {
            service.Release.TrySetResult();
            if (owner is not null)
            {
                try { await owner.DisposeAsync(); }
                catch (Exception exception) { failure = failure is null ? exception : new AggregateException(failure, exception); }
            }
        }
        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.ToString().Contains("test_application_execution_already_observed", StringComparison.Ordinal)).IsTrue();
        await Assert.That(service.Entered.Task.IsCompleted).IsFalse();
    }

    [Test]
    public async Task FailedResourceJoinsDelayedStartupAndRetainsDisposalFailure()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        using var service = new DelayedStartupService();
        OwnedAppHostFixture? owner = null;
        Exception? testFailure = null;
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
                await Assert.That(service.Entered.Task.IsCompleted).IsTrue();
                var resource = owner.Application.Services.GetRequiredService<DistributedApplicationModel>()
                    .Resources.Single(resource => resource.Name == "postgres");
                await owner.Application.ResourceNotifications.PublishUpdateAsync(resource,
                    snapshot => snapshot with { State = new ResourceStateSnapshot(KnownResourceStates.FailedToStart, null) });
                await await Task.WhenAny(service.Cancelled.Task, startup).WaitAsync(deadline.Token);
                await Assert.That(service.Cancelled.Task.IsCompleted).IsTrue();
                await Assert.That(startup.IsCompleted).IsFalse();
                service.Release.TrySetResult();
                var startupFailure = await Assert.That(async () => await startup).Throws<AggregateException>();
                var startupCauses = startupFailure!.Flatten().InnerExceptions.Select(exception => exception.Message).ToArray();
                await Assert.That(startupCauses.Contains("test_resource_start_failed:postgres", StringComparer.Ordinal)).IsTrue();
                await Assert.That(startupCauses.Any(message =>
                    message.StartsWith("test_resource_cleanup_proof_incomplete:", StringComparison.Ordinal))).IsTrue();
                await Assert.That(service.Disposed).IsFalse();
                var failure = await Assert.That(async () => await owner.DisposeAsync()).Throws<AggregateException>();
                var causes = failure!.Flatten().InnerExceptions.Select(exception => exception.Message).ToArray();
                await Assert.That(causes.Contains("test_resource_start_failed:postgres", StringComparer.Ordinal)).IsTrue();
                await Assert.That(service.Disposed).IsTrue();
                await Assert.That(causes.Contains("assurance_disposal_fault", StringComparer.Ordinal)).IsTrue();
                await Assert.That(owner.ExecutionSettled).IsTrue();
                await Assert.That(Directory.Exists(owner.Root)).IsTrue();
                verified = true;
            }
            catch (Exception exception)
            {
                testFailure = exception;
                throw;
            }
            finally
            {
                service.Release.TrySetResult();
                if (service.Entered.Task.IsCompleted)
                    await service.StartingExited.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
        catch (AggregateException) when (verified) { }
        catch (Exception cleanupFailure) when (testFailure is not null && !ReferenceEquals(testFailure, cleanupFailure))
        {
            throw new AggregateException(testFailure, cleanupFailure);
        }
        finally
        {
            service.DisarmDisposalFailure();
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

        public void DisarmDisposalFailure() => failDisposal = false;

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
