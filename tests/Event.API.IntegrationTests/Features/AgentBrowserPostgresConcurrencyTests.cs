using System.Data.Common;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.BackgroundServices;
using Explore.API.Hosting;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.AiAssistant.Requests.Commands;
using Microsoft.Extensions.Logging.Abstractions;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quartz;
using Quartz.Impl;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class AgentBrowserPostgresConcurrencyTests
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task ResetDrainsActualRequestAndOutboxUnitsBeforePurging(bool releaseRequestFirst)
    {
        string commandPipe = $"islamu-agent-database-reset-{Guid.NewGuid():N}";
        var requestBarrier = new RequestBarrier();
        var dispatch = new BlockingDispatcher();
        await using var fixture = await AgentBrowserPersonaFixture.CreateAsync(enableReset: true, configure: services =>
        {
            services.AddSingleton<IInterceptor>(requestBarrier);
            services.RemoveAll<IOutboxMessageDispatcher>();
            services.AddSingleton<IOutboxMessageDispatcher>(dispatch);
        }, resetPipeName: commandPipe);
        await fixture.RunAsync();
        dispatch.Services = fixture.Services;
        var coordinator = fixture.Services.GetRequiredService<AgentBrowserResetCoordinator>();
        string token = await fixture.LoginAsync(AgentBrowserPersonaCatalog.Organizer);
        Guid stamp;
        Guid messageId = Guid.CreateVersion7();
        await using (var database = fixture.CreateDatabase())
        {
            stamp = (await database.Events.SingleAsync(row => row.Id == AgentBrowserPersonaCatalog.EventId)).ConcurrencyStamp;
            database.Set<OutboxMessage>().Add(new OutboxMessage
            {
                Id = messageId,
                AggregateId = AgentBrowserPersonaCatalog.EventId,
                AggregateType = "Event",
                EventType = "reset-concurrency-invariant",
                Status = OutboxMessageStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                MaxRetries = 3
            });
            await database.SaveChangesAsync();
        }
        using var client = fixture.CreateTenantClient("default", token);
        using var patch = new HttpRequestMessage(HttpMethod.Patch, $"/api/event/{AgentBrowserPersonaCatalog.EventId:D}")
        {
            Content = new StringContent("{\"title\":{\"value\":\"Changed before reset\"}}", Encoding.UTF8, "application/json")
        };
        patch.Headers.TryAddWithoutValidation("If-Match", $"\"{stamp:D}\"");
        requestBarrier.Armed = true;
        Task<HttpResponseMessage> request = client.SendAsync(patch);
        var outbox = ActivatorUtilities.CreateInstance<OutboxProcessor>(fixture.Services);
        Task worker = outbox.ProcessOutboxBatchAsync(TestContext.Current!.Execution.CancellationToken);
        try
        {
            await Task.WhenAll(requestBarrier.Entered.Task, dispatch.Entered.Task).WaitAsync(TimeSpan.FromSeconds(20));
            Task maintenance = coordinator.WaitForMaintenanceAsync();
            Task<string> reset = AgentDatabaseResetLifecycleTests.ResetThroughPipeAsync(commandPipe);
            await maintenance.WaitAsync(TimeSpan.FromSeconds(20));
            using (var rejected = await client.GetAsync("/api/user"))
                await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
            await Assert.That(coordinator.Drained.IsCompleted).IsFalse();
            await Assert.That(await AgentDatabaseResetLifecycleTests.ResetThroughPipeAsync()).IsEqualTo("failed");
            if (releaseRequestFirst)
            {
                requestBarrier.Release.TrySetResult();
                using var updated = await request.WaitAsync(TimeSpan.FromSeconds(20));
                await Assert.That(updated.StatusCode).IsEqualTo(HttpStatusCode.OK);
            }
            else
            {
                dispatch.Release.TrySetResult();
                await worker.WaitAsync(TimeSpan.FromSeconds(20));
                await using var database = fixture.CreateDatabase();
                await Assert.That((await database.Set<OutboxMessage>().SingleAsync(row => row.Id == messageId)).Status)
                    .IsEqualTo(OutboxMessageStatus.Completed);
            }
            // One completed real unit cannot let purge overtake the other blocked unit.
            await Assert.That(coordinator.Drained.IsCompleted).IsFalse();
            await using (var database = fixture.CreateDatabase())
                await Assert.That(await database.Users.CountAsync()).IsGreaterThanOrEqualTo(6);
            requestBarrier.Release.TrySetResult();
            dispatch.Release.TrySetResult();
            using var response = await request.WaitAsync(TimeSpan.FromSeconds(20));
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await worker.WaitAsync(TimeSpan.FromSeconds(20));
            await Assert.That((await reset).StartsWith("ready ", StringComparison.Ordinal)).IsTrue();
            await fixture.AssertReadyAsync();
            await using var baseline = fixture.CreateDatabase();
            await Assert.That(await baseline.Users.CountAsync()).IsEqualTo(6);
            await Assert.That(await baseline.Set<OutboxMessage>().AnyAsync(row => row.Id == messageId)).IsFalse();
            await Assert.That((await baseline.Events.SingleAsync(row => row.Id == AgentBrowserPersonaCatalog.EventId)).Title)
                .IsEqualTo("Agent browser event");
        }
        finally
        {
            requestBarrier.Release.TrySetResult();
            dispatch.Release.TrySetResult();
            await Task.WhenAll(request, worker).WaitAsync(TimeSpan.FromSeconds(20));
            outbox.Dispose();
        }
    }

    [Test]
    public async Task QuartzJobAdmittedBeforeMaintenanceFinishesBeforePurge()
    {
        await using var fixture = await AgentBrowserPersonaFixture.CreateAsync(enableReset: true);
        await fixture.RunAsync();
        var coordinator = fixture.Services.GetRequiredService<AgentBrowserResetCoordinator>();
        var dispatch = new BlockingDispatcher { Services = fixture.Services };
        var scheduler = await new StdSchedulerFactory(new System.Collections.Specialized.NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = Guid.CreateVersion7().ToString("N"),
            ["quartz.threadPool.maxConcurrency"] = "2"
        }).GetScheduler();
        scheduler.ListenerManager.AddJobListener(new AgentBrowserResetJobListener(coordinator));
        var job = JobBuilder.Create<DatabaseWritingJob>().WithIdentity("reset-invariant").Build();
        job.JobDataMap["dispatch"] = dispatch;
        await scheduler.ScheduleJob(job, TriggerBuilder.Create().StartNow().Build());
        await scheduler.Start();
        try
        {
            await dispatch.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Task maintenance = coordinator.WaitForMaintenanceAsync();
            Task<string> reset = AgentDatabaseResetLifecycleTests.ResetThroughPipeAsync();
            await maintenance.WaitAsync(TimeSpan.FromSeconds(20));
            await Assert.That(coordinator.Drained.IsCompleted).IsFalse();
            dispatch.Release.TrySetResult();
            await Assert.That((await reset).StartsWith("ready ", StringComparison.Ordinal)).IsTrue();
            await fixture.AssertReadyAsync();
            await using var database = fixture.CreateDatabase();
            await Assert.That(await database.Users.CountAsync()).IsEqualTo(6);
        }
        finally
        {
            dispatch.Release.TrySetResult();
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }
    }

    [Test]
    public async Task DequeuedAiPointerCannotExecuteAfterResetButNewGenerationWorkStillRuns()
    {
        var commands = new DatabaseWritingAiCommand();
        await using var fixture = await AgentBrowserPersonaFixture.CreateAsync(enableReset: true);
        await fixture.RunAsync();
        commands.Services = fixture.Services;
        var coordinator = fixture.Services.GetRequiredService<AgentBrowserResetCoordinator>();
        var queue = new PausingAiQueue(new AiAssistantRunQueue(coordinator));
        // Exercise the real worker and channel with a database-writing command boundary; the API's
        // native protected-operation registration remains untouched and fully validated.
        var workerServices = new ServiceCollection();
        workerServices.AddHttpContextAccessor();
        workerServices.AddScoped<Explore.Application.Contracts.Services.ITenantContextAccessor, Explore.Infrastructure.Services.TenantContextAccessor>();
        workerServices.AddSingleton<ICommandHandler<ProcessAiRunCommand>>(commands);
        await using var workerProvider = workerServices.BuildServiceProvider();
        using var worker = new AiAssistantRunWorker(queue, workerProvider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AiAssistantRunWorker>.Instance, coordinator);
        Guid staleId = Guid.CreateVersion7();
        Guid freshId = Guid.CreateVersion7();
        commands.ExpectedFreshId = freshId;
        CancellationToken token = TestContext.Current!.Execution.CancellationToken;
        await queue.EnqueueAsync(new(AgentBrowserPersonaCatalog.TenantId, Guid.CreateVersion7(), staleId, "assistant"), token);
        await worker.StartAsync(token);
        try
        {
            // Pause AFTER the real channel removes the item, before the worker can acquire admission.
            await queue.Dequeued.Task.WaitAsync(TimeSpan.FromSeconds(20), token);
            await Assert.That((await AgentDatabaseResetLifecycleTests.ResetThroughPipeAsync()).StartsWith("ready ", StringComparison.Ordinal)).IsTrue();
            await queue.EnqueueAsync(new(AgentBrowserPersonaCatalog.TenantId, Guid.CreateVersion7(), freshId, "assistant"), token);
            queue.Release.TrySetResult();
            await commands.FreshCommitted.Task.WaitAsync(TimeSpan.FromSeconds(20), token);
            await using var database = fixture.CreateDatabase();
            await Assert.That(await database.Users.AnyAsync(row => row.Id == staleId, token)).IsFalse();
            await Assert.That(await database.Users.AnyAsync(row => row.Id == freshId, token)).IsTrue();
        }
        finally
        {
            queue.Release.TrySetResult();
            await worker.StopAsync(token);
        }
    }

    private sealed class PausingAiQueue(AiAssistantRunQueue inner) : IAiAssistantRunQueue
    {
        internal TaskCompletionSource Dequeued { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask EnqueueAsync(AiAssistantRunQueueItem item, CancellationToken cancellationToken) => inner.EnqueueAsync(item, cancellationToken);
        public async IAsyncEnumerable<AiAssistantRunQueueItem> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var item in inner.ReadAllAsync(cancellationToken))
            {
                if (Dequeued.TrySetResult()) await Release.Task.WaitAsync(TimeSpan.FromSeconds(40), cancellationToken);
                yield return item;
            }
        }
    }

    private sealed class DatabaseWritingAiCommand : ICommandHandler<ProcessAiRunCommand>
    {
        internal IServiceProvider Services { get; set; } = null!;
        internal Guid ExpectedFreshId { get; set; }
        internal TaskCompletionSource FreshCommitted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task ExecuteAsync(ProcessAiRunCommand request, CancellationToken cancellationToken)
        {
            await using var scope = Services.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            database.Users.Add(new User
            {
                Id = request.RunId,
                CreatedAt = DateTime.UtcNow,
                Pii = new UserPii
                {
                    Email = "queue@agent.example.test",
                    FirstName = "Queue",
                    LastName = "Invariant"
                }
            });
            await database.SaveChangesAsync(cancellationToken);
            if (request.RunId == ExpectedFreshId) FreshCommitted.TrySetResult();
        }
    }

    public sealed class DatabaseWritingJob : IJob
    {
        public Task Execute(IJobExecutionContext context) => ((BlockingDispatcher)context.MergedJobDataMap["dispatch"])
            .WriteAsync(context.CancellationToken);
    }

    public sealed class BlockingDispatcher : IOutboxMessageDispatcher
    {
        internal IServiceProvider Services { get; set; } = null!;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task DispatchAsync(OutboxMessage message, CancellationToken ct = default) => WriteAsync(ct);
        public Task ReconcileDeadLetterAsync(OutboxMessage message, CancellationToken ct = default) =>
            throw new InvalidOperationException("unexpected_dead_letter");
        internal async Task WriteAsync(CancellationToken token)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(40), token);
            await using var scope = Services.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            database.Users.Add(new User
            {
                Id = Guid.CreateVersion7(),
                CreatedAt = DateTime.UtcNow,
                Pii = new UserPii
                {
                    Email = "worker@agent.example.test",
                    FirstName = "Concurrent",
                    LastName = "Worker"
                }
            });
            await database.SaveChangesAsync(token);
        }
    }

    private sealed class RequestBarrier : DbCommandInterceptor
    {
        internal bool Armed { get; set; }
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (Armed && command.CommandText.Contains("UPDATE islamu_event.events", StringComparison.Ordinal))
            {
                Armed = false;
                Entered.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(40), cancellationToken);
            }
            return result;
        }
    }
}
