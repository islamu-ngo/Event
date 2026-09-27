using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Settings;
using Explore.Persistence;
using Explore.Persistence.Schema;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using Quartz;
using Quartz.Listener;

namespace Explore.API.Hosting;

/// <summary>
/// Single API-process owner of the development database. A current-OS-user-only named pipe is the
/// control surface, not an HTTP endpoint. Closing admission and taking a work lease are atomic under
/// the same monitor; draining covers whole request/worker units, not merely open EF transactions.
/// Failed resets retain maintenance. After purge commits, retries resume native receipts, never purge
/// them again. Process restart goes through the ordinary pre-traffic native startup recovery.
/// </summary>
public sealed class AgentBrowserResetCoordinator(
    IServiceProvider services,
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<AgentBrowserResetCoordinator> logger) : BackgroundService, IAgentBrowserWorkAdmission, IHealthCheck
{
    private readonly object _sync = new();
    private TaskCompletionSource _opened = Signal();
    private TaskCompletionSource _closed = Signal();
    private TaskCompletionSource _drained = CompletedSignal();
    private CancellationTokenSource _maintenanceRequested = new();
    private bool _maintenance = true;
    private bool _purged;
    private int _active;
    private int _resetting;
    private int _disposed;
    private long _generation;
    private NpgsqlConnection? _owner;

    public static string PipeName(int ownerProcessId) => $"islamu-agent-database-reset-{ownerProcessId}";
    private string ControlPipeName => configuration["AgentBrowser:ResetPipeName"] is { Length: > 0 } configured
        ? configured : PipeName(Environment.ProcessId);
    public long Generation => Volatile.Read(ref _generation);
    public CancellationToken MaintenanceRequested { get { lock (_sync) return _maintenanceRequested.Token; } }
    public Task Drained { get { lock (_sync) return _drained.Task; } }
    public Task WaitForMaintenanceAsync() { lock (_sync) return _maintenance ? Task.CompletedTask : _closed.Task; }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        RequireAdmission();
        if (_owner is not null) throw new InvalidOperationException("agent_browser_owner_already_initialized");
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        if (!database.Database.IsNpgsql()) throw new InvalidOperationException("agent_browser_database_provider");
        _owner = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(database.Database.GetConnectionString())
        {
            Pooling = false
        }.ConnectionString);
        var owner = _owner;
        try
        {
            await owner.OpenAsync(cancellationToken);
            if (owner.Database != "islamu_event_agent") throw new InvalidOperationException("agent_browser_database_name");
            await using var claim = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", owner);
            claim.Parameters.AddWithValue("key", AgentBrowserPersonaStartup.AdvisoryLockKey + 1);
            if (!(bool)(await claim.ExecuteScalarAsync(cancellationToken))!)
                throw new InvalidOperationException("agent_browser_owner_contended");
            owner.StateChange += (_, change) =>
            {
                if (change.CurrentState != System.Data.ConnectionState.Open) CloseAdmission();
            };
            await RequireLocalStorageAsync(cancellationToken);
            await AgentBrowserPersonaStartup.WithProvisioningLockAsync(services,
                token => AgentBrowserPersonaStartup.ProvisionAsync(services, configuration, token), cancellationToken);
            OpenAdmission();
        }
        catch
        {
            CloseAdmission();
            _owner = null;
            await owner.DisposeAsync();
            throw;
        }
    }

    public async Task<TimeSpan> ResetAsync(CancellationToken cancellationToken)
    {
        RequireAdmission();
        if (_owner is null || _owner.State != System.Data.ConnectionState.Open)
            throw new InvalidOperationException("agent_browser_owner_unavailable");
        if (Interlocked.CompareExchange(ref _resetting, 1, 0) != 0)
            throw new InvalidOperationException("agent_browser_reset_contended");
        long started = Stopwatch.GetTimestamp();
        CloseAdmission();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(120));
        try
        {
            await Drained.WaitAsync(deadline.Token);
            await RequireLocalStorageAsync(deadline.Token);
            await AgentBrowserPersonaStartup.WithProvisioningLockAsync(services, async token =>
            {
                if (!_purged)
                {
                    await AgentBrowserPersonaStartup.ValidateResetOwnershipAsync(services, configuration, token);
                    await using var scope = services.CreateAsyncScope();
                    await AgentBrowserDatabaseReset.PurgeAsync(scope.ServiceProvider.GetRequiredService<ExploreDbContext>(), token);
                    _purged = true;
                    // Every admitted producer has drained. Items queued before/during the drain now
                    // refer to the old database, even if a consumer already removed one from its channel.
                    Interlocked.Increment(ref _generation);
                }
                // Invalidate database projections, not Redis sessions or unrelated storage. HybridCache's
                // wildcard is a logical cache-generation invalidation, not FLUSHDB or key enumeration.
                await services.GetRequiredService<HybridCache>().RemoveByTagAsync("*", token);
                if (services.GetRequiredService<IMemoryCache>() is not MemoryCache memory)
                    throw new InvalidOperationException("agent_browser_reset_memory_cache_unsupported");
                memory.Clear();
                await AgentBrowserPersonaStartup.ProvisionAsync(services, configuration, token);
                await AgentBrowserPersonaStartup.VerifyResetBaselineAsync(services, token);
                await RequireLocalStorageAsync(token);
                var routes = services.GetRequiredService<ITenantSlugCache>();
                await routes.RefreshAsync(token);
                if (await routes.GetTenantIdByDomainAsync("default", token) != Explore.Persistence.Seed.AgentBrowserPersonaCatalog.TenantId
                    || await routes.GetTenantIdByDomainAsync("agent-negative", token) != Explore.Persistence.Seed.AgentBrowserPersonaCatalog.NegativeTenantId)
                    throw new InvalidOperationException("agent_browser_reset_routing_incomplete");
                await services.GetRequiredService<IOutputCacheStore>().EvictByTagAsync("agent-database", token);
            }, deadline.Token);
            _purged = false;
            OpenAdmission();
            return Stopwatch.GetElapsedTime(started);
        }
        finally
        {
            // No finally-open: timeout, contention, lost response and failed native transitions fail closed.
            Volatile.Write(ref _resetting, 0);
        }
    }

    public IDisposable? TryEnter()
    {
        lock (_sync)
        {
            if (_maintenance) return null;
            if (_active++ == 0) _drained = Signal();
            return new WorkLease(this);
        }
    }

    public async ValueTask<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            Task open;
            lock (_sync)
            {
                if (!_maintenance)
                {
                    if (_active++ == 0) _drained = Signal();
                    return new WorkLease(this);
                }
                open = _opened.Task;
            }
            await open.WaitAsync(cancellationToken);
        }
    }

    public async Task HandleHttpAsync(HttpContext context, RequestDelegate next)
    {
        using var work = TryEnter();
        if (work is null)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.Headers.CacheControl = "no-store";
            return;
        }
        await next(context);
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        lock (_sync) return Task.FromResult(_maintenance
            ? HealthCheckResult.Unhealthy("agent_database_maintenance") : HealthCheckResult.Healthy());
    }

    private void CloseAdmission()
    {
        CancellationTokenSource requested;
        lock (_sync)
        {
            if (!_maintenance) _opened = Signal();
            _maintenance = true;
            _closed.TrySetResult();
            requested = _maintenanceRequested;
        }
        requested.Cancel();
    }

    private void OpenAdmission()
    {
        lock (_sync)
        {
            _maintenanceRequested.Dispose();
            _maintenanceRequested = new();
            _maintenance = false;
            _closed = Signal();
            _opened.TrySetResult();
        }
    }

    private void Exit()
    {
        lock (_sync)
        {
            if (--_active == 0) _drained.TrySetResult();
        }
    }

    private void RequireAdmission()
    {
        if (!ExploreDatabaseMigrator.EnsureAgentBrowserAdmission(configuration, environment))
            throw new InvalidOperationException("agent_browser_reset_disabled");
        // This profile omits RabbitMQ. Do not admit a topology with ungated external consumers.
        if (configuration.GetValue<bool>("EmailDispatchRabbitMq:Enabled"))
            throw new InvalidOperationException("agent_browser_reset_external_consumer");
    }

    private async Task RequireLocalStorageAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<IHierarchicalSettingsResolver>();
        var policies = scope.ServiceProvider.GetRequiredService<IStoragePolicyResolver>();
        settings.InvalidateCache();
        var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var tenantIds = await database.Tenants.IgnoreQueryFilters().AsNoTracking()
            .Select(tenant => tenant.Id).ToListAsync(cancellationToken);
        foreach (Guid? tenantId in tenantIds.Select(static id => (Guid?)id).Prepend(null))
        {
            if (tenantId is { } id) settings.InvalidateCache(SettingScope.Tenant, id);
            var policy = await policies.ResolveAsync(tenantId, cancellationToken);
            if (!string.Equals(policy.Provider, StorageProviders.Local, StringComparison.OrdinalIgnoreCase)
                || policy.Routes.Any(route => !string.Equals(
                    route.Provider, StorageProviders.Local, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("agent_browser_storage_provider_unsupported");
        }
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        RequireAdmission();
        string ownerPipe = PipeName(Environment.ProcessId);
        return ControlPipeName == ownerPipe
            ? ListenAsync(ownerPipe, stoppingToken)
            : Task.WhenAll(ListenAsync(ownerPipe, stoppingToken), ListenAsync(ControlPipeName, stoppingToken));
    }

    private async Task ListenAsync(string pipeName, CancellationToken stoppingToken)
    {
        var clients = new List<Task>();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var connected = Signal();
                Task client = HandleControlAsync(pipeName, connected, stoppingToken);
                clients.RemoveAll(task => task.IsCompletedSuccessfully);
                clients.Add(client);
                await Task.WhenAny(connected.Task, client);
                if (!connected.Task.IsCompletedSuccessfully) await client;
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug("Agent database reset control listener stopped on host cancellation.");
        }
        finally { await Task.WhenAll(clients); }
    }

    private async Task HandleControlAsync(string pipeName, TaskCompletionSource connected, CancellationToken stoppingToken)
    {
        await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.WaitForConnectionAsync(stoppingToken);
        connected.SetResult();
        try
        {
            using var requestDeadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            requestDeadline.CancelAfter(TimeSpan.FromSeconds(5));
            byte[] request = new byte[6];
            await pipe.ReadExactlyAsync(request, requestDeadline.Token);
            if (!request.AsSpan().SequenceEqual("reset\n"u8)) return;
            string response;
            try
            {
                TimeSpan elapsed = await ResetAsync(stoppingToken);
                response = FormattableString.Invariant($"ready {elapsed.TotalMilliseconds:F0}ms target=2000ms\n");
                logger.LogInformation("Agent database reset ready in {ElapsedMilliseconds}ms; target 2000ms", elapsed.TotalMilliseconds);
            }
            catch (Exception exception)
            {
                // Every reset failure leaves admission closed and must return a sanitized failure to the control caller.
                // Native/provider exception messages can contain secrets or SQL values.
                string reason = exception is InvalidOperationException && exception.Message.StartsWith("agent_browser_", StringComparison.Ordinal)
                    ? exception.Message : exception is PostgresException postgres ? postgres.SqlState : exception.GetType().Name;
                logger.LogWarning("Agent database reset failed closed. FailureCode={FailureCode}", reason);
                response = "failed\n";
            }
            await pipe.WriteAsync(Encoding.UTF8.GetBytes(response), stoppingToken);
            await pipe.FlushAsync(stoppingToken);
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException)
        {
            logger.LogDebug("Agent database reset control disconnected. FailureType={FailureType}", exception.GetType().Name);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        CloseAdmission();
        await base.StopAsync(cancellationToken);
        if (_owner is not null)
        {
            await _owner.DisposeAsync();
            _owner = null;
        }
    }

    public override void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        CloseAdmission();
        _owner?.Dispose();
        _maintenanceRequested.Dispose();
        base.Dispose();
    }

    private sealed class WorkLease(AgentBrowserResetCoordinator owner) : IDisposable
    {
        private AgentBrowserResetCoordinator? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Exit();
    }
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static TaskCompletionSource CompletedSignal() { var signal = Signal(); signal.SetResult(); return signal; }
}

/// <summary>Quartz admits the whole execution before any job code and releases only after completion.</summary>
public sealed class AgentBrowserResetJobListener(AgentBrowserResetCoordinator coordinator) : JobListenerSupport
{
    private const string LeaseKey = "agent-database-work-lease";
    public override string Name => "agent-database-maintenance";
    public override async Task JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default) =>
        context.Put(LeaseKey, await coordinator.EnterAsync(cancellationToken));
    public override Task JobWasExecuted(IJobExecutionContext context, JobExecutionException? jobException, CancellationToken cancellationToken = default)
    {
        (context.Get(LeaseKey) as IDisposable)?.Dispose();
        return Task.CompletedTask;
    }
}
