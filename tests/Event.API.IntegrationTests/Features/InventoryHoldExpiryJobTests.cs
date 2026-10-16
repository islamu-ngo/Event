using Explore.Persistence.Database.ProviderPrimitives;
using Explore.API.Scheduling;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Scheduling;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Services.Registration;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Seed;
using Explore.Secrets.Database;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Quartz;

namespace ApiIntegrationTests.Features;

public sealed class InventoryHoldExpiryJobTests
{
    [Test]
    public async Task DeadlineUsesFreshRuntimeScopeAndExpiresOnlyItsCurrentOwnedDueHoldOnce()
    {
        await using var data = await NativeWorker.CreateAsync();
        await using var parent = data.Services.CreateAsyncScope();
        var accessor = parent.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        await Assert.That(accessor.TenantId).IsNull();
        var job = parent.ServiceProvider.GetRequiredService<InventoryHoldExpiryJob>();
        await job.Execute(Context(data.A.TenantId, data.A.OrderId));
        var expired = await data.ReadHoldAsync(data.A, data.A.DueHoldId);
        await Assert.That(expired.RegistrationInventoryHoldStatusId).IsEqualTo((int)RegistrationInventoryHoldStatusEnum.Expired);
        await Assert.That(expired.ReleasedAt).IsNotNull();
        await Assert.That((await data.ReadOrderAsync(data.A)).RegistrationOrderStatusId)
            .IsEqualTo((int)RegistrationOrderStatusEnum.ReadyForCheckout);
        await Assert.That((await data.ReadHoldAsync(data.B, data.B.DueHoldId)).RegistrationInventoryHoldStatusId)
            .IsEqualTo((int)RegistrationInventoryHoldStatusEnum.Active);
        await Assert.That((await data.ReadHoldAsync(data.A, data.A.FutureHoldId)).RegistrationInventoryHoldStatusId)
            .IsEqualTo((int)RegistrationInventoryHoldStatusEnum.Active);
        await job.Execute(Context(data.A.TenantId, data.A.OrderId));
        var replay = await data.ReadHoldAsync(data.A, data.A.DueHoldId);
        await Assert.That(replay.ReleasedAt).IsEqualTo(expired.ReleasedAt);
        await Assert.That(replay.ConcurrencyStamp).IsEqualTo(expired.ConcurrencyStamp);
        await Assert.That(accessor.TenantId).IsNull();
    }

    [Test]
    public async Task FrozenRuntimeLeaseRejectsRetargetingAndWorkerScopesRemainIndependent()
    {
        await using var data = await NativeWorker.CreateAsync();
        await using var parent = data.Services.CreateAsyncScope();
        var factory = parent.ServiceProvider.GetRequiredService<IInventoryHoldExpiryExecutionScopeFactory>();
        await using var first = await factory.CreateAsync(data.A.TenantId, CancellationToken.None);
        var contextA = first.Services.GetRequiredService<ExploreDbContext>();
        var tenantA = first.Services.GetRequiredService<ITenantContextAccessor>();
        var foreign = new TenantIdentity(data.B.TenantId);
        var denied = Assert.Throws<InvalidOperationException>(() => contextA.TenantContext = foreign);
        await Assert.That(denied.Message).IsEqualTo("tenant_lease_identity_changed");
        Assert.Throws<InvalidOperationException>(() => tenantA.SetTenant(data.B.TenantId));
        Assert.Throws<InvalidOperationException>(() => tenantA.Clear());
        await using var second = await factory.CreateAsync(data.B.TenantId, CancellationToken.None);
        var contextB = second.Services.GetRequiredService<ExploreDbContext>();
        await Assert.That(ReferenceEquals(contextA, contextB)).IsFalse();
        await Assert.That(contextA.TenantFilterTenantId).IsEqualTo(data.A.TenantId);
        await Assert.That(contextB.TenantFilterTenantId).IsEqualTo(data.B.TenantId);
    }

    [Test]
    public async Task DeadlineDropsMalformedEmptyAndForeignOrderPointersWithoutExpiringInventory()
    {
        await using var data = await NativeWorker.CreateAsync();
        await using var parent = data.Services.CreateAsyncScope();
        var job = parent.ServiceProvider.GetRequiredService<InventoryHoldExpiryJob>();
        await job.Execute(Context(Guid.Empty, data.A.OrderId));
        var invalid = Substitute.For<IJobExecutionContext>();
        invalid.MergedJobDataMap.Returns(new JobDataMap
        {
            [ScheduledDeadlinePointerKeys.TenantId] = "not-a-guid",
            [ScheduledDeadlinePointerKeys.RegistrationOrderId] = data.A.OrderId.ToString()
        });
        await job.Execute(invalid);
        await job.Execute(Context(data.A.TenantId, data.B.OrderId));
        foreach (var owner in new[] { data.A, data.B })
            await Assert.That((await data.ReadHoldAsync(owner, owner.DueHoldId)).RegistrationInventoryHoldStatusId)
                .IsEqualTo((int)RegistrationInventoryHoldStatusEnum.Active);
    }

    [Test]
    public async Task ReconciliationDiscoversBothTenantsAndRecoversInterruptedExpiryWithoutDueHold()
    {
        await using var data = await NativeWorker.CreateAsync();
        // Committed prior-pass expiry is the recovery-target signal, not a timing delay.
        await using (var prior = await data.Runtime.CreateDbContextAsync(data.A.TenantId))
        {
            await Assert.That(await new RegistrationInventoryRepository(prior)
                .TryExpireDueHoldAsync(data.A.DueHoldId, NativeWorker.ObservedAt, CancellationToken.None)).IsTrue();
        }
        await using var parent = data.Services.CreateAsyncScope();
        var discovery = parent.ServiceProvider.GetRequiredService<IInventoryHoldExpiryDiscovery>();
        var targets = await discovery.GetTargetsAsync(NativeWorker.ObservedAt, 100, CancellationToken.None);
        await Assert.That(targets.Any(target => target.TenantId == data.A.TenantId && target.HoldId is null)).IsTrue();
        await Assert.That(targets.Any(target => target.TenantId == data.B.TenantId && target.HoldId == data.B.DueHoldId)).IsTrue();
        var job = parent.ServiceProvider.GetRequiredService<InventoryHoldExpiryReconciliationJob>();
        await job.Execute(Substitute.For<IJobExecutionContext>());
        foreach (var owner in new[] { data.A, data.B })
        {
            await Assert.That((await data.ReadOrderAsync(owner)).RegistrationOrderStatusId)
                .IsEqualTo((int)RegistrationOrderStatusEnum.ReadyForCheckout);
            await Assert.That((await data.ReadHoldAsync(owner, owner.DueHoldId)).RegistrationInventoryHoldStatusId)
                .IsEqualTo((int)RegistrationInventoryHoldStatusEnum.Expired);
            await Assert.That((await data.ReadHoldAsync(owner, owner.FutureHoldId)).RegistrationInventoryHoldStatusId)
                .IsEqualTo((int)RegistrationInventoryHoldStatusEnum.Active);
        }
        var stamp = (await data.ReadHoldAsync(data.B, data.B.DueHoldId)).ConcurrencyStamp;
        await job.Execute(Substitute.For<IJobExecutionContext>());
        await Assert.That((await data.ReadHoldAsync(data.B, data.B.DueHoldId)).ConcurrencyStamp).IsEqualTo(stamp);
    }

    private static IJobExecutionContext Context(Guid tenantId, Guid orderId)
    {
        var context = Substitute.For<IJobExecutionContext>();
        context.MergedJobDataMap.Returns(new JobDataMap
        {
            [ScheduledDeadlinePointerKeys.TenantId] = tenantId.ToString(),
            [ScheduledDeadlinePointerKeys.RegistrationOrderId] = orderId.ToString()
        });
        return context;
    }

    private sealed record Owner(Guid TenantId, Guid OrderId, Guid DueHoldId, Guid FutureHoldId);
    private sealed record TenantIdentity(Guid TenantId) : ITenantContext;

    private sealed class NativeWorker(string path, ServiceProvider services, TenantExploreDbContextFactory runtime, Owner a, Owner b)
        : IAsyncDisposable
    {
        public static readonly DateTime ObservedAt = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public ServiceProvider Services => services;
        public TenantExploreDbContextFactory Runtime => runtime;
        public Owner A => a;
        public Owner B => b;

        public static async Task<NativeWorker> CreateAsync()
        {
            string path = Path.Combine(Path.GetTempPath(), $"hold-expiry-{Guid.CreateVersion7():N}.db");
            var options = new PrimaryDatabaseConnectionOptions
            {
                Provider = PrimaryDatabaseProvider.Sqlite,
                Role = PrimaryDatabaseRole.Runtime,
                Database = path
            };
            var runtime = new TenantExploreDbContextFactory(options);
            var global = new GlobalExploreDbContextFactory(options with { Role = PrimaryDatabaseRole.Global });
            await using var setup = await global.CreateDbContextAsync();
            await setup.Database.EnsureCreatedAsync();
            await LookupTableSeeder.SeedAsync(setup);
            await setup.Database.OpenConnectionAsync();
            // Setup alone omits unrelated parent rows; worker contexts retain ordinary provider constraints.
            await setup.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");
            Owner a = Seed(setup), b = Seed(setup);
            await setup.SaveChangesAsync();
            await setup.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON");
            var services = new ServiceCollection();
            services.AddSingleton<ITenantExploreDbContextFactory>(runtime);
            services.AddSingleton<IGlobalExploreDbContextFactory>(global);
            services.AddHttpContextAccessor();
            services.AddScoped<ITenantContextAccessor, TenantContextAccessor>();
            services.AddInventoryHoldExpiryWorker();
            services.AddScoped(provider => InventoryHoldExpiryExecutionScopeFactory.GetSelectedRuntimeContext(provider)
                ?? throw new InvalidOperationException("Worker dependencies resolved before tenant selection."));
            services.AddScoped<IRegistrationInventoryRepository, RegistrationInventoryRepository>();
            services.AddScoped<IRegistrationOrderLifecycleService>(provider =>
            {
                var context = provider.GetRequiredService<ExploreDbContext>();
                var inventory = provider.GetRequiredService<IRegistrationInventoryRepository>();
                return new RegistrationOrderLifecycleService(
                    inventory, new PromotionRedemptionRepository(context), new RegistrationParticipantRepository(context),
                    new EventTicketCatalogRepository(context), new PlatformContributionSettingRepository(context),
                    new EventSessionRepository(context),
                    new OutboxRepository(context), new EfCoreUnitOfWork(context), new RegistrationFinalizationRepository(context),
                    new RegistrationPaymentAttemptRepository(context), Substitute.For<IScheduledDeadlineDispatcher>(),
                    TimeProvider.System, new UnusedPaidAcceptance(), new RegistrationOrderTransitionCoordinator(inventory));
            });
            services.AddSingleton<Microsoft.Extensions.Logging.ILogger<InventoryHoldExpiryJob>>(NullLogger<InventoryHoldExpiryJob>.Instance);
            services.AddSingleton<Microsoft.Extensions.Logging.ILogger<InventoryHoldExpiryReconciliationJob>>(
                NullLogger<InventoryHoldExpiryReconciliationJob>.Instance);
            services.AddScoped<InventoryHoldExpiryJob>();
            services.AddScoped<InventoryHoldExpiryReconciliationJob>();
            return new NativeWorker(path, services.BuildServiceProvider(new ServiceProviderOptions
            { ValidateOnBuild = true, ValidateScopes = true }), runtime, a, b);
        }

        private static Owner Seed(ExploreDbContext context)
        {
            Guid tenant = Guid.CreateVersion7(), eventId = Guid.CreateVersion7();
            context.Tenants.Add(new Tenant
            {
                Id = tenant,
                FullName = "Hold expiry tenant",
                Slug = $"hold-expiry-{tenant:N}",
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!
            });
            var catalog = EventTicketCatalogVersion.Create(tenant, eventId, "USD", 1);
            var pool = EventCapacityPool.Create(tenant, eventId, "Hold expiry capacity", 10, 900,
                CapacityHoldPolicyEnum.TimedHoldOnSelection, CapacityOversellPolicyEnum.Disallow, true);
            var ticket = EventTicketType.Create(Guid.CreateVersion7(), tenant, catalog.Id, "Admission", "USD",
                TicketPricingModeEnum.Free, null, null, null, ParticipantDataCollectionModeEnum.None, pool.Id,
                null, null, false, false, null, null, null, null);
            catalog.AddTicketType(ticket, pool);
            catalog.AddEntitlement(ticket, TicketTypeEntitlement.CreateForEvent(ticket.Id, tenant, eventId, 1));
            catalog.Publish();
            var order = RegistrationOrder.Create(
                tenant, eventId, Guid.CreateVersion7(), null, BookingPartyTypeEnum.Individual, catalog.Id,
                RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(), 4, 3, 2, GuestRecoveryPolicyEnum.VerifiedEmailRequired),
                null, null, "USD", ObservedAt.AddDays(-1), null);
            order.AddLine(RegistrationOrderLine.Create(Guid.CreateVersion7(), catalog, ticket, order.Id, 1, null, null));
            order.TransitionTo(RegistrationOrderStatusEnum.AwaitingParticipantDetails, ObservedAt.AddDays(-1));
            var due = RegistrationInventoryHold.Create(order.Id, pool.Id, ticket.Id, tenant, 1,
                ObservedAt.AddDays(-1), ObservedAt.AddHours(-1));
            var future = RegistrationInventoryHold.Create(order.Id, pool.Id, ticket.Id, tenant, 1,
                ObservedAt.AddDays(-1), new DateTime(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            context.AddRange(catalog, pool, order, due, future);
            return new Owner(tenant, order.Id, due.Id, future.Id);
        }

        public async Task<RegistrationInventoryHold> ReadHoldAsync(Owner owner, Guid holdId)
        {
            await using var context = await runtime.CreateDbContextAsync(owner.TenantId);
            return (await new RegistrationInventoryRepository(context)
                .GetHoldsByOrderAsync(owner.OrderId, owner.TenantId, CancellationToken.None)).Single(hold => hold.Id == holdId);
        }

        public async Task<RegistrationOrder> ReadOrderAsync(Owner owner)
        {
            await using var context = await runtime.CreateDbContextAsync(owner.TenantId);
            return (await new RegistrationInventoryRepository(context)
                .GetOrderWithLinesAsync(owner.OrderId, owner.TenantId, CancellationToken.None))!;
        }

        public async ValueTask DisposeAsync()
        {
            await services.DisposeAsync();
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
            SqliteConnection.ClearPool(connection);
            File.Delete(path);
        }
    }

    /// <summary>Paid acceptance is outside the free recovery path; entering it must fail the test.</summary>
    private sealed class UnusedPaidAcceptance : IPaidOrderAcceptanceService
    {
        public Task<PaidOrderAcceptanceResult> DescribeAsync(RegistrationOrder order, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Free hold recovery must not require paid acceptance.");
        public Task<PaidOrderAcceptanceResult> DescribeAsync(RegistrationOrder order, Guid? reservedPaymentAttemptId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Free hold recovery must not require paid acceptance.");
        public Task<PaidOrderAcceptanceResult> AcceptAsync(RegistrationOrder order, PaidOrderAcceptanceAcknowledgementDto? acknowledgement,
            DateTime acceptedAt, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Free hold recovery must not require paid acceptance.");
    }
}
