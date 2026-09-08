using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using TUnit.Core;

namespace ApiIntegrationTests.Features;

/// <summary>
/// Clustering is configured and validated in this platform but was never executed, which made scale-out the
/// largest unproven operational claim: two replicas would either share the work correctly or silently run
/// every cron job twice, and nothing distinguished those outcomes.
/// <para>
/// The store is PostgreSQL rather than SQLite on purpose. Quartz's clustered lock handler serializes nodes
/// with row-level locking against the <c>LOCKS</c> table, which SQLite's single-writer file model cannot
/// provide in the same form — a SQLite "pass" would prove serialization by accident of the file lock, not by
/// the mechanism a real deployment uses.
/// </para>
/// </summary>
[Category(TestCategories.Runtime)]
[NotInParallel(SchedulerProofConstraints.LiveScheduler)]
[ClassDataSource<QuartzPostgreSqlSchedulerFixture>(Shared = SharedType.PerAssembly)]
public sealed class QuartzClusteringTests(QuartzPostgreSqlSchedulerFixture fixture)
{
    private const string TablePrefix = QuartzPostgreSqlSchedulerFixture.TablePrefix;

    /// <summary>Short enough that both nodes are registered well before the trigger becomes due.</summary>
    private static readonly TimeSpan CheckinInterval = TimeSpan.FromSeconds(1);

    /// <summary>The trigger starts in the future so both nodes are polling and genuinely racing for it.</summary>
    private static readonly TimeSpan TriggerLead = TimeSpan.FromSeconds(3);

    /// <summary>Time allowed after the first execution for a duplicate to show up on the other node.</summary>
    private static readonly TimeSpan DuplicateObservationWindow = TimeSpan.FromSeconds(6);

    [Test]
    public async Task TwoClusteredSchedulersOverOnePostgreSqlStoreExecuteOneTriggerExactlyOnce()
    {
        fixture.SkipWhenContainerRuntimeUnavailable();
        await fixture.EnsureSchedulerSchemaAsync();

        var clusterName = $"cluster-probe-{Guid.CreateVersion7():N}";
        var jobKey = new JobKey($"cluster-fire-{Guid.CreateVersion7():N}", "tests");
        var probe = ClusteredProbeJob.Register(jobKey.Name);

        await using var firstNode = BuildClusteredNode(clusterName);
        await using var secondNode = BuildClusteredNode(clusterName);
        var firstScheduler = await firstNode.GetRequiredService<ISchedulerFactory>().GetScheduler();
        var secondScheduler = await secondNode.GetRequiredService<ISchedulerFactory>().GetScheduler();

        await firstScheduler.Start();
        await secondScheduler.Start();
        try
        {
            // Scheduled through one node only; in a cluster the store, not the submitting node, decides
            // which instance acquires the trigger.
            await firstScheduler.ScheduleJob(
                JobBuilder.Create<ClusteredProbeJob>().WithIdentity(jobKey).StoreDurably().Build(),
                TriggerBuilder.Create()
                    .WithIdentity(jobKey.Name, jobKey.Group)
                    .StartAt(DateTimeOffset.UtcNow.Add(TriggerLead))
                    .UsingJobData(ClusteredProbeJob.ProbeNameDataKey, jobKey.Name)
                    .Build());

            var fired = await probe.WaitForFirstExecutionAsync(TriggerLead + TimeSpan.FromSeconds(30));
            await Assert.That(fired).IsTrue().Because("a clustered trigger must still run somewhere.");

            // The failure this test exists to catch is a *second* execution, which by definition arrives
            // after the first, so the assertion is only meaningful once the window has elapsed.
            await Task.Delay(DuplicateObservationWindow);

            await Assert.That(probe.ExecutionCount).IsEqualTo(1)
                .Because("two clustered nodes sharing one store must not both run the same trigger.");
        }
        finally
        {
            await firstScheduler.Shutdown(waitForJobsToComplete: false);
            await secondScheduler.Shutdown(waitForJobsToComplete: false);
            ClusteredProbeJob.Unregister(jobKey.Name);
        }
    }

    /// <summary>
    /// Distinct rows in <c>QRTZ_SCHEDULER_STATE</c> are what makes the single-execution result meaningful:
    /// without them the test could pass simply because only one node ever joined the cluster.
    /// </summary>
    [Test]
    public async Task BothClusteredNodesRegisterDistinctInstancesInSchedulerState()
    {
        fixture.SkipWhenContainerRuntimeUnavailable();
        await fixture.EnsureSchedulerSchemaAsync();

        var clusterName = $"cluster-state-{Guid.CreateVersion7():N}";

        await using var firstNode = BuildClusteredNode(clusterName);
        await using var secondNode = BuildClusteredNode(clusterName);
        var firstScheduler = await firstNode.GetRequiredService<ISchedulerFactory>().GetScheduler();
        var secondScheduler = await secondNode.GetRequiredService<ISchedulerFactory>().GetScheduler();

        await firstScheduler.Start();
        await secondScheduler.Start();
        try
        {
            await Assert.That(firstScheduler.SchedulerInstanceId).IsNotEqualTo(secondScheduler.SchedulerInstanceId)
                .Because($"{QuartzSchedulerSettings.AutoInstanceId} instance ids must be unique per process.");

            var registeredNodes = await WaitForRegisteredNodesAsync(clusterName, expected: 2, TimeSpan.FromSeconds(30));

            await Assert.That(registeredNodes).IsEqualTo(2L);
        }
        finally
        {
            await firstScheduler.Shutdown(waitForJobsToComplete: false);
            await secondScheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    private async Task<long> WaitForRegisteredNodesAsync(string clusterName, long expected, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        long observed;
        do
        {
            observed = await fixture.CountRowsAsync(
                $"SELECT count(*) FROM {TablePrefix}SCHEDULER_STATE WHERE SCHED_NAME = '{clusterName}'");
            if (observed >= expected)
            {
                return observed;
            }

            await Task.Delay(CheckinInterval);
        } while (DateTimeOffset.UtcNow < deadline);

        return observed;
    }

    /// <summary>
    /// One cluster member, configured the way <c>QuartzSchedulerExtensions</c> configures a clustered host:
    /// shared scheduler name, <c>AUTO</c> instance id, clustering over the persistent store.
    /// </summary>
    private ServiceProvider BuildClusteredNode(string clusterName)
    {
        var services = new ServiceCollection();
        services.AddSchedulerProofLogging();
        services.AddQuartz(quartz =>
        {
            quartz.SchedulerName = clusterName;
            quartz.SchedulerId = QuartzSchedulerSettings.AutoInstanceId;
            quartz.UseDefaultThreadPool(1);

            // A node only learns about work another node scheduled by polling. The 30-second production
            // default would make this test's outcome depend on which node happens to submit the trigger;
            // one second keeps both nodes genuinely competing for it within the test's patience.
            quartz.SetProperty("quartz.scheduler.idleWaitTime", "1000");
            quartz.UsePersistentStore(store =>
            {
                store.UseProperties = true;
                store.UseSystemTextJsonSerializer();
                store.PerformSchemaValidation = true;
                // Each node gets its own ADO data-source name: Quartz's connection manager is process-wide
                // and keyed by that name, so two in-process nodes sharing it would also share — and tear
                // down — one provider. Real cluster members are separate processes and never collide.
                store.UsePostgres(
                    ado =>
                    {
                        ado.ConnectionString = fixture.ConnectionString;
                        ado.TablePrefix = TablePrefix;
                    },
                    dataSourceName: $"{clusterName}-{Guid.CreateVersion7():N}");
                store.UseClustering(clustering => clustering.CheckinInterval = CheckinInterval);
            });
        });

        return services.BuildServiceProvider();
    }
}

/// <summary>
/// Counts executions per probe name across every scheduler instance in the process, which is what makes a
/// duplicate fire observable at all: both cluster nodes run inside this one test host.
/// </summary>
public sealed class ClusteredProbeJob : IJob
{
    internal const string ProbeNameDataKey = "probeName";

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ExecutionCounter> Probes = new();

    internal static ExecutionCounter Register(string probeName)
    {
        var counter = new ExecutionCounter();
        Probes[probeName] = counter;
        return counter;
    }

    internal static void Unregister(string probeName) => Probes.TryRemove(probeName, out _);

    public Task Execute(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.MergedJobDataMap.TryGetValue(ProbeNameDataKey, out var rawName) &&
            rawName is string probeName &&
            Probes.TryGetValue(probeName, out var counter))
        {
            counter.RecordExecution();
        }

        return Task.CompletedTask;
    }

    internal sealed class ExecutionCounter
    {
        private readonly TaskCompletionSource _firstExecution = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _executionCount;

        public int ExecutionCount => Volatile.Read(ref _executionCount);

        public void RecordExecution()
        {
            Interlocked.Increment(ref _executionCount);
            _firstExecution.TrySetResult();
        }

        public async Task<bool> WaitForFirstExecutionAsync(TimeSpan timeout)
        {
            var finished = await Task.WhenAny(_firstExecution.Task, Task.Delay(timeout));
            return finished == _firstExecution.Task;
        }
    }
}
