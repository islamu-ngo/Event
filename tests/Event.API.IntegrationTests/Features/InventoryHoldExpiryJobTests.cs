using Explore.API.Scheduling;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Scheduling;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Quartz;
using TUnit.Core;

namespace ApiIntegrationTests.Features;

public sealed class InventoryHoldExpiryJobTests
{
    [Test]
    public async Task ExpiryJobExpiresItsOrdersDueHoldsAndTriggersLifecycleRecovery()
    {
        var tenantId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        RegistrationInventoryHold hold = CreateHold(tenantId, orderId);

        var inventory = Substitute.For<IRegistrationInventoryRepository>();
        inventory.GetHoldsByOrderAsync(orderId, tenantId, Arg.Any<CancellationToken>()).Returns([hold]);
        inventory.TryExpireDueHoldAsync(hold.Id, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);
        var lifecycle = SubstituteLifecycle();
        var tenantAccessor = Substitute.For<ITenantContextAccessor>();

        await CreateExpiryJob(inventory, lifecycle, tenantAccessor)
            .Execute(CreateContext(PointerFor(tenantId, orderId)));

        await inventory.Received(1).TryExpireDueHoldAsync(hold.Id, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await lifecycle.Received(1).RecoverExpiredHoldAsync(orderId, tenantId, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Tenant scope is ambient, so a job that sets it and does not clear it silently widens whatever runs
    /// next on that scope. It must be cleared even when the work itself fails.
    /// </summary>
    [Test]
    public async Task ExpiryJobSetsAndClearsTenantContextEvenWhenTheWorkFails()
    {
        var tenantId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var inventory = Substitute.For<IRegistrationInventoryRepository>();
        inventory.GetHoldsByOrderAsync(orderId, tenantId, Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<RegistrationInventoryHold>>>(_ => throw new InvalidOperationException("database unavailable"));
        var tenantAccessor = Substitute.For<ITenantContextAccessor>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateExpiryJob(inventory, SubstituteLifecycle(), tenantAccessor)
                .Execute(CreateContext(PointerFor(tenantId, orderId))));

        await Assert.That(exception).IsNotNull();
        tenantAccessor.Received(1).SetTenant(tenantId);
        tenantAccessor.Received(1).Clear();
    }

    /// <summary>
    /// A deadline can fire after a checkout already consumed the holds. The conditional expiry reports that
    /// nothing was due, and the job must then leave the order alone rather than re-running recovery on an
    /// order that has moved on.
    /// </summary>
    [Test]
    public async Task ExpiryJobIsANoOpWhenNoHoldWasStillDue()
    {
        var tenantId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        RegistrationInventoryHold hold = CreateHold(tenantId, orderId);

        var inventory = Substitute.For<IRegistrationInventoryRepository>();
        inventory.GetHoldsByOrderAsync(orderId, tenantId, Arg.Any<CancellationToken>()).Returns([hold]);
        inventory.TryExpireDueHoldAsync(hold.Id, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(false);
        var lifecycle = SubstituteLifecycle();

        await CreateExpiryJob(inventory, lifecycle, Substitute.For<ITenantContextAccessor>())
            .Execute(CreateContext(PointerFor(tenantId, orderId)));

        await lifecycle.DidNotReceive().RecoverExpiredHoldAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExpiryJobSkipsAnUnusablePointerInsteadOfThrowing()
    {
        var inventory = Substitute.For<IRegistrationInventoryRepository>();
        var dataMap = new JobDataMap
        {
            { ScheduledDeadlinePointerKeys.TenantId, "not-a-guid" },
            { ScheduledDeadlinePointerKeys.RegistrationOrderId, Guid.CreateVersion7().ToString() }
        };

        await CreateExpiryJob(inventory, SubstituteLifecycle(), Substitute.For<ITenantContextAccessor>())
            .Execute(CreateContext(dataMap));

        await inventory.DidNotReceive().GetHoldsByOrderAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The reason the sweep survived the move to precise deadlines: an order whose deadline never fired —
    /// lost trigger, hold created before the feature shipped — is still found and released here.
    /// </summary>
    [Test]
    public async Task ReconciliationSweepRecoversAnOrderWhoseDeadlineNeverFired()
    {
        var tenantId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        RegistrationInventoryHold orphaned = CreateHold(tenantId, orderId);
        var lifecycles = new List<IRegistrationOrderLifecycleService>();
        var repositoryCount = 0;

        var services = new ServiceCollection();
        services.AddScoped<ITenantContextAccessor>(_ => Substitute.For<ITenantContextAccessor>());
        services.AddScoped<IRegistrationInventoryRepository>(_ =>
        {
            var repository = Substitute.For<IRegistrationInventoryRepository>();
            if (Interlocked.Increment(ref repositoryCount) == 1)
            {
                repository.GetExpiredActiveHoldsAsync(Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                    .Returns([orphaned]);
                repository.GetHoldExpiryRecoveryTargetsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
                    .Returns([]);
            }
            else
            {
                repository.TryExpireDueHoldAsync(Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
                    .Returns(true);
            }

            return repository;
        });
        services.AddScoped<IRegistrationOrderLifecycleService>(_ =>
        {
            var lifecycle = SubstituteLifecycle();
            lifecycles.Add(lifecycle);
            return lifecycle;
        });

        await using ServiceProvider provider = BuildProvider(services);

        await CreateReconciliationJob(provider).Execute(CreateContext());

        await Assert.That(lifecycles).HasSingleItem();
        await lifecycles.Single().Received(1).RecoverExpiredHoldAsync(orderId, tenantId, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A recovery target has no due hold left — its expiry already happened in an interrupted pass — so the
    /// sweep must advance its lifecycle on the strength of the target alone.
    /// </summary>
    [Test]
    public async Task ReconciliationSweepRecoversATargetThatHasNoDueHoldLeft()
    {
        var tenantId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var lifecycles = new List<IRegistrationOrderLifecycleService>();
        var repositoryCount = 0;

        var services = new ServiceCollection();
        services.AddScoped<ITenantContextAccessor>(_ => Substitute.For<ITenantContextAccessor>());
        services.AddScoped<IRegistrationInventoryRepository>(_ =>
        {
            var repository = Substitute.For<IRegistrationInventoryRepository>();
            if (Interlocked.Increment(ref repositoryCount) == 1)
            {
                repository.GetExpiredActiveHoldsAsync(Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                    .Returns([]);
                repository.GetHoldExpiryRecoveryTargetsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
                    .Returns([new RegistrationHoldExpiryRecoveryTarget(tenantId, orderId)]);
            }

            return repository;
        });
        services.AddScoped<IRegistrationOrderLifecycleService>(_ =>
        {
            var lifecycle = SubstituteLifecycle();
            lifecycles.Add(lifecycle);
            return lifecycle;
        });

        await using ServiceProvider provider = BuildProvider(services);

        await CreateReconciliationJob(provider).Execute(CreateContext());

        await Assert.That(lifecycles).HasSingleItem();
        await lifecycles.Single().Received(1).RecoverExpiredHoldAsync(orderId, tenantId, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Each order is processed in its own scope so a pooled DbContext never carries one order's tracked
    /// state — or its failure — into the next.
    /// </summary>
    [Test]
    public async Task ReconciliationSweepUsesAFreshScopePerOrder()
    {
        RegistrationInventoryHold first = CreateHold(Guid.CreateVersion7(), Guid.CreateVersion7());
        RegistrationInventoryHold second = CreateHold(Guid.CreateVersion7(), Guid.CreateVersion7());
        var repositoryCount = 0;
        var tenantAccessors = new List<ITenantContextAccessor>();

        var services = new ServiceCollection();
        services.AddScoped<ITenantContextAccessor>(_ =>
        {
            var accessor = Substitute.For<ITenantContextAccessor>();
            tenantAccessors.Add(accessor);
            return accessor;
        });
        services.AddScoped<IRegistrationInventoryRepository>(_ =>
        {
            var repository = Substitute.For<IRegistrationInventoryRepository>();
            if (Interlocked.Increment(ref repositoryCount) == 1)
            {
                repository.GetExpiredActiveHoldsAsync(Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                    .Returns([first, second]);
                repository.GetHoldExpiryRecoveryTargetsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
                    .Returns([]);
            }
            else
            {
                repository.TryExpireDueHoldAsync(Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
                    .Returns(true);
            }

            return repository;
        });
        services.AddScoped<IRegistrationOrderLifecycleService>(_ => SubstituteLifecycle());

        await using ServiceProvider provider = BuildProvider(services);

        await CreateReconciliationJob(provider).Execute(CreateContext());

        // One discovery scope plus one per order.
        await Assert.That(repositoryCount).IsEqualTo(3);
        await Assert.That(tenantAccessors).Count().IsEqualTo(2);
        foreach (ITenantContextAccessor accessor in tenantAccessors)
        {
            accessor.Received(1).Clear();
        }
    }

    private static ServiceProvider BuildProvider(ServiceCollection services) => services.BuildServiceProvider(
        new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

    private static InventoryHoldExpiryJob CreateExpiryJob(
        IRegistrationInventoryRepository inventory,
        IRegistrationOrderLifecycleService lifecycle,
        ITenantContextAccessor tenantAccessor) => new(
            inventory,
            lifecycle,
            tenantAccessor,
            NullLogger<InventoryHoldExpiryJob>.Instance);

    private static InventoryHoldExpiryReconciliationJob CreateReconciliationJob(IServiceProvider provider) => new(
        provider,
        NullLogger<InventoryHoldExpiryReconciliationJob>.Instance);

    private static IRegistrationOrderLifecycleService SubstituteLifecycle()
    {
        var lifecycle = Substitute.For<IRegistrationOrderLifecycleService>();
        lifecycle.RecoverExpiredHoldAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(RegistrationOrderLifecycleResponseDto.Success(
                Guid.Empty,
                message: null,
                order: null)));
        return lifecycle;
    }

    private static JobDataMap PointerFor(Guid tenantId, Guid orderId)
    {
        var pointer = new JobDataMap();
        foreach (var (key, value) in InventoryHoldDeadline.PointerFor(tenantId, orderId))
        {
            pointer.Put(key, value);
        }

        return pointer;
    }

    private static IJobExecutionContext CreateContext(JobDataMap? dataMap = null)
    {
        var context = Substitute.For<IJobExecutionContext>();
        context.CancellationToken.Returns(CancellationToken.None);
        context.MergedJobDataMap.Returns(dataMap ?? []);
        return context;
    }

    private static RegistrationInventoryHold CreateHold(Guid tenantId, Guid orderId)
    {
        DateTime createdAt = DateTime.UtcNow.AddMinutes(-2);
        return RegistrationInventoryHold.Create(
            Guid.CreateVersion7(),
            orderId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            tenantId,
            quantity: 1,
            createdAt,
            createdAt.AddMinutes(1));
    }
}
