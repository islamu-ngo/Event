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
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
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
        await using var setup = fixture.CreateDbContext();
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
        await setup.Database.ExecuteSqlRawAsync($"""
            GRANT USAGE ON SCHEMA {schema} TO {runtime};
            GRANT SELECT, UPDATE ON {holds}, {orders} TO {runtime};
            GRANT SELECT ON {attempts} TO {runtime};
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
                """);
        }
    }

    [Test]
    public async Task GlobalDiscoveryExportsOnlyBoundedIdentitiesAndRejectsBusinessReadsRuntimeAndInvalidBounds()
    {
        await fixture.ResetAsync();
        await using var setup = fixture.CreateDbContext();
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
        var discovery = new InventoryHoldExpiryDiscovery(factory);
        var targets = await discovery.GetTargetsAsync(Now, 100, CancellationToken.None);
        await Assert.That(targets).IsEquivalentTo([
            new InventoryHoldExpiryTarget(a.TenantId, a.Id, aDue.Id),
            new InventoryHoldExpiryTarget(b.TenantId, b.Id, bDue.Id),
            new InventoryHoldExpiryTarget(b.TenantId, b.Id, null)]);
        await Assert.That((await discovery.GetTargetsAsync(Now, 1, CancellationToken.None)).Count).IsEqualTo(2);
        await using var global = await factory.CreateDbContextAsync();
        await global.Database.OpenConnectionAsync();
        await using var command = global.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"SELECT * FROM {PostgresInventoryHoldExpiryDiscoveryContract.FunctionSql(global)}(@now, @take)";
        command.Parameters.Add(new NpgsqlParameter("now", Now));
        command.Parameters.Add(new NpgsqlParameter("take", 100));
        await using (var reader = await command.ExecuteReaderAsync())
        {
            await Assert.That(reader.FieldCount).IsEqualTo(3);
            await Assert.That(Enumerable.Range(0, reader.FieldCount).Select(reader.GetName))
                .IsEquivalentTo(["TenantId", "OrderId", "HoldId"]);
        }
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
            var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
                await caller.Database.ExecuteSqlRawAsync(
                    $"SELECT * FROM {PostgresInventoryHoldExpiryDiscoveryContract.FunctionSql(setup)}({{0}}, {{1}})", [Now, 1]));
            await Assert.That(denied?.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        }
        var invalid = await Assert.ThrowsAsync<PostgresException>(async () =>
            await global.Database.ExecuteSqlRawAsync(
                $"SELECT * FROM {PostgresInventoryHoldExpiryDiscoveryContract.FunctionSql(global)}({{0}}, {{1}})", [Now, 101]));
        await Assert.That(invalid?.SqlState).IsEqualTo(PostgresErrorCodes.InvalidParameterValue);
    }

    [Test]
    public async Task DiscoveryFailsClosedOnUndeclaredNativeExecutorAndSourceReadDrift()
    {
        await using var setup = fixture.CreateDbContext();
        await InstallAsync(setup);
        // Runtime is deliberately NOT an enrolled executor. These two independent
        // faults must each fail reinstall instead of becoming an implicit allowlist.
        foreach (string fault in new[]
        {
            $"GRANT EXECUTE ON FUNCTION {PostgresInventoryHoldExpiryDiscoveryContract.FunctionSql(setup)}(timestamp with time zone, integer) TO {Identifier(fixture.NativeQueueEnrollment.RuntimeLogin)}",
            $"GRANT SELECT ON {Table<RegistrationInventoryHold>(setup)} TO {Identifier(fixture.NativeQueueEnrollment.GlobalLogin)}"
        })
        {
            await setup.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await setup.Database.BeginTransactionAsync();
                await setup.Database.ExecuteSqlRawAsync(fault);
                var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
                    await setup.Database.ExecuteSqlRawAsync(
                        PostgresInventoryHoldExpiryDiscoveryContract.BuildSql(setup, fixture.NativeQueueEnrollment)));
                await Assert.That(denied?.SqlState).IsEqualTo(PostgresErrorCodes.ObjectNotInPrerequisiteState);
                await transaction.RollbackAsync();
            });
        }
        await InstallAsync(setup);
    }

    private Task InstallAsync(ExploreDbContext context) =>
        PostgresInventoryHoldExpiryDiscoveryContract.ApplyAsync(context, fixture.NativeQueueEnrollment);

    private ServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITenantExploreDbContextFactory>(
            new TenantExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime)));
        services.AddSingleton<IGlobalExploreDbContextFactory>(
            new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global)));
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
}
