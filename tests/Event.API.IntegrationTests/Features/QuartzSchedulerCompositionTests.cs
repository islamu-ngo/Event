// ABOUTME: Guards scheduler-wide host composition independently from EmailDispatch mode.
// ABOUTME: Prevents a hosted email fallback from disabling unrelated Quartz jobs and operator surfaces.

using Explore.API.Hosting;
using Explore.API.Scheduling;
using Explore.API.Extensions;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Scheduling;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Quartz;
using TUnit.Core;

namespace ApiIntegrationTests.Features;

[NotInParallel(SchedulerProofConstraints.LiveScheduler)]
public sealed class QuartzSchedulerCompositionTests
{
    [Test]
    public async Task CompositionStateUsesSchedulerWideAuthority()
    {
        string[] propertyNames = typeof(ApiHostCompositionState).GetProperties()
            .Select(property => property.Name)
            .ToArray();

        await Assert.That(propertyNames).Contains("UseQuartzScheduler");
        await Assert.That(propertyNames).DoesNotContain("UseQuartzEmailDispatch");
    }

    [Test]
    public async Task SchedulerOwnsAnExactRecurringKeyReconciler()
    {
        Type? reconciler = typeof(ApiHostCompositionState).Assembly.GetType(
            "Explore.API.Scheduling.QuartzOwnedRecurringJobReconciler");

        await Assert.That(reconciler).IsNotNull();
    }

    [Test]
    [Arguments("Explore.API.Scheduling.RegistrationProviderSubmissionWriteDrainJob")]
    [Arguments("Explore.API.Scheduling.RegistrationProviderSubscriptionLifecycleDrainJob")]
    [Arguments("Explore.API.Scheduling.IntegrationSyncDrainJob")]
    [Arguments("Explore.API.Scheduling.LocalWebhookDeliveryDrainJob")]
    [Arguments("Explore.API.Scheduling.IncomingWebhookIntakeDrainJob")]
    [Arguments("Explore.API.Scheduling.IncomingWebhookEffectDrainJob")]
    [Arguments("Explore.API.Scheduling.WebhookBulkReplayDrainJob")]
    [Arguments("Explore.API.Scheduling.WebhookProviderPublicationDrainJob")]
    [Arguments("Explore.API.Scheduling.PdsSyncDrainJob")]
    public async Task QueueDrainsHaveQuartzBoundaries(string typeName)
    {
        Type? jobType = typeof(ApiHostCompositionState).Assembly.GetType(typeName);

        await Assert.That(jobType).IsNotNull();
        await Assert.That(jobType!.GetInterfaces()).Contains(typeof(IJob));
        await Assert.That(jobType.GetCustomAttributes(typeof(DisallowConcurrentExecutionAttribute), inherit: true)).IsNotEmpty();
    }

    [Test]
    public async Task MigratedQueueDrainsHaveExactOwnedRecurringKeysWithoutLegacyWorkers()
    {
        JobKey[] keys =
        [
            QuartzSchedulerKeys.IntegrationSyncDrain,
            QuartzSchedulerKeys.LocalWebhookDeliveryDrain,
            QuartzSchedulerKeys.IncomingWebhookIntakeDrain,
            QuartzSchedulerKeys.IncomingWebhookEffectDrain,
            QuartzSchedulerKeys.WebhookBulkReplayDrain,
            QuartzSchedulerKeys.WebhookProviderPublicationDrain,
            QuartzSchedulerKeys.PdsSyncDrain,
        ];

        foreach (JobKey key in keys)
        {
            await Assert.That(QuartzSchedulerKeys.OwnedRecurringJobs.Count(candidate => candidate == key)).IsEqualTo(1);
            await Assert.That(QuartzSchedulerKeys.RecurringTriggerFor(key).Name).IsEqualTo(key.Name);
        }

        Type apiAssemblyType = typeof(ApiHostCompositionState);
        string[] retiredWorkers =
        [
            "Explore.API.BackgroundServices.IntegrationSyncProcessor",
            "Explore.API.BackgroundServices.WebhookDeliveryProcessor",
            "Explore.API.BackgroundServices.IncomingWebhookProcessor",
            "Explore.API.BackgroundServices.IncomingWebhookEffectProcessor",
            "Explore.API.BackgroundServices.WebhookBulkReplayProcessor",
            "Explore.API.BackgroundServices.WebhookProviderPublicationProcessor",
            "Explore.API.BackgroundServices.PdsSyncWorker",
        ];
        foreach (string typeName in retiredWorkers)
        {
            await Assert.That(apiAssemblyType.Assembly.GetType(typeName)).IsNull();
        }
    }

    [Test]
    public async Task ThinQueueJobsInvokeOnePassInRequiredOrderWithSchedulerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        IJobExecutionContext context = Substitute.For<IJobExecutionContext>();
        context.CancellationToken.Returns(cancellation.Token);

        var integration = Substitute.For<IIntegrationSyncDrainService>();
        integration.ProcessBatchAsync(cancellation.Token).Returns(new IntegrationSyncDrainResult(0, 0, 0, 0, 0, 0, 0));
        await new IntegrationSyncDrainJob(integration, NullLogger<IntegrationSyncDrainJob>.Instance).Execute(context);
        await integration.Received(1).ProcessBatchAsync(cancellation.Token);

        var local = Substitute.For<IWebhookDeliveryDrainService>();
        var localOrder = new List<string>();
        local.RecoverStaleProcessingAsync(cancellation.Token)
            .Returns(_ =>
            {
                localOrder.Add("recover");
                return new WebhookDeliveryRecoveryResult(0, DateTimeOffset.UtcNow);
            });
        local.ProcessBatchAsync(cancellation.Token)
            .Returns(_ =>
            {
                localOrder.Add("drain");
                return new WebhookDeliveryDrainResult(0, 0, 0, 0, 0, 0, 0);
            });
        await new LocalWebhookDeliveryDrainJob(local, NullLogger<LocalWebhookDeliveryDrainJob>.Instance).Execute(context);
        await Assert.That(localOrder.Count).IsEqualTo(2);
        await Assert.That(localOrder[0]).IsEqualTo("recover");
        await Assert.That(localOrder[1]).IsEqualTo("drain");

        var intake = Substitute.For<IIncomingWebhookDrainService>();
        intake.ProcessBatchAsync(cancellation.Token).Returns(new IncomingWebhookDrainResult(0, 0, 0, 0, 0));
        await new IncomingWebhookIntakeDrainJob(intake, NullLogger<IncomingWebhookIntakeDrainJob>.Instance).Execute(context);
        await intake.Received(1).ProcessBatchAsync(cancellation.Token);

        var effect = Substitute.For<IIncomingWebhookEffectDrainService>();
        effect.ProcessBatchAsync(cancellation.Token).Returns(new IncomingWebhookDrainResult(0, 0, 0, 0, 0));
        await new IncomingWebhookEffectDrainJob(effect, NullLogger<IncomingWebhookEffectDrainJob>.Instance).Execute(context);
        await effect.Received(1).ProcessBatchAsync(cancellation.Token);

        var replay = Substitute.For<IWebhookBulkReplayService>();
        replay.ProcessQueuedAsync(cancellation.Token).Returns(new WebhookBulkReplayProcessResult(0, 0, 0));
        await new WebhookBulkReplayDrainJob(replay, NullLogger<WebhookBulkReplayDrainJob>.Instance).Execute(context);
        await replay.Received(1).ProcessQueuedAsync(cancellation.Token);

        var publication = Substitute.For<IWebhookProviderPublicationDrainService>();
        var publicationOrder = new List<string>();
        publication.ProcessBatchAsync(cancellation.Token)
            .Returns(_ =>
            {
                publicationOrder.Add("publish");
                return new WebhookProviderPublicationDrainResult(0, 0, 0, 0, 0, 0, 0);
            });
        publication.ProcessReconciliationBatchAsync(cancellation.Token)
            .Returns(_ =>
            {
                publicationOrder.Add("reconcile");
                return new WebhookProviderReconciliationDrainResult(0, 0, 0, 0, 0, 0, 0, 0);
            });
        await new WebhookProviderPublicationDrainJob(
            publication,
            NullLogger<WebhookProviderPublicationDrainJob>.Instance).Execute(context);
        await Assert.That(publicationOrder.Count).IsEqualTo(2);
        await Assert.That(publicationOrder[0]).IsEqualTo("publish");
        await Assert.That(publicationOrder[1]).IsEqualTo("reconcile");

        var pds = Substitute.For<IPdsSyncDrainService>();
        pds.ProcessBatchAsync(cancellation.Token).Returns(new PdsSyncDrainResult(0, 0, 0, 0));
        await new PdsSyncDrainJob(pds, NullLogger<PdsSyncDrainJob>.Instance).Execute(context);
        await pds.Received(1).ProcessBatchAsync(cancellation.Token);
    }

    [Test]
    public async Task ThinQueueJobPropagatesUnexpectedFailure()
    {
        IJobExecutionContext context = Substitute.For<IJobExecutionContext>();
        var service = Substitute.For<IPdsSyncDrainService>();
        service.ProcessBatchAsync(Arg.Any<CancellationToken>()).Returns<Task<PdsSyncDrainResult>>(_ =>
            throw new InvalidOperationException("test failure"));
        var job = new PdsSyncDrainJob(service, NullLogger<PdsSyncDrainJob>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => job.Execute(context));
    }

    [Test]
    public async Task EveryThinQueueJobPropagatesFailureAndStopsItsOrderedPass()
    {
        IJobExecutionContext context = Substitute.For<IJobExecutionContext>();

        var integration = Substitute.For<IIntegrationSyncDrainService>();
        integration.ProcessBatchAsync(Arg.Any<CancellationToken>()).Returns<Task<IntegrationSyncDrainResult>>(_ =>
            throw new InvalidOperationException("integration"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new IntegrationSyncDrainJob(integration, NullLogger<IntegrationSyncDrainJob>.Instance).Execute(context));

        var local = Substitute.For<IWebhookDeliveryDrainService>();
        local.RecoverStaleProcessingAsync(Arg.Any<CancellationToken>()).Returns<Task<WebhookDeliveryRecoveryResult>>(_ =>
            throw new InvalidOperationException("local"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new LocalWebhookDeliveryDrainJob(local, NullLogger<LocalWebhookDeliveryDrainJob>.Instance).Execute(context));
        await local.DidNotReceive().ProcessBatchAsync(Arg.Any<CancellationToken>());

        var intake = Substitute.For<IIncomingWebhookDrainService>();
        intake.ProcessBatchAsync(Arg.Any<CancellationToken>()).Returns<Task<IncomingWebhookDrainResult>>(_ =>
            throw new InvalidOperationException("intake"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new IncomingWebhookIntakeDrainJob(intake, NullLogger<IncomingWebhookIntakeDrainJob>.Instance).Execute(context));

        var effect = Substitute.For<IIncomingWebhookEffectDrainService>();
        effect.ProcessBatchAsync(Arg.Any<CancellationToken>()).Returns<Task<IncomingWebhookDrainResult>>(_ =>
            throw new InvalidOperationException("effect"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new IncomingWebhookEffectDrainJob(effect, NullLogger<IncomingWebhookEffectDrainJob>.Instance).Execute(context));

        var replay = Substitute.For<IWebhookBulkReplayService>();
        replay.ProcessQueuedAsync(Arg.Any<CancellationToken>()).Returns<Task<WebhookBulkReplayProcessResult>>(_ =>
            throw new InvalidOperationException("replay"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new WebhookBulkReplayDrainJob(replay, NullLogger<WebhookBulkReplayDrainJob>.Instance).Execute(context));

        var publication = Substitute.For<IWebhookProviderPublicationDrainService>();
        publication.ProcessBatchAsync(Arg.Any<CancellationToken>()).Returns<Task<WebhookProviderPublicationDrainResult>>(_ =>
            throw new InvalidOperationException("publication"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new WebhookProviderPublicationDrainJob(
                publication,
                NullLogger<WebhookProviderPublicationDrainJob>.Instance).Execute(context));
        await publication.DidNotReceive().ProcessReconciliationBatchAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task EnabledQueueDrainsComposeExactPayloadFreeJobsAndStableTriggers()
    {
        await using ServiceProvider provider = BuildSchedulerProvider(new Dictionary<string, string?>());
        QuartzRecurringJobManifest manifest = provider.GetRequiredService<QuartzRecurringJobManifest>();
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        JobKey[] expected = MigratedQueueKeys();

        try
        {
            foreach (JobKey key in expected)
            {
                await Assert.That(manifest.Desired.Count(candidate => candidate == key)).IsEqualTo(1);
                IJobDetail? job = await scheduler.GetJobDetail(key);
                ITrigger? trigger = await scheduler.GetTrigger(QuartzSchedulerKeys.RecurringTriggerFor(key));
                await Assert.That(job).IsNotNull();
                await Assert.That(job!.JobDataMap.Count).IsEqualTo(0);
                await Assert.That(trigger).IsNotNull();
                await Assert.That(trigger!.JobDataMap.Count).IsEqualTo(0);
                await Assert.That(trigger.JobKey).IsEqualTo(key);
            }
        }
        finally
        {
            await scheduler.Shutdown(false);
        }
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task TransientCleanupRemainsScheduledWhenAtprotoLoginIsDisabled(bool atprotoEnabled)
    {
        await using ServiceProvider provider = BuildSchedulerProvider(new Dictionary<string, string?>
        {
            ["Authentication:AtprotoLoginEnabled"] = atprotoEnabled.ToString(),
            ["Atproto:Enabled"] = atprotoEnabled.ToString(),
            ["Atproto:PdsSync:Enabled"] = "false"
        });
        QuartzRecurringJobManifest manifest = provider.GetRequiredService<QuartzRecurringJobManifest>();
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        var key = new JobKey("atproto-transient-cleanup", QuartzSchedulerKeys.RecurringGroup);

        try
        {
            await Assert.That(manifest.Owned).Contains(key);
            await Assert.That(manifest.Desired.Count(candidate => candidate.Equals(key))).IsEqualTo(1);
            await Assert.That(ScheduledJobNames.All).Contains("atproto-transient-cleanup");
            IJobDetail? job = await scheduler.GetJobDetail(key);
            await Assert.That(job).IsNotNull();
            await Assert.That(job!.ConcurrentExecutionDisallowed).IsTrue();
            await Assert.That(job.JobDataMap.Count).IsEqualTo(0);
            IReadOnlyCollection<ITrigger> triggers = await scheduler.GetTriggersOfJob(key);
            await Assert.That(triggers.Count).IsEqualTo(1);
            var trigger = triggers.Single() as ISimpleTrigger;
            await Assert.That(trigger).IsNotNull();
            await Assert.That(trigger!.RepeatInterval).IsEqualTo(TimeSpan.FromMinutes(1));
            await Assert.That(trigger.RepeatCount).IsEqualTo(-1);
            await Assert.That(trigger.Key).IsEqualTo(QuartzSchedulerKeys.RecurringTriggerFor(key));
            await Assert.That(trigger.JobDataMap.Count).IsEqualTo(0);
        }
        finally
        {
            await scheduler.Shutdown(false);
        }
    }

    [Test]
    public async Task DisabledQueueLanesAreAbsentFromDesiredManifestWhileRemainingOwnedForCleanup()
    {
        (string Setting, JobKey[] Keys)[] lanes =
        [
            ("IntegrationSyncProcessor:Enabled", [QuartzSchedulerKeys.IntegrationSyncDrain]),
            ("WebhookDeliveryProcessor:Enabled", [QuartzSchedulerKeys.LocalWebhookDeliveryDrain]),
            ("Webhooks:IncomingProcessing:Enabled", [
                QuartzSchedulerKeys.IncomingWebhookIntakeDrain,
                QuartzSchedulerKeys.IncomingWebhookEffectDrain]),
            ("WebhookBulkReplay:Enabled", [QuartzSchedulerKeys.WebhookBulkReplayDrain]),
            ("WebhookProviderPublicationProcessor:Enabled", [QuartzSchedulerKeys.WebhookProviderPublicationDrain]),
            ("Atproto:PdsSync:Enabled", [QuartzSchedulerKeys.PdsSyncDrain])
        ];

        foreach ((string setting, JobKey[] keys) in lanes)
        {
            await using ServiceProvider provider = BuildSchedulerProvider(new Dictionary<string, string?>
            {
                [setting] = "false"
            });
            QuartzRecurringJobManifest manifest = provider.GetRequiredService<QuartzRecurringJobManifest>();
            foreach (JobKey key in keys)
            {
                await Assert.That(manifest.Owned).Contains(key);
                await Assert.That(manifest.Desired).DoesNotContain(key);
            }
        }
    }

    [Test]
    public async Task GloballyDisabledSchedulerRetainsNoOpDeadlineDispatcher()
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Scheduler:Quartz:Enabled"] = "false",
                ["Scheduler:Quartz:UsePersistentStore"] = "false",
                ["Database:Provider"] = "Sqlite",
                ["Database:Database"] = Path.Combine(Path.GetTempPath(), "disabled-scheduler-composition.db")
            }).Build();
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.EnvironmentName.Returns("Production");
        var services = new ServiceCollection();
        services.AddScoped<IScheduledDeadlineDispatcher, NoOpScheduledDeadlineDispatcher>();

        services.AddApiQuartzScheduler(configuration, environment, enabled: true, useQuartzEmailDispatch: false);
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();

        IScheduledDeadlineDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<IScheduledDeadlineDispatcher>();
        await Assert.That(dispatcher).IsTypeOf<NoOpScheduledDeadlineDispatcher>();
        await Assert.That(provider.GetServices<IHostedService>())
            .DoesNotContain(service => service is QuartzOwnedRecurringJobReconciler);
    }

    private static JobKey[] MigratedQueueKeys() =>
    [
        QuartzSchedulerKeys.RegistrationProviderSubmissionWriteDrain,
        QuartzSchedulerKeys.RegistrationProviderSubscriptionLifecycleDrain,
        QuartzSchedulerKeys.IntegrationSyncDrain,
        QuartzSchedulerKeys.LocalWebhookDeliveryDrain,
        QuartzSchedulerKeys.IncomingWebhookIntakeDrain,
        QuartzSchedulerKeys.IncomingWebhookEffectDrain,
        QuartzSchedulerKeys.WebhookBulkReplayDrain,
        QuartzSchedulerKeys.WebhookProviderPublicationDrain,
        QuartzSchedulerKeys.PdsSyncDrain
    ];

    private static ServiceProvider BuildSchedulerProvider(IReadOnlyDictionary<string, string?> overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["Scheduler:Quartz:Enabled"] = "true",
            ["Scheduler:Quartz:UsePersistentStore"] = "false",
            ["Database:Provider"] = "Sqlite",
            ["Database:Database"] = Path.Combine(Path.GetTempPath(), "queue-drain-composition.db"),
            ["WebhookProviderPublicationProcessor:Enabled"] = "true"
        };
        foreach ((string key, string? value) in overrides)
        {
            values[key] = value;
        }

        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Production);
        var services = new ServiceCollection();
        services.AddSchedulerProofLogging();
        services.AddScoped<IScheduledDeadlineDispatcher, NoOpScheduledDeadlineDispatcher>();
        services.AddSingleton(Substitute.For<ISchedulerJobTelemetry>());
        services.AddApiQuartzScheduler(configuration, environment, enabled: true, useQuartzEmailDispatch: false);
        return services.BuildServiceProvider();
    }
}
