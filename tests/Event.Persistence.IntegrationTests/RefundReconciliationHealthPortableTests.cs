using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Infrastructure.HealthChecks;
using Explore.Persistence;
using Explore.Persistence.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Event.Persistence.IntegrationTests;

public sealed class RefundReconciliationHealthPortableTests
{
    private static readonly DateTime ObservedAt = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PortableReadinessUsesFreshGlobalContextRatherThanRequestTenant(bool boundRequest)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
        await connection.OpenAsync();
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(connection).UseSnakeCaseNamingConvention().Options;
        Guid tenantA = Guid.CreateVersion7(), tenantB = Guid.CreateVersion7();
        await using (var seed = new ExploreDbContext(options))
        {
            await seed.Database.EnsureCreatedAsync();
            RefundReconciliationHealthNativeTests.SeedRows(seed, tenantA, tenantB);
            await seed.SaveChangesAsync();
        }
        await using var request = new ExploreDbContext(options)
        {
            TenantContext = boundRequest ? new TenantIdentity(tenantA) : null
        };
        await Assert.That(await request.RefundAttempts.CountAsync(row => row.TenantId == tenantB)).IsEqualTo(0);
        var factory = new PortableGlobalFactory(options);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IGlobalExploreDbContextFactory>(factory);
        services.AddSingleton<TimeProvider>(new FixedClock());
        services.AddScoped(_ => request);
        services.AddRefundReconciliationHealthPersistence();
        services.AddHealthChecks().AddCheck<RefundReconciliationHealthCheck>(
            "refund-reconciliation", HealthStatus.Unhealthy, ["ready"]);
        await using var provider = services.BuildServiceProvider();
        HealthReport report = await provider.GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(registration => registration.Tags.Contains("ready"));
        await Assert.That(report.Status).IsEqualTo(HealthStatus.Degraded);
        var health = await provider.GetRequiredService<IRefundReconciliationHealthObserver>()
            .GetReconciliationHealthAsync(ObservedAt, CancellationToken.None);
        await Assert.That(health).IsEqualTo(new RefundReconciliationHealth(
            3, 1, 1, 2, 1, 2, 1, 1, ObservedAt.AddMinutes(-30)));
        await Assert.That(request.TenantFilterTenantId).IsEqualTo(boundRequest ? tenantA : (Guid?)null);
        await Assert.That(request.IsTenantFilterBypassed).IsFalse();
    }

    [Test]
    public async Task TenantBoundFactoryCannotMasqueradeAsFleetObserver()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
        await connection.OpenAsync();
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(connection).UseSnakeCaseNamingConvention().Options;
        Guid tenant = Guid.CreateVersion7();
        await using (var seed = new ExploreDbContext(options))
        {
            await seed.Database.EnsureCreatedAsync();
            RefundReconciliationHealthNativeTests.SeedRows(seed, tenant, Guid.CreateVersion7());
            await seed.SaveChangesAsync();
        }
        var services = new ServiceCollection();
        services.AddSingleton<IGlobalExploreDbContextFactory>(new PortableGlobalFactory(options, tenant));
        services.AddRefundReconciliationHealthPersistence();
        await using var provider = services.BuildServiceProvider();
        var check = new RefundReconciliationHealthCheck(
            provider.GetRequiredService<IServiceScopeFactory>(), new FixedClock());
        HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext());
        await Assert.That(result.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(result.Data.Count).IsEqualTo(0);
    }

    private sealed class PortableGlobalFactory(DbContextOptions<ExploreDbContext> options, Guid? invalidTenant = null)
        : IGlobalExploreDbContextFactory
    {
        public Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExploreDbContext(options)
            {
                TenantContext = invalidTenant is { } tenant ? new TenantIdentity(tenant) : null
            });
    }

    private sealed record TenantIdentity(Guid TenantId) : ITenantContext;
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(ObservedAt);
    }
}
