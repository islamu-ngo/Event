using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Scheduling;
using Explore.Secrets.Database;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using TUnit.Core;

namespace ApiIntegrationTests.Features;

[NotInParallel(SchedulerProofConstraints.LiveScheduler)]
public sealed class QuartzSqliteDurableSchedulingTests
{
    private const string TablePrefix = "QRTZ_";

    [Test]
    public async Task EmbeddedSqliteSchemaCreatesEveryAdoJobStoreTable()
    {
        using var database = new TemporarySqliteDatabase();
        await database.ApplySchedulerSchemaAsync();

        var tables = await database.QueryTableNamesAsync();

        await Assert.That(tables).Contains(TablePrefix + "JOB_DETAILS");
        await Assert.That(tables).Contains(TablePrefix + "TRIGGERS");
        await Assert.That(tables).Contains(TablePrefix + "CRON_TRIGGERS");
        await Assert.That(tables).Contains(TablePrefix + "SIMPLE_TRIGGERS");
        await Assert.That(tables).Contains(TablePrefix + "LOCKS");
    }

    [Test]
    public async Task ApplyingTheSqliteSchemaTwiceIsIdempotent()
    {
        using var database = new TemporarySqliteDatabase();
        await database.ApplySchedulerSchemaAsync();
        await database.ApplySchedulerSchemaAsync();

        var tables = await database.QueryTableNamesAsync();

        await Assert.That(tables).Contains(TablePrefix + "JOB_DETAILS");
    }

    [Test]
    public async Task ScheduledJobStateSurvivesASchedulerRestartOnSqlite()
    {
        using var database = new TemporarySqliteDatabase();
        await database.ApplySchedulerSchemaAsync();

        var jobKey = new JobKey("durable-probe", "tests");
        var triggerKey = new TriggerKey("durable-probe-trigger", "tests");

        // First scheduler instance persists a far-future trigger, then shuts down entirely.
        var firstScheduler = await database.CreateSchedulerAsync();
        await firstScheduler.Start();
        await firstScheduler.ScheduleJob(
            JobBuilder.Create<NoOpProbeJob>().WithIdentity(jobKey).Build(),
            TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .StartAt(DateTimeOffset.UtcNow.AddHours(1))
                .UsingJobData("pointer", "pointer-only-payload")
                .Build());
        await firstScheduler.Shutdown(waitForJobsToComplete: true);

        // A brand new scheduler over the same file must rediscover the persisted job and its payload.
        var secondScheduler = await database.CreateSchedulerAsync();
        await secondScheduler.Start();
        try
        {
            var recoveredJob = await secondScheduler.GetJobDetail(jobKey);
            var recoveredTrigger = await secondScheduler.GetTrigger(triggerKey);

            await Assert.That(recoveredJob).IsNotNull();
            await Assert.That(recoveredTrigger).IsNotNull();
            await Assert.That(recoveredTrigger!.JobDataMap.GetString("pointer")).IsEqualTo("pointer-only-payload");
        }
        finally
        {
            await secondScheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    [Test]
    public async Task AnImmediateTriggerActuallyExecutesOnSqlite()
    {
        using var database = new TemporarySqliteDatabase();
        await database.ApplySchedulerSchemaAsync();

        NoOpProbeJob.Reset();
        var scheduler = await database.CreateSchedulerAsync();
        await scheduler.Start();
        try
        {
            await scheduler.ScheduleJob(
                JobBuilder.Create<NoOpProbeJob>().WithIdentity("firing-probe", "tests").Build(),
                TriggerBuilder.Create().WithIdentity("firing-probe-trigger", "tests").StartNow().Build());

            var fired = await NoOpProbeJob.WaitForExecutionAsync(TimeSpan.FromSeconds(30));

            await Assert.That(fired).IsTrue();
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    /// <summary>Records that it ran so a test can prove the store hands work back to the scheduler thread.</summary>
    private sealed class NoOpProbeJob : IJob
    {
        private static TaskCompletionSource _executed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static void Reset()
        {
            _executed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public static async Task<bool> WaitForExecutionAsync(TimeSpan timeout)
        {
            var completed = await Task.WhenAny(_executed.Task, Task.Delay(timeout));
            return completed == _executed.Task;
        }

        public Task Execute(IJobExecutionContext context)
        {
            _executed.TrySetResult();
            return Task.CompletedTask;
        }
    }

    /// <summary>Owns a throwaway SQLite file plus the scheduler wiring pointed at it.</summary>
    private sealed class TemporarySqliteDatabase : IDisposable
    {
        private readonly string _path = Path.Combine(
            Path.GetTempPath(),
            $"quartz-sqlite-{Guid.CreateVersion7():N}.db");

        private readonly List<ServiceProvider> _providers = [];

        private string ConnectionString => $"Data Source={_path};Cache=Shared";

        public async Task ApplySchedulerSchemaAsync()
        {
            await using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();

            foreach (var statement in QuartzSchemaInitializer.BuildStatements(
                         PrimaryDatabaseProvider.Sqlite,
                         TablePrefix))
            {
                await using var command = connection.CreateCommand();
                command.CommandText = statement;
                await command.ExecuteNonQueryAsync();
            }
        }

        public async Task<IReadOnlyList<string>> QueryTableNamesAsync()
        {
            await using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";

            List<string> names = [];
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                names.Add(reader.GetString(0));
            }

            return names;
        }

        public async Task<IScheduler> CreateSchedulerAsync()
        {
            var services = new ServiceCollection();
            services.AddSchedulerProofLogging();
            services.AddQuartz(quartz =>
            {
                quartz.SchedulerName = "sqlite-durability-probe";
                quartz.UseDefaultThreadPool(1);
                quartz.UsePersistentStore(store =>
                {
                    store.UseProperties = true;
                    store.UseSystemTextJsonSerializer();
                    // Quartz's connection manager is process-wide and keyed by data-source name. Several
                    // schedulers live in one test process, so each needs its own name; otherwise disposing
                    // one container shuts the shared provider down under the others.
                    store.UseMicrosoftSQLite(
                        ado =>
                        {
                            ado.ConnectionString = ConnectionString;
                            ado.TablePrefix = TablePrefix;
                        },
                        dataSourceName: $"sqlite-probe-{Guid.CreateVersion7():N}");
                });
            });

            var provider = services.BuildServiceProvider();
            _providers.Add(provider);
            return await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        }

        public void Dispose()
        {
            foreach (var provider in _providers)
            {
                provider.Dispose();
            }

            SqliteConnection.ClearAllPools();
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
    }
}
