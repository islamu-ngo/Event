using System.Collections.Concurrent;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.BackgroundServices;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Secrets.Database;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class NativeBackgroundWorkerDiagnosticTests
{
    [Test]
    [Arguments(1)]
    [Arguments(3)]
    public async Task UnknownOutboxContentFailsAdmissionWithoutDiagnosticIdentity(int maxRetries)
    {
        await using var factory = await WorkerFactory.CreateAsync();
        var message = new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            AggregateId = Guid.CreateVersion7(),
            AggregateType = "private-aggregate-canary",
            EventType = "private-event-type-canary",
            Payload = "private-payload-canary",
            Status = OutboxMessageStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            MaxRetries = maxRetries
        };
        using (var scope = factory.Services.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>()
                .ExecuteInTransactionAsync(async _ => { await repository.Create(message); });
        }

        await factory.Outbox.ProcessOutboxBatchAsync(CancellationToken.None);

        using var read = factory.Services.CreateScope();
        var persisted = await read.ServiceProvider.GetRequiredService<ExploreDbContext>()
            .OutboxMessages.AsNoTracking().SingleAsync(row => row.Id == message.Id);
        await Assert.That(persisted.Status).IsEqualTo(OutboxMessageStatus.Failed);
        await Assert.That(persisted.RetryCount).IsEqualTo(0);
        await Assert.That(persisted.LastError).IsEqualTo("generic_source_not_admitted");
        await Assert.That(persisted.NextRetryAt).IsNotNull();
        await Assert.That(persisted.Payload).IsEqualTo(message.Payload);
        // Rejection is a persisted fixed code; admission does not export the rejected envelope.
        string[] privateValues = [message.Id.ToString(), message.AggregateId.ToString(),
            message.AggregateType, message.EventType, message.Payload!];
        await Assert.That(factory.Logs.Entries.Where(entry => entry.Category == typeof(OutboxProcessor).FullName).Any(entry =>
            entry.Exception is not null || privateValues.Any(value =>
                entry.Rendered.Contains(value, StringComparison.Ordinal)
                || entry.Fields.Values.Any(field => field?.ToString()?.Contains(value, StringComparison.Ordinal) == true))))
            .IsFalse();
    }

    [Test]
    public async Task NativeCacheCorrectionCompletesWithoutExportingSubjectIdentity()
    {
        await using var factory = await WorkerFactory.CreateAsync();
        var message = PrivacyErasureCacheInvalidationOutboxMessageFactory.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), DateTime.UtcNow);
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IOutboxRepository>().Create(message);

        await factory.Outbox.ProcessOutboxBatchAsync(CancellationToken.None);

        // The generic outbox has no public completed-row query. Read the committed ledger
        // in an independent context, not the processor's tracked input.
        using var read = factory.Services.CreateScope();
        var row = await read.ServiceProvider.GetRequiredService<ExploreDbContext>()
            .OutboxMessages.AsNoTracking().SingleAsync(item => item.Id == message.Id);
        await Assert.That(row.Status).IsEqualTo(OutboxMessageStatus.Completed);
        await Assert.That(row.ProcessedAt).IsNotNull();
        await Assert.That(row.NextRetryAt).IsNull();
        await Assert.That(row.RetryCount).IsEqualTo(0);
        await factory.Logs.AssertSafeAsync(typeof(OutboxProcessor),
            message.Id.ToString(), message.AggregateId.ToString(), message.EventType);
    }

    [Test]
    public async Task NativeAssistantCacheFailureIsContainedWithoutRunOrExceptionExport()
    {
        await using var factory = await WorkerFactory.CreateAsync();
        var runId = Guid.CreateVersion7();
        var conversationId = Guid.CreateVersion7();
        var failure = factory.Logs.WaitForFailure(typeof(AiAssistantRunWorker), "run_processing_failed");
        factory.Cache.FailRead = true;

        await factory.Services.GetRequiredService<IAiAssistantRunQueue>().EnqueueAsync(
            new(PlatformDefaults.DefaultTenantId, conversationId, runId, "assistant"),
            CancellationToken.None);
        await failure.WaitAsync(TimeSpan.FromSeconds(20));

        factory.Cache.FailRead = false;
        await factory.Logs.AssertSafeAsync(typeof(AiAssistantRunWorker),
            runId.ToString(), conversationId.ToString(), PlatformDefaults.DefaultTenantId.ToString(),
            FaultingMemoryCache.PrivateFailure);
        var worker = factory.Services.GetServices<IHostedService>().OfType<AiAssistantRunWorker>().Single();
        await Assert.That(worker.ExecuteTask!.IsCompleted).IsFalse();
    }

    [Test]
    public async Task NativeBootstrapCacheFailureRollsBackSeedWithoutExceptionExport()
    {
        await using var factory = await WorkerFactory.CreateAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            // Remove only this fixture's seed so the real bootstrap has a missing default to fill.
            await database.SystemSettings.Where(row =>
                row.SettingKey == GovernanceSettingKeys.AiAssistant.Enabled).ExecuteDeleteAsync();
        }
        factory.Cache.FailRemove = true;
        var options = factory.Services.GetRequiredService<IOptions<AiProviderSettings>>().Value;
        options.Enabled = true;
        options.Provider = AiProviderSettings.ProviderFake;
        var worker = factory.Services.GetRequiredService<AiProviderSettingsBootstrapWorker>();

        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(20));

        factory.Cache.FailRemove = false;
        using var read = factory.Services.CreateScope();
        await Assert.That(await read.ServiceProvider.GetRequiredService<ISystemSettingRepository>()
            .GetByKey(GovernanceSettingKeys.AiAssistant.Enabled)).IsNull();
        await factory.Logs.AssertSafeAsync(typeof(AiProviderSettingsBootstrapWorker),
            FaultingMemoryCache.PrivateFailure);
    }

    [Test]
    [Arguments("local", AuthorizationProviderBootstrapState.Ready)]
    [Arguments("cerbos", AuthorizationProviderBootstrapState.Failed)]
    public async Task NativeProviderBootstrapKeepsReadinessAndOmitsBusinessMessage(string provider, string status)
    {
        await using var factory = await WorkerFactory.CreateAsync();
        factory.Services.GetRequiredService<IOptions<AuthorizationProviderDeploymentOptions>>().Value.Provider = provider;
        var runner = factory.Services.GetRequiredService<CerbosPolicyBootSyncRunner>();

        await runner.RunOnceAsync(CancellationToken.None);

        var snapshot = factory.Services.GetRequiredService<AuthorizationProviderBootstrapState>().Read();
        await Assert.That(snapshot.Status).IsEqualTo(status);
        await Assert.That(snapshot.Message).IsNotNull();
        await factory.Logs.AssertSafeAsync(typeof(CerbosPolicyBootSyncRunner), snapshot.Message!);
        if (provider == "cerbos")
            await Assert.That(factory.Logs.Entries.Any(entry =>
                entry.Fields.TryGetValue("FailureCategory", out var category)
                && Equals(category, "reconciliation_incomplete"))).IsTrue();
    }

    private sealed class WorkerFactory : AuthenticatedWebApplicationFactory
    {
        private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"worker-diagnostics-{Guid.CreateVersion7():N}.db");
        internal WorkerLogs Logs { get; } = new();
        internal FaultingMemoryCache Cache { get; } = new();
        protected override bool UsesPostgreSqlContainer => false;
        internal OutboxProcessor Outbox => Services.GetServices<IHostedService>().OfType<OutboxProcessor>().Single();

        internal static async Task<WorkerFactory> CreateAsync()
        {
            var factory = new WorkerFactory();
            factory.AdditionalConfiguration["Authorization:Provider"] = "local";
            factory.AdditionalConfiguration["Cerbos:GrpcEndpoint"] = "";
            factory.AdditionalConfiguration["OutboxProcessor:Enabled"] = "false";
            factory.AdditionalConfiguration["OutboxProcessor:VerboseLogging"] = "true";
            factory.AdditionalConfiguration["EmailDispatchProcessor:Enabled"] = "false";
            factory.AdditionalConfiguration["EmailDispatchRabbitMq:Enabled"] = "false";
            factory.AdditionalConfiguration["AiProvider:Enabled"] = "false";
            try
            {
                var options = new DbContextOptionsBuilder<ExploreDbContext>();
                factory.ConfigureDatabase(options);
                await using var database = new ExploreDbContext(options.Options);
                await database.Database.EnsureCreatedAsync();
                await SqliteDatabaseInitializer.InitializeAsync(database, default);
                _ = factory.Services;
                return factory;
            }
            catch
            {
                await factory.DisposeAsync();
                throw;
            }
        }

        private void ConfigureDatabase(DbContextOptionsBuilder options)
        {
            PrimaryDatabaseProviderComposition.ConfigureApplication(options, new PrimaryDatabaseConnectionOptions
            {
                Role = PrimaryDatabaseRole.Runtime,
                Provider = PrimaryDatabaseProvider.Sqlite,
                Database = databasePath
            });
            options.UseSnakeCaseNamingConvention();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveExploreDbContextRegistrations();
                services.AddDbContextFactory<ExploreDbContext>(ConfigureDatabase);
                services.RemoveAll<INotificationOutboxAdmissionDbContextFactory>();
                services.AddSingleton<INotificationOutboxAdmissionDbContextFactory, WorkerAdmissionFactory>();
                services.AddScoped(serviceProvider =>
                {
                    var database = serviceProvider.GetRequiredService<IDbContextFactory<ExploreDbContext>>().CreateDbContext();
                    database.TenantContext = serviceProvider.GetRequiredService<ITenantContext>();
                    database.CurrentUserService = serviceProvider.GetRequiredService<ICurrentUserService>();
                    return database;
                });
                GenericOutboxWorkerTestServices.Configure(services);
                // Cache storage is the fault seam; handlers, repositories, authority and UoW stay native.
                services.RemoveAll<IMemoryCache>();
                services.AddSingleton<IMemoryCache>(Cache);
                services.AddLogging(logging =>
                {
                    logging.AddProvider(Logs);
                    logging.AddFilter<WorkerLogs>("Explore.API.BackgroundServices", LogLevel.Debug);
                });
                // Testing omits these boot-only registrations; use the production concrete types.
                services.AddSingleton<AiProviderSettingsBootstrapWorker>();
                services.AddSingleton<CerbosPolicyBootSyncRunner>();
                services.PostConfigure<CerbosPolicyBootSyncOptions>(options =>
                {
                    options.MaxAttempts = 2;
                    options.RetryDelaySeconds = 0;
                });
            });
        }

        public override async ValueTask DisposeAsync()
        {
            Cache.FailRead = false;
            Cache.FailRemove = false;
            await base.DisposeAsync();
            Cache.Dispose();
            File.Delete(databasePath);
            File.Delete(databasePath + "-wal");
            File.Delete(databasePath + "-shm");
        }
    }

    private sealed class WorkerAdmissionFactory(IDbContextFactory<ExploreDbContext> factory)
        : INotificationOutboxAdmissionDbContextFactory
    {
        public Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            factory.CreateDbContextAsync(cancellationToken);
    }

    private sealed class FaultingMemoryCache : IMemoryCache
    {
        internal const string PrivateFailure = "private-cache-body https://private-provider.example.test/private-run";
        private readonly MemoryCache inner = new(new MemoryCacheOptions());
        internal volatile bool FailRead;
        internal volatile bool FailRemove;
        public ICacheEntry CreateEntry(object key) => inner.CreateEntry(key);
        public bool TryGetValue(object key, out object? value)
        {
            if (FailRead && Equals(key, "HierSettings:System"))
                throw new InvalidOperationException(PrivateFailure);
            return inner.TryGetValue(key, out value);
        }
        public void Remove(object key)
        {
            if (FailRemove && Equals(key, "HierSettings:System"))
                throw new InvalidOperationException(PrivateFailure);
            inner.Remove(key);
        }
        public void Dispose() => inner.Dispose();
    }

    private sealed record WorkerLog(string Category, Exception? Exception, string Rendered,
        IReadOnlyDictionary<string, object?> Fields);

    private sealed class WorkerLogs : ILoggerProvider
    {
        internal ConcurrentQueue<WorkerLog> Entries { get; } = new();
        private readonly ConcurrentDictionary<string, TaskCompletionSource> failures = new();
        internal Task WaitForFailure(Type worker, string failureCategory) =>
            failures.GetOrAdd($"{worker.FullName}/{failureCategory}",
                _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
        public ILogger CreateLogger(string categoryName) => new Collector(this, categoryName);
        public void Dispose() { }

        internal async Task AssertSafeAsync(Type worker, params string[] privateValues)
        {
            var entries = Entries.Where(entry => entry.Category == worker.FullName).ToArray();
            await Assert.That(entries.Length).IsGreaterThan(0);
            string[] allowed = ["Count", "FailureCategory", "Retry", "Max", "Retries", "Delay",
                "Attempt", "MaxAttempts", "TimeoutSeconds", "Interval", "BatchSize"];
            foreach (var entry in entries)
            {
                await Assert.That(entry.Exception).IsNull();
                await Assert.That(entry.Fields.Keys.Except(allowed, StringComparer.Ordinal).Any()).IsFalse();
                var exported = entry.Rendered + string.Join(" ", entry.Fields.Select(pair => $"{pair.Key}={pair.Value}"));
                foreach (var value in privateValues)
                    await Assert.That(exported.Contains(value, StringComparison.Ordinal)).IsFalse();
            }
        }

        private sealed class Collector(WorkerLogs owner, string category) : ILogger
        {
            public bool IsEnabled(LogLevel logLevel) => true;
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (!category.StartsWith("Explore.API.BackgroundServices.", StringComparison.Ordinal)) return;
                var fields = state is IEnumerable<KeyValuePair<string, object?>> values
                    ? values.Where(pair => pair.Key != "{OriginalFormat}").ToDictionary(pair => pair.Key, pair => pair.Value)
                    : new Dictionary<string, object?>();
                owner.Entries.Enqueue(new(category, exception, formatter(state, exception), fields));
                if (logLevel >= LogLevel.Warning
                    && fields.TryGetValue("FailureCategory", out var failureCategory)
                    && owner.failures.TryGetValue($"{category}/{failureCategory}", out var signal))
                    signal.TrySetResult();
            }
        }
    }
}
