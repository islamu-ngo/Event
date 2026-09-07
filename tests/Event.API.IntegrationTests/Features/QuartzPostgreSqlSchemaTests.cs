using Event.Api.IntegrationTests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using TUnit.Core;

namespace ApiIntegrationTests.Features;

/// <summary>
/// <see cref="QuartzSchemaInitializerTests"/> reads the embedded scripts as text, which proves they mention
/// the right tables and nothing destructive — but a script can name every table and still be rejected by the
/// engine. Only PostgreSQL had no execution coverage, and it is the default for every non-standalone tier,
/// so this suite runs the real statements through a real server and then schedules real work over the result.
/// </summary>
[Category(TestCategories.Runtime)]
[NotInParallel(SchedulerProofConstraints.LiveScheduler)]
[ClassDataSource<QuartzPostgreSqlSchedulerFixture>(Shared = SharedType.PerAssembly)]
public sealed class QuartzPostgreSqlSchemaTests(QuartzPostgreSqlSchedulerFixture fixture)
{
    private const string TablePrefix = QuartzPostgreSqlSchedulerFixture.TablePrefix;

    /// <summary>The ADO job store cannot operate if any of these is missing.</summary>
    private static readonly string[] RequiredTables =
    [
        "JOB_DETAILS",
        "TRIGGERS",
        "SIMPLE_TRIGGERS",
        "CRON_TRIGGERS",
        "SIMPROP_TRIGGERS",
        "BLOB_TRIGGERS",
        "CALENDARS",
        "PAUSED_TRIGGER_GRPS",
        "FIRED_TRIGGERS",
        "SCHEDULER_STATE",
        "LOCKS"
    ];

    [Test]
    public async Task EmbeddedPostgreSqlSchemaCreatesEveryAdoJobStoreTable()
    {
        fixture.SkipWhenContainerRuntimeUnavailable();
        await fixture.EnsureSchedulerSchemaAsync();

        var tables = await fixture.QuerySchedulerTableNamesAsync();

        foreach (var table in RequiredTables)
        {
            await Assert.That(tables).Contains(TablePrefix + table)
                .Because($"the PostgreSQL scheduler schema must create {TablePrefix}{table} on a real engine.");
        }
    }

    /// <summary>
    /// The script runs on every API start, so a second application must be a no-op rather than an error.
    /// Asserting the table set is unchanged also catches a script that "succeeds" by recreating empty tables.
    /// </summary>
    [Test]
    public async Task ApplyingThePostgreSqlSchemaTwiceIsIdempotent()
    {
        fixture.SkipWhenContainerRuntimeUnavailable();
        await fixture.EnsureSchedulerSchemaAsync();
        var afterFirst = await fixture.QuerySchedulerTableNamesAsync();

        await fixture.ReapplySchedulerSchemaAsync();
        var afterSecond = await fixture.QuerySchedulerTableNamesAsync();

        await Assert.That(afterSecond.Order()).IsEquivalentTo(afterFirst.Order());
    }

    /// <summary>
    /// Quartz probes for optional columns and degrades silently when one is absent, so a structural test is
    /// not enough: this asserts the column exists in the engine's own catalog, where the driver looks.
    /// </summary>
    [Test]
    public async Task PostgreSqlTriggersTableCarriesTheMisfireOriginalFireTimeColumn()
    {
        fixture.SkipWhenContainerRuntimeUnavailable();
        await fixture.EnsureSchedulerSchemaAsync();

        var columnCount = await fixture.CountRowsAsync(
            """
            SELECT count(*) FROM information_schema.columns
            WHERE table_schema = 'public'
              AND upper(table_name) = 'QRTZ_TRIGGERS'
              AND upper(column_name) = 'MISFIRE_ORIG_FIRE_TIME'
            """);

        await Assert.That(columnCount).IsEqualTo(1L);
    }

    /// <summary>
    /// The end of the chain: a scheduler configured exactly as the API host configures it — PostgreSQL
    /// delegate, property storage, schema validation on — must accept a trigger and actually run it.
    /// </summary>
    [Test]
    public async Task AScheduledTriggerPersistsAndFiresUnderThePostgreSqlDelegate()
    {
        fixture.SkipWhenContainerRuntimeUnavailable();
        await fixture.EnsureSchedulerSchemaAsync();

        var jobKey = new JobKey($"postgres-firing-probe-{Guid.CreateVersion7():N}", "tests");
        var triggerKey = new TriggerKey(jobKey.Name, jobKey.Group);
        var probe = PostgreSqlProbeJob.Register(jobKey.Name);

        await using var provider = BuildSchedulerProvider($"postgres-schema-probe-{Guid.CreateVersion7():N}");
        var scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.Start();
        try
        {
            await scheduler.ScheduleJob(
                JobBuilder.Create<PostgreSqlProbeJob>().WithIdentity(jobKey).Build(),
                TriggerBuilder.Create()
                    .WithIdentity(triggerKey)
                    .StartNow()
                    .UsingJobData(PostgreSqlProbeJob.ProbeNameDataKey, jobKey.Name)
                    .Build());

            var storedTrigger = await scheduler.GetTrigger(triggerKey);
            await Assert.That(storedTrigger).IsNotNull();

            var fired = await probe.WaitForExecutionAsync(TimeSpan.FromSeconds(30));
            await Assert.That(fired).IsTrue()
                .Because("a trigger stored by the PostgreSQL delegate must be handed back to the scheduler thread.");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
            PostgreSqlProbeJob.Unregister(jobKey.Name);
        }
    }

    /// <summary>
    /// Mirrors <c>QuartzSchedulerExtensions.ConfigurePersistentStore</c> for the PostgreSQL branch so the
    /// proof covers the host's real configuration rather than a simpler one that happens to work.
    /// <para>
    /// The one deliberate deviation is the ADO data-source name. Quartz keys its process-wide connection
    /// manager by that name, and a production host has exactly one scheduler, so the default is correct
    /// there. A test process builds several, and sharing one name lets the first container disposed shut
    /// the provider down under every other scheduler in the process.
    /// </para>
    /// </summary>
    private ServiceProvider BuildSchedulerProvider(string schedulerName)
    {
        var services = new ServiceCollection();
        services.AddSchedulerProofLogging();
        services.AddQuartz(quartz =>
        {
            quartz.SchedulerName = schedulerName;
            quartz.UseDefaultThreadPool(1);
            quartz.UsePersistentStore(store =>
            {
                store.UseProperties = true;
                store.UseSystemTextJsonSerializer();
                store.PerformSchemaValidation = true;
                store.UsePostgres(
                    ado =>
                    {
                        ado.ConnectionString = fixture.ConnectionString;
                        ado.TablePrefix = TablePrefix;
                    },
                    dataSourceName: schedulerName);
            });
        });

        return services.BuildServiceProvider();
    }
}

/// <summary>
/// Signals its execution through a named registry rather than a single static field, so concurrently
/// running scheduler proofs cannot resolve each other's completion signal.
/// </summary>
public sealed class PostgreSqlProbeJob : IJob
{
    internal const string ProbeNameDataKey = "probeName";

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, TaskCompletionSource> Probes = new();

    internal static ExecutionProbe Register(string probeName)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Probes[probeName] = completion;
        return new ExecutionProbe(completion);
    }

    internal static void Unregister(string probeName) => Probes.TryRemove(probeName, out _);

    public Task Execute(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.MergedJobDataMap.TryGetValue(ProbeNameDataKey, out var rawName) &&
            rawName is string probeName &&
            Probes.TryGetValue(probeName, out var completion))
        {
            completion.TrySetResult();
        }

        return Task.CompletedTask;
    }

    internal sealed class ExecutionProbe(TaskCompletionSource completion)
    {
        public async Task<bool> WaitForExecutionAsync(TimeSpan timeout)
        {
            var finished = await Task.WhenAny(completion.Task, Task.Delay(timeout));
            return finished == completion.Task;
        }
    }
}
