using Explore.Persistence.Database.ProviderPrimitives;
using System.Data.Common;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Services.Registration;
using Explore.Application.Services.Registration;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Registration;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using DomainEvent = Explore.Domain.Event;

namespace Event.Persistence.IntegrationTests.Fixtures;

/// <summary>Subscription-owned harness; all business reads/writes use real repositories and ordinary tenant credentials.</summary>
public sealed class SubscriptionClaimNativeFixture : IAsyncDisposable
{
    private static readonly DateTime Now = new(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc);
    private readonly PostgreSqlContainerFixture _database;
    private readonly BindingBarrierInterceptor _lookup;
    private readonly ServiceProvider _services;
    private readonly List<(Guid TenantId, Guid BindingId)> _rows = [];
    private readonly string _runtimeTables;
    private readonly string _runtimeLogin;
    public MutableClock Clock { get; } = new(Now);
    public RecordingAdapter Adapter { get; }
    public List<Guid> ResolvedTenantLeases { get; } = [];
    public GlobalExploreDbContextFactory Global { get; }
    public TenantExploreDbContextFactory Runtime { get; }

    private SubscriptionClaimNativeFixture(PostgreSqlContainerFixture database, string tables, string bindingTable, string stateTable, string messageTable)
    {
        _database = database;
        _runtimeTables = tables;
        var options = database.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime);
        _runtimeLogin = options.Username!;
        Global = new(database.GetNativeQueueOptions(PrimaryDatabaseRole.Global), poolSize: 1);
        Runtime = new(options, poolSize: 2);
        _lookup = new(bindingTable, stateTable, messageTable);
        Adapter = new(Clock);
        var services = new ServiceCollection();
        services.AddMetrics();
        services.AddSingleton<BusinessMetrics>();
        services.AddHttpContextAccessor();
        services.AddScoped<ITenantContextAccessor, TenantContextAccessor>();
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton<IGlobalExploreDbContextFactory>(Global);
        services.AddSingleton<ITenantExploreDbContextFactory>(new BarrierTenantFactory(options, Runtime, _lookup));
        services.AddRegistrationProviderSubscriptionWorker();
        services.AddScoped(provider => GenericOutboxExecutionScopeFactory.GetSelectedRuntimeContext(provider)
            ?? throw new InvalidOperationException("Subscription dependencies resolved before selecting Runtime."));
        services.AddScoped<IRegistrationProviderRepository, RegistrationProviderRepository>();
        services.AddScoped<IIncomingWebhookMessageRepository, IncomingWebhookMessageRepository>();
        services.AddScoped<IIncomingWebhookEffectOutboxRepository, IncomingWebhookEffectOutboxRepository>();
        services.AddScoped<IUnitOfWork, EfCoreUnitOfWork>();
        services.AddScoped<IRegistrationProviderDescriptor>(provider =>
        {
            var context = provider.GetRequiredService<ExploreDbContext>();
            Guid tenant = context.BindTenantLease()
                ?? throw new InvalidOperationException("Adapter resolved without a tenant lease.");
            if (context.IsTenantFilterBypassed ||
                provider.GetRequiredService<ITenantContextAccessor>().TenantId != tenant)
                throw new InvalidOperationException("Adapter lease and execution tenant disagree.");
            ResolvedTenantLeases.Add(tenant);
            return Adapter;
        });
        services.AddScoped<IRegistrationProviderRegistry, RegistrationProviderRegistry>();
        services.AddSingleton<IRegistrationProviderCallbackUriBuilder, CallbackUri>();
        services.AddSingleton<IRegistrationProviderCallbackReceiptProtector, ReceiptProtector>();
        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    public static async Task<SubscriptionClaimNativeFixture> CreateAsync(
        PostgreSqlContainerFixture database, RegistrationProviderSubscriptionOperation operation, int tenantCount = 1)
    {
        await database.ResetAsync();
        await using var administrator = database.CreateDbContext();
        await PostgresRegistrationProviderSubscriptionClaimContract.ApplyAsync(administrator, database.NativeQueueEnrollment);
        await PostgresRegistrationProviderSubscriptionClaimContract.ApplyAsync(administrator, database.NativeQueueEnrollment);
        Type[] runtimeTypes =
        [
            typeof(RegistrationProviderSubscriptionState), typeof(RegistrationProviderBinding),
            typeof(RegistrationProviderConnection), typeof(RegistrationProviderApprovedOrigin),
            typeof(RegistrationProviderFieldMapping), typeof(RegistrationProviderOptionMapping),
            typeof(RegistrationProviderCapability), typeof(RegistrationForm), typeof(RegistrationFormVersion),
            typeof(IncomingWebhookMessage), typeof(IncomingWebhookEffectOutbox)
        ];
        string tables = string.Join(", ", runtimeTypes.Select(type => Table(administrator, type)));
        string schema = administrator.Model.FindEntityType(typeof(RegistrationProviderSubscriptionState))!.GetSchema()!;
        string runtime = database.NativeQueueEnrollment.RuntimeLogin;
        await administrator.Database.ExecuteSqlRawAsync($"""
            GRANT USAGE ON SCHEMA "{schema}" TO "{runtime}";
            GRANT SELECT, INSERT, UPDATE, DELETE ON {tables} TO "{runtime}";
            """);
        var fixture = new SubscriptionClaimNativeFixture(database, tables,
            Table(administrator, typeof(RegistrationProviderBinding)),
            Table(administrator, typeof(RegistrationProviderSubscriptionState)),
            Table(administrator, typeof(IncomingWebhookMessage)));
        try
        {
            await fixture.SeedAsync(administrator, operation, tenantCount);
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    public Task<IReadOnlyList<RegistrationProviderSubscriptionClaim>> ClaimAsync(RegistrationProviderSubscriptionOperation operation) =>
        new RegistrationProviderSubscriptionClaimQueue(Global).ClaimAsync(
            operation, 10, Clock.GetUtcNow().UtcDateTime, TimeSpan.FromMinutes(2), CancellationToken.None);

    public async Task<bool> ProcessAsync(RegistrationProviderSubscriptionClaim claim, RegistrationProviderSubscriptionOperation operation)
    {
        await using var caller = _services.CreateAsyncScope();
        await using var scope = await caller.ServiceProvider.GetRequiredService<IRegistrationProviderSubscriptionExecutionScopeFactory>()
            .CreateAsync(claim.TenantId, CancellationToken.None);
        return await scope.ProcessAsync(claim, operation, CancellationToken.None);
    }

    public async Task<int> DrainAsync(bool boundCaller)
    {
        await using var scope = _services.CreateAsyncScope();
        // A previously selected caller must not become either worker execution tenant.
        if (boundCaller)
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(_rows[0].TenantId);
        return await scope.ServiceProvider.GetRequiredService<RegistrationProviderSubscriptionDrainService>()
            .DrainOnceAsync(CancellationToken.None);
    }

    public EventBarrier SuspendBindingLookup() => _lookup.Arm();
    public EventBarrier SuspendResponseStaging() => _lookup.Arm(staging: true);

    public async Task<RegistrationProviderSubscriptionState> ReadAsync(RegistrationProviderSubscriptionClaim claim)
    {
        await using var context = await Runtime.CreateDbContextAsync(claim.TenantId);
        return (await new RegistrationProviderSubscriptionStateRepository(context, Clock).GetAsync(
            claim.TenantId, claim.RegistrationProviderBindingId, "native.responses", CancellationToken.None))!;
    }

    public async Task RequireNoStagingAsync(Guid tenantId)
    {
        await using var context = await Runtime.CreateDbContextAsync(tenantId);
        foreach (var row in _rows.Where(row => row.TenantId == tenantId))
        {
            string identity = $"{row.BindingId:N}:response-1";
            await Assert.That(await new IncomingWebhookMessageRepository(context).GetByProviderMessageIdForUpdateAsync(
                tenantId, "registration-provider", identity, CancellationToken.None)).IsNull();
            await Assert.That(await new IncomingWebhookEffectOutboxRepository(context).GetByProviderIdentityAsync(
                tenantId, "registration-provider", identity, "registration.provider_submission", CancellationToken.None)).IsNull();
        }
    }

    public async Task RequireSuccessfulSweepsAsync()
    {
        foreach (var row in _rows)
        {
            await using var context = await Runtime.CreateDbContextAsync(row.TenantId);
            var state = await new RegistrationProviderSubscriptionStateRepository(context, Clock)
                .GetAsync(row.TenantId, row.BindingId, "native.responses", CancellationToken.None);
            await Assert.That(state!.LastSweepSuccessAt).IsEqualTo(Now);
            await Assert.That(state.ResponseCheckpoint).IsEqualTo("native-next");
            await Assert.That(state.LeaseToken).IsNull();
            await Assert.That(await context.RegistrationProviderSubscriptionStates.IgnoreQueryFilters()
                .CountAsync(state => state.TenantId != row.TenantId)).IsEqualTo(0);
            string identity = $"{row.BindingId:N}:response-1";
            await Assert.That(await new IncomingWebhookEffectOutboxRepository(context).GetByProviderIdentityAsync(
                row.TenantId, "registration-provider", identity, "registration.provider_submission", CancellationToken.None)).IsNotNull();
        }
    }

    public async Task RequireGlobalPrivateDenialsAsync()
    {
        await using var global = await Global.CreateDbContextAsync();
        foreach (Type type in new[] { typeof(RegistrationProviderSubscriptionState), typeof(RegistrationProviderBinding),
            typeof(RegistrationProviderConnection), typeof(IncomingWebhookMessage), typeof(UserPii) })
        {
            var failure = await Assert.ThrowsAsync<PostgresException>(async () =>
                await global.Database.ExecuteSqlRawAsync($"SELECT * FROM {Table(global, type)}"));
            await Assert.That(failure!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
            failure = await Assert.ThrowsAsync<PostgresException>(async () =>
                await global.Database.ExecuteSqlRawAsync($"DELETE FROM {Table(global, type)} WHERE false"));
            await Assert.That(failure!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        }
    }

    private async Task SeedAsync(ExploreDbContext context, RegistrationProviderSubscriptionOperation operation, int tenantCount)
    {
        var user = new User { Pii = new UserPii { Email = "subscription@example.test", FirstName = "Subscription", LastName = "Custody" } };
        context.Add(user);
        await context.SaveChangesAsync();
        var actor = new Actor { Pii = new ActorPii { DisplayName = "Subscription custody" }, ActorTypeId = 1, ActorType = null!, UserId = user.Id };
        context.Add(actor);
        await context.SaveChangesAsync();
        for (int index = 0; index < tenantCount; index++)
        {
            var tenant = new Tenant
            {
                Id = Guid.CreateVersion7(),
                FullName = "Subscription custody",
                Slug = $"subscription-{Guid.CreateVersion7():N}",
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!
            };
            var target = new DomainEvent(EventStatusEnum.Draft)
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenant.Id,
                Tenant = tenant,
                Title = "Subscription custody",
                Subtitle = string.Empty,
                Description = string.Empty,
                FirstSessionDate = DateOnly.FromDateTime(Now.AddDays(1)),
                LastSessionDate = DateOnly.FromDateTime(Now.AddDays(1)),
                EventTypeId = 1,
                AudienceGenderId = 1,
                AudienceAgeId = 1,
                ActorId = actor.Id,
                Actor = null!,
                OrganizerActorId = actor.Id,
                VisibilityTypeId = 1,
                VisibilityType = null!,
                EventStatus = null!,
                EventFormatId = 1,
                EventFormat = null!,
                EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated
            };
            var form = RegistrationForm.Create(tenant.Id, target.Id, "native", "subscription", "Subscription", Now);
            var version = RegistrationFormVersion.Create(form, 1, "en", null, null, Now);
            form.AddVersion(version);
            var tuple = Adapter.Tuple;
            var connection = RegistrationProviderConnection.Create(tenant.Id, "Native subscription", RegistrationProviderKindEnum.ExternalForm,
                RegistrationProviderDeploymentKindEnum.HostedSaas, tuple.ProviderCode, tuple.ProviderDeploymentCode,
                tuple.ApiVersion, tuple.AdapterPolicyVersion, tuple.ConformanceEvidenceRevision,
                "https://subscription.example.test/api", "https://subscription.example.test",
                "native-workspace", null, null, Now);
            var binding = RegistrationProviderBinding.Create(tenant.Id, connection.Id, form.Id, version.Id,
                RegistrationProviderPresentationModeEnum.Redirect, RegistrationProviderCollectionModeEnum.ProviderHosted,
                RegistrationProviderCompletionModeEnum.Callback, RegistrationProviderTrustLevelEnum.SelectedFields, null, Now);
            var state = RegistrationProviderSubscriptionState.Create(tenant.Id, binding.Id, "native.responses",
                "private-watch", operation == RegistrationProviderSubscriptionOperation.Renewal ? Now.AddHours(1) : Now.AddDays(10),
                null, Now);
            if (operation == RegistrationProviderSubscriptionOperation.Sweep)
                state.ReceiveNotification(Now);
            context.AddRange(tenant, target, form, connection, binding, state);
            _rows.Add((tenant.Id, binding.Id));
        }
        await context.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _lookup.Release();
        Adapter.Release();
        await _services.DisposeAsync();
        await using var administrator = _database.CreateDbContext();
        await administrator.Database.ExecuteSqlRawAsync(
            $"REVOKE SELECT, INSERT, UPDATE, DELETE ON {_runtimeTables} FROM \"{_runtimeLogin}\"");
    }

    private static string Table(ExploreDbContext context, Type type)
    {
        var entity = context.Model.FindEntityType(type)!;
        return $"\"{entity.GetSchema()}\".\"{entity.GetTableName()}\"";
    }

    public sealed class MutableClock(DateTime now) : TimeProvider
    {
        private DateTime _now = now;
        public override DateTimeOffset GetUtcNow() => new(_now);
        public void Set(DateTime value) => _now = value;
    }

    public sealed class EventBarrier
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Entered => _entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
        public void Release() => _released.TrySetResult();
        public async Task PauseAsync(CancellationToken cancellationToken)
        {
            _entered.TrySetResult();
            await _released.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
        }
    }

    public sealed class RecordingAdapter(MutableClock clock) :
        IRegistrationProviderDescriptor, IRegistrationProviderSubscriptionManager, IRegistrationProviderReconciliationProvider
    {
        private EventBarrier? _barrier;
        public int Handoffs { get; private set; }
        public List<Guid> Tenants { get; } = [];
        public RegistrationProviderTuple Tuple { get; } = new("NATIVE_TEST", "HOSTED", "v1", "subscription-v1", "native-evidence");
        public RegistrationProviderCapabilitySet ProvenCapabilities => RegistrationProviderCapabilitySet.None with
        { SubscriptionManagement = true, Reconciliation = true };
        public EventBarrier SuspendHandoff() => _barrier = new();
        public void Release() => _barrier?.Release();
        private async Task RecordAsync(Guid tenant, CancellationToken cancellationToken)
        {
            Handoffs++;
            Tenants.Add(tenant);
            if (_barrier is { } barrier)
                await barrier.PauseAsync(cancellationToken);
        }
        public async Task<RegistrationProviderSubscriptionResult> EnsureSubscriptionAsync(
            RegistrationProviderSubscriptionRequest request, CancellationToken cancellationToken)
        {
            await RecordAsync(request.TenantId, cancellationToken);
            return new(true, "renewed-watch", ExpiresAtUtc: clock.GetUtcNow().UtcDateTime.AddDays(6));
        }
        public async Task<RegistrationProviderReconciliationResult> ReconcileAsync(
            RegistrationProviderReconciliationRequest request, CancellationToken cancellationToken)
        {
            await RecordAsync(request.TenantId, cancellationToken);
            return new(1, false, [new("response-1", "revision-1", clock.GetUtcNow().UtcDateTime)], "native-next");
        }
    }

    private sealed class CallbackUri : IRegistrationProviderCallbackUriBuilder
    {
        public Uri Build(string providerCode, Guid bindingId) => new($"https://callback.example.test/{bindingId:N}");
    }

    private sealed class ReceiptProtector : IRegistrationProviderCallbackReceiptProtector
    {
        private readonly IDataProtector _protector = new EphemeralDataProtectionProvider().CreateProtector("native-subscription-receipt");
        public string Protect(RegistrationProviderCallbackReceipt receipt) => _protector.Protect(JsonSerializer.Serialize(receipt));
        public RegistrationProviderCallbackReceipt Unprotect(string protectedReceipt) =>
            JsonSerializer.Deserialize<RegistrationProviderCallbackReceipt>(_protector.Unprotect(protectedReceipt))!;
    }

    private sealed class BindingBarrierInterceptor(string bindingTable, string stateTable, string messageTable) : DbCommandInterceptor
    {
        private EventBarrier? _barrier;
        private bool _consumed;
        private bool _staging;
        public bool IsArmed => _barrier is not null;
        public EventBarrier Arm(bool staging = false) { _consumed = false; _staging = staging; return _barrier = new(); }
        public void Release() => _barrier?.Release();
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            // The native provider query has already run. Suspend only the binding
            // lookup, never a claim/authority query that happens to join its parent.
            bool target = _staging
                ? command.CommandText.Contains("INSERT INTO " + messageTable, StringComparison.Ordinal)
                : command.CommandText.Contains(bindingTable, StringComparison.Ordinal) &&
                    !command.CommandText.Contains(stateTable, StringComparison.Ordinal);
            if (!_consumed && _barrier is { } barrier && target)
            {
                _consumed = true;
                await barrier.PauseAsync(cancellationToken);
            }
            return result;
        }
    }

    private sealed class BarrierTenantFactory(PrimaryDatabaseConnectionOptions options,
        TenantExploreDbContextFactory production, BindingBarrierInterceptor interceptor) : ITenantExploreDbContextFactory
    {
        public Task<ExploreDbContext> CreateDbContextAsync(Guid tenantId, CancellationToken cancellationToken = default)
        {
            if (!interceptor.IsArmed)
                return production.CreateDbContextAsync(tenantId, cancellationToken);
            var builder = new DbContextOptionsBuilder<ExploreDbContext>();
            PrimaryDatabaseProviderComposition.ConfigureApplication(builder, options);
            builder.AddInterceptors(PostgresTenantSessionInterceptor.RuntimeInstance, interceptor);
            var context = new ExploreDbContext(builder.Options) { TenantContext = new TenantIdentity(tenantId) };
            context.BindTenantLease();
            return Task.FromResult(context);
        }
    }

    private sealed record TenantIdentity(Guid TenantId) : Explore.Application.Contracts.Infrastructure.ITenantContext;
}
