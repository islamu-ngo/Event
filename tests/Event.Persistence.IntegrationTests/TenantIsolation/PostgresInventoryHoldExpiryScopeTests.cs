using System.Diagnostics;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Services;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class PostgresInventoryHoldExpiryScopeTests(PostgreSqlContainerFixture fixture)
{
    private static readonly DateTime Now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task FrozenNullRuntimeRefusesAmbientMutationWhileFreshScopesExpireOnlyOwnedCurrentInventoryOnce()
    {
        await fixture.ResetAsync();
        await using var setup = OpenMigrator();
        await InstallAsync(setup);
        var a = CreateOrder(Guid.CreateVersion7());
        var b = CreateOrder(Guid.CreateVersion7());
        var aDue = Hold(a, Now.AddMinutes(-1));
        var bDue = Hold(b, Now.AddMinutes(-1));
        var aLive = Hold(a, Now.AddDays(1));
        await SeedAsync(setup, a, b, aDue, bDue, aLive);
        string runtime = Identifier(fixture.NativeQueueEnrollment.RuntimeLogin);
        string schema = Identifier(HoldTableSchema(setup));
        string holds = Table<RegistrationInventoryHold>(setup), orders = Table<RegistrationOrder>(setup);
        string attempts = Table<PaymentAttempt>(setup);
        var names = new HumanPrincipalSourceSql(setup);
        string holdStatuses = Table<RegistrationInventoryHoldStatus>(setup);
        string orderStatuses = Table<RegistrationOrderStatus>(setup);
        string holdStatusId = names.Column(typeof(RegistrationInventoryHoldStatus), nameof(RegistrationInventoryHoldStatus.Id));
        string orderStatusId = names.Column(typeof(RegistrationOrderStatus), nameof(RegistrationOrderStatus.Id));
        await setup.Database.ExecuteSqlRawAsync($"""
            GRANT USAGE ON SCHEMA {schema} TO {runtime};
            GRANT SELECT, UPDATE ON {holds}, {orders} TO {runtime};
            GRANT SELECT ON {attempts} TO {runtime};
            GRANT SELECT ({holdStatusId}) ON {holdStatuses} TO {runtime};
            GRANT SELECT ({orderStatusId}) ON {orderStatuses} TO {runtime};
            """);
        try
        {
            // Reproduce the original acquisition order through the real provider:
            // repository constructed on a frozen null lease, then ambient tenancy changed.
            var options = TestDbContextOptions.Create<ExploreDbContext>();
            PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime));
            options.AddInterceptors(PostgresTenantSessionInterceptor.RuntimeInstance);
            var ambient = new TenantContextAccessor(new Microsoft.AspNetCore.Http.HttpContextAccessor());
            await using var oldContext = new ExploreDbContext(options.Options) { TenantContext = new TenantFacts(ambient) };
            oldContext.BindTenantLease();
            var oldRepository = new RegistrationInventoryRepository(oldContext);
            ambient.SetTenant(a.TenantId);
            var frozen = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await oldRepository.GetHoldsByOrderAsync(a.Id, a.TenantId, CancellationToken.None));
            await Assert.That(frozen?.Message).IsEqualTo("tenant_lease_identity_changed");

            await using var services = Services();
            await using var parent = services.CreateAsyncScope();
            var scopes = parent.ServiceProvider.GetRequiredService<IInventoryHoldExpiryExecutionScopeFactory>();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            async Task<bool> ExpireAsync()
            {
                await using var scope = await scopes.CreateAsync(a.TenantId, deadline.Token);
                var context = scope.Services.GetRequiredService<ExploreDbContext>();
                await Assert.That(context.TenantFilterTenantId).IsEqualTo(a.TenantId);
                await Assert.That(await context.RegistrationInventoryHolds.IgnoreQueryFilters()
                    .CountAsync(row => row.Id == bDue.Id, deadline.Token)).IsEqualTo(0);
                return await scope.Services.GetRequiredService<IRegistrationInventoryRepository>()
                    .TryExpireDueHoldAsync(aDue.Id, Now, deadline.Token);
            }
            // A second independently constructed worker scope must observe the
            // committed expiry and cannot release the inventory a second time.
            await Assert.That(await ExpireAsync()).IsTrue();
            await Assert.That(await ExpireAsync()).IsFalse();
            await using var observerScope = await scopes.CreateAsync(a.TenantId, deadline.Token);
            var repository = observerScope.Services.GetRequiredService<IRegistrationInventoryRepository>();
            var rows = await repository.GetHoldsByOrderAsync(a.Id, a.TenantId, deadline.Token);
            var expired = rows.Single(row => row.Id == aDue.Id);
            await Assert.That(expired.RegistrationInventoryHoldStatusId).IsEqualTo((int)RegistrationInventoryHoldStatusEnum.Expired);
            await Assert.That(rows.Single(row => row.Id == aLive.Id).RegistrationInventoryHoldStatusId)
                .IsEqualTo((int)RegistrationInventoryHoldStatusEnum.Active);
            await Assert.That(await repository.TryExpireDueHoldAsync(bDue.Id, Now, deadline.Token)).IsFalse();
            await Assert.That(await repository.TryExpireDueHoldAsync(aLive.Id, Now, deadline.Token)).IsFalse();
            await using var bScope = await scopes.CreateAsync(b.TenantId, deadline.Token);
            var foreignRows = await bScope.Services.GetRequiredService<IRegistrationInventoryRepository>()
                .GetHoldsByOrderAsync(b.Id, b.TenantId, deadline.Token);
            await Assert.That(foreignRows.Single().RegistrationInventoryHoldStatusId)
                .IsEqualTo((int)RegistrationInventoryHoldStatusEnum.Active);
        }
        finally
        {
            await setup.Database.ExecuteSqlRawAsync($"""
                REVOKE SELECT, UPDATE ON {holds}, {orders} FROM {runtime};
                REVOKE SELECT ON {attempts} FROM {runtime};
                REVOKE SELECT ({holdStatusId}) ON {holdStatuses} FROM {runtime};
                REVOKE SELECT ({orderStatusId}) ON {orderStatuses} FROM {runtime};
                """);
        }
    }

    [Test]
    public async Task GlobalDiscoveryExportsOnlyBoundedIdentitiesAndRejectsBusinessReadsRuntimeAndInvalidBounds()
    {
        await fixture.ResetAsync();
        await using var setup = OpenMigrator();
        await InstallAsync(setup);
        await InstallAsync(setup);
        var a = CreateOrder(Guid.CreateVersion7());
        var b = CreateOrder(Guid.CreateVersion7());
        var aDue = Hold(a, Now.AddMinutes(-2));
        var bDue = Hold(b, Now.AddMinutes(-1));
        var live = Hold(b, Now.AddMinutes(1));
        b.TryBeginHoldExpiryRecovery(Now);
        await SeedAsync(setup, a, b, aDue, bDue, live);
        var factory = new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global));
        var discovery = new InventoryHoldExpiryDiscovery(factory, fixture.NativeQueueEnrollment);
        var targets = await discovery.GetTargetsAsync(Now, 100, CancellationToken.None);
        await Assert.That(targets).IsEquivalentTo([
            new InventoryHoldExpiryTarget(a.TenantId, a.Id, aDue.Id),
            new InventoryHoldExpiryTarget(b.TenantId, b.Id, bDue.Id),
            new InventoryHoldExpiryTarget(b.TenantId, b.Id, null)]);
        var limited = await discovery.GetTargetsAsync(Now, 1, CancellationToken.None);
        await Assert.That(limited.Count).IsEqualTo(2);
        await Assert.That(limited[0]).IsEqualTo(new InventoryHoldExpiryTarget(a.TenantId, a.Id, aDue.Id));
        await Assert.That(limited[1]).IsEqualTo(new InventoryHoldExpiryTarget(b.TenantId, b.Id, null));
        await using var global = await factory.CreateDbContextAsync();
        await global.Database.OpenConnectionAsync();
        await PostgresInventoryHoldExpiryReadPermissions.RequireReadyAsync(global, fixture.NativeQueueEnrollment);
        using (var probe = new DiscoveryQueryProbe(global))
        {
            var observed = await InventoryHoldExpiryQuery.ReadAsync(global, Now, 100, CancellationToken.None);
            await Assert.That(observed).IsEquivalentTo(targets);
            await Assert.That(probe.Commands).IsEqualTo(1);
        }
        await using var command = global.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid=p.pronamespace
                WHERE n.nspname=@schema AND p.proname='inventory_hold_expiry_targets')
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname IN
                    ('hold_expiry_discovery_owner','hold_expiry_discovery_migrator','hold_expiry_discovery_executor'))
            """;
        command.Parameters.Add(new NpgsqlParameter("schema", HoldTableSchema(setup)));
        await Assert.That(await command.ExecuteScalarAsync()).IsEqualTo(true);
        foreach (string table in new[] { Table<RegistrationInventoryHold>(setup), Table<RegistrationOrder>(setup) })
        {
            var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
                await global.Database.ExecuteSqlRawAsync($"SELECT * FROM {table}"));
            await Assert.That(denied?.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        }
        var runtimeFactory = new TenantExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime));
        await using var runtime = await runtimeFactory.CreateDbContextAsync(a.TenantId);
        foreach (var caller in new[] { runtime, setup })
        {
            await caller.Database.OpenConnectionAsync();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                PostgresInventoryHoldExpiryReadPermissions.RequireReadyAsync(caller, fixture.NativeQueueEnrollment));
        }
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            discovery.GetTargetsAsync(Now, 101, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            discovery.GetTargetsAsync(Now, 0, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            discovery.GetTargetsAsync(DateTime.SpecifyKind(Now, DateTimeKind.Unspecified), 1, CancellationToken.None));
    }

    [Test]
    public async Task DiscoveryFailsClosedOnGlobalDelegationAndSourceReadDrift()
    {
        await using var setup = OpenMigrator();
        await InstallAsync(setup);
        // Runtime cannot acquire the Global identity, and Global cannot acquire
        // whole business records. Neither fault may be normalized by reinstallation.
        foreach (string fault in new[]
        {
            $"GRANT {Identifier(fixture.NativeQueueEnrollment.GlobalLogin)} TO {Identifier(fixture.NativeQueueEnrollment.RuntimeLogin)} WITH INHERIT FALSE, SET FALSE, ADMIN TRUE",
            $"GRANT SELECT ON {Table<RegistrationInventoryHold>(setup)} TO {Identifier(fixture.NativeQueueEnrollment.GlobalLogin)}"
        })
        {
            await setup.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await setup.Database.BeginTransactionAsync();
                await setup.Database.ExecuteSqlRawAsync(fault);
                var denied = await Assert.ThrowsAsync<InvalidOperationException>(() => InstallAsync(setup));
                await Assert.That(denied.Message).IsEqualTo("hold_expiry_read_permissions_drift");
                await Assert.ThrowsAsync<InvalidOperationException>(() => InstallAsync(setup));
                await transaction.RollbackAsync();
            });
        }
        await InstallAsync(setup);
    }

    private Task InstallAsync(ExploreDbContext context) =>
        PostgresInventoryHoldExpiryReadPermissions.ApplyAsync(context, fixture.NativeQueueEnrollment);

    private ExploreDbContext OpenMigrator()
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        var context = new ExploreDbContext(options.Options);
        context.BindTenantLease();
        return context;
    }

    private ServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITenantExploreDbContextFactory>(
            new TenantExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime)));
        services.AddSingleton<IGlobalExploreDbContextFactory>(
            new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global)));
        services.AddSingleton(fixture.NativeQueueEnrollment);
        services.AddHttpContextAccessor();
        services.AddScoped<ITenantContextAccessor, TenantContextAccessor>();
        services.AddInventoryHoldExpiryWorker();
        services.AddScoped(provider => InventoryHoldExpiryExecutionScopeFactory.GetSelectedRuntimeContext(provider)
            ?? throw new InvalidOperationException("Tenant must be selected before resolving Runtime."));
        services.AddScoped<IRegistrationInventoryRepository, RegistrationInventoryRepository>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    private static RegistrationOrder CreateOrder(Guid tenant)
    {
        var order = RegistrationOrder.Create(tenant, Guid.CreateVersion7(), Guid.CreateVersion7(), null,
            BookingPartyTypeEnum.Individual, Guid.CreateVersion7(),
            RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(), 4, 3, 2, GuestRecoveryPolicyEnum.VerifiedEmailRequired),
            null, null, "USD", Now.AddHours(-1), null);
        order.TransitionTo(RegistrationOrderStatusEnum.AwaitingParticipantDetails, Now.AddHours(-1));
        return order;
    }

    private static RegistrationInventoryHold Hold(RegistrationOrder order, DateTime expires) =>
        RegistrationInventoryHold.Create(order.Id, Guid.CreateVersion7(), Guid.CreateVersion7(),
            order.TenantId, 1, Now.AddHours(-1), expires);

    private static async Task SeedAsync(ExploreDbContext context, params object[] rows)
    {
        await context.Database.OpenConnectionAsync();
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            await context.Database.ExecuteSqlRawAsync("SET LOCAL session_replication_role = replica");
            context.AddRange(rows);
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        });
    }

    private static string Identifier(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    private static string HoldTableSchema(ExploreDbContext context) =>
        context.Model.FindEntityType(typeof(RegistrationInventoryHold))!.GetSchema()!;
    private static string Table<T>(ExploreDbContext context) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier(
            context.Model.FindEntityType(typeof(T))!.GetTableName()!, context.Model.FindEntityType(typeof(T))!.GetSchema());
    private sealed record TenantFacts(ITenantContextAccessor Accessor) : Explore.Application.Contracts.Infrastructure.ITenantContext
    {
        public Guid TenantId => Accessor.TenantId ?? Guid.Empty;
    }

    private sealed class DiscoveryQueryProbe : IObserver<DiagnosticListener>,
        IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly ExploreDbContext context;
        private readonly DbContextId contextId;
        private readonly IDisposable listeners;
        private readonly System.Collections.Concurrent.ConcurrentBag<IDisposable> subscriptions = [];
        public DiscoveryQueryProbe(ExploreDbContext selected)
        {
            context = selected;
            contextId = selected.ContextId;
            listeners = DiagnosticListener.AllListeners.Subscribe(this);
        }
        public int Commands { get; private set; }
        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name == "Microsoft.EntityFrameworkCore")
                subscriptions.Add(listener.Subscribe(this, name => name == RelationalEventId.CommandExecuted.Name));
        }
        public void OnNext(KeyValuePair<string, object?> diagnostic)
        {
            if (diagnostic.Value is CommandExecutedEventData command
                && ReferenceEquals(command.Context, context) && command.Context?.ContextId == contextId
                && command.ExecuteMethod == DbCommandMethod.ExecuteReader)
                Commands++;
        }
        public void OnCompleted() { }
        public void OnError(Exception error) => throw error;
        public void Dispose()
        {
            listeners.Dispose();
            foreach (var subscription in subscriptions) subscription.Dispose();
        }
    }
}
