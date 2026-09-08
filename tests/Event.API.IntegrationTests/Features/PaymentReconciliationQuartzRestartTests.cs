using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Scheduling;
using Explore.Application.Contracts.Payments;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Services.Registration;
using Explore.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Quartz;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel(SchedulerProofConstraints.LiveScheduler)]
[ClassDataSource<QuartzPostgreSqlSchedulerFixture>(Shared = SharedType.PerAssembly)]
public sealed class PaymentReconciliationQuartzRestartTests(QuartzPostgreSqlSchedulerFixture fixture)
{
    [Test]
    public async Task PersistedPaymentDrainTriggerContinuesOnFreshSchedulerHostAfterRestart()
    {
        fixture.SkipWhenContainerRuntimeUnavailable();
        await fixture.EnsureSchedulerSchemaAsync();
        string schedulerName = $"payment-restart-{Guid.CreateVersion7():N}";
        var jobKey = new JobKey($"payment-drain-{Guid.CreateVersion7():N}", "tests");
        var claimObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using (ServiceProvider firstHost = BuildNode(schedulerName, new TaskCompletionSource()))
        {
            IScheduler first = await firstHost.GetRequiredService<ISchedulerFactory>().GetScheduler();
            await first.Start();
            await first.ScheduleJob(
                JobBuilder.Create<PaymentReconciliationDrainJob>().WithIdentity(jobKey).StoreDurably().Build(),
                TriggerBuilder.Create().WithIdentity(jobKey.Name, jobKey.Group)
                    .ForJob(jobKey).StartAt(DateTimeOffset.UtcNow.AddSeconds(10)).Build());
            await first.Shutdown(waitForJobsToComplete: false);
        }

        long persisted = await fixture.CountRowsAsync(
            $"SELECT count(*) FROM {QuartzPostgreSqlSchedulerFixture.TablePrefix}TRIGGERS WHERE SCHED_NAME = '{schedulerName}' AND JOB_NAME = '{jobKey.Name}'");
        await Assert.That(persisted).IsEqualTo(1L);

        await using ServiceProvider secondHost = BuildNode(schedulerName, claimObserved);
        IScheduler second = await secondHost.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await second.Start();
        Task completed = await Task.WhenAny(claimObserved.Task, Task.Delay(TimeSpan.FromSeconds(30)));
        await second.Shutdown(waitForJobsToComplete: true);

        await Assert.That(completed).IsSameReferenceAs(claimObserved.Task);
    }

    private ServiceProvider BuildNode(string schedulerName, TaskCompletionSource claimObserved)
    {
        var repository = Substitute.For<IRegistrationPaymentAttemptRepository>();
        repository.ClaimDueDispatchEffectsAsync(
                Arg.Any<string>(), Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns([]);
        repository.ClaimDueReconciliationsAsync(
                "payment-reconciliation-drain-job", 50, Arg.Any<DateTime>(), TimeSpan.FromMinutes(2), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                claimObserved.TrySetResult();
                return [];
            });
        var checkout = Substitute.For<IHostedCheckoutSessionRetriever>();
        var payment = Substitute.For<IPaymentIntentRetriever>();
        var activation = Substitute.For<IPaidCheckoutActivationService>();
        activation.EvaluateAsync(Arg.Any<PaidCheckoutActivationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PaidCheckoutActivationResult(true, null, "active"));
        var services = new ServiceCollection();
        services.AddSchedulerProofLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton(repository);
        services.AddSingleton(checkout);
        services.AddSingleton(payment);
        services.AddSingleton(Substitute.For<IHostedCheckoutSessionCreator>());
        services.AddSingleton(Substitute.For<IRegistrationOrderLifecycleService>());
        services.AddSingleton<IPaidCheckoutActivationService>(activation);
        var freshness = Substitute.For<IPaidOrderAcceptanceFreshnessService>();
        freshness.IsCurrentAsync(Arg.Any<PaymentAttempt>(), Arg.Any<CancellationToken>()).Returns(true);
        services.AddSingleton<IPaidOrderAcceptanceFreshnessService>(freshness);
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddScoped(provider => new RegistrationPaymentCheckoutDispatchService(
            provider.GetRequiredService<IRegistrationPaymentAttemptRepository>(),
            provider.GetRequiredService<IHostedCheckoutSessionCreator>(),
            provider.GetRequiredService<IHostedCheckoutSessionRetriever>(),
            provider.GetRequiredService<IPaymentIntentRetriever>(),
            provider.GetRequiredService<IRegistrationOrderLifecycleService>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IPaidCheckoutActivationService>(),
            provider.GetRequiredService<IPaidOrderAcceptanceFreshnessService>()));
        services.AddScoped(provider => new RegistrationPaymentReconciliationService(
            provider.GetRequiredService<IRegistrationPaymentAttemptRepository>(),
            provider.GetRequiredService<IHostedCheckoutSessionRetriever>(),
            provider.GetRequiredService<IPaymentIntentRetriever>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddScoped(provider => new PaymentReconciliationDrainJob(
            provider.GetRequiredService<RegistrationPaymentCheckoutDispatchService>(),
            provider.GetRequiredService<RegistrationPaymentReconciliationService>(),
            provider.GetRequiredService<IConfiguration>(),
            NullLogger<PaymentReconciliationDrainJob>.Instance));
        services.AddQuartz(quartz =>
        {
            quartz.SchedulerName = schedulerName;
            quartz.SchedulerId = "AUTO";
            quartz.UseDefaultThreadPool(1);
            quartz.UsePersistentStore(store =>
            {
                store.UseProperties = true;
                store.UseSystemTextJsonSerializer();
                store.PerformSchemaValidation = true;
                store.UsePostgres(ado =>
                {
                    ado.ConnectionString = fixture.ConnectionString;
                    ado.TablePrefix = QuartzPostgreSqlSchedulerFixture.TablePrefix;
                }, dataSourceName: $"{schedulerName}-{Guid.CreateVersion7():N}");
            });
        });
        return services.BuildServiceProvider();
    }
}
