using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

public sealed class RegistrationRetentionUnsupportedProviderTests
{
    [Test]
    public async Task ModelOnlyProviderDoesNotReportSuccessfulEmptyDiscoveryOrCleanup()
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseInMemoryDatabase("retention-" + Guid.CreateVersion7()).Options;
        await AssertUnsupportedAsync(options);
    }

    [Test]
    public async Task PortableRelationalDiscoveryRetainsActiveEligibilityAndTenantSelection()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = TestDbContextOptions.Create<ExploreDbContext>().UseSqlite(connection)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(SqliteNamedLockTransactionInterceptor.Instance,
                SqliteProjectionLockTransactionInterceptor.Instance).Options;
        await using (var setup = new ExploreDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.BindTenantLease();
            setup.TenantStatuses.AddRange(
                new TenantStatus { Id = 1, MasterCode = "ACTIVE", FullName = "Active", IsActiveState = true },
                new TenantStatus { Id = 2, MasterCode = "SUSPENDED", FullName = "Suspended", IsActiveState = false });
            Guid active = Guid.CreateVersion7(), suspended = Guid.CreateVersion7();
            setup.Tenants.AddRange(
                new Tenant { Id = active, FullName = "Active tenant", Slug = $"active-{active:N}", TenantStatusId = 1, TenantStatus = null! },
                new Tenant { Id = suspended, FullName = "Suspended tenant", Slug = $"suspended-{suspended:N}", TenantStatusId = 2, TenantStatus = null! });
            await using (var gate = new EventDiscoveryEnrollmentGate(setup))
            {
                await using var custody = await gate.AcquireAsync();
                await setup.SaveChangesAsync();
            }
            var factory = new UnsupportedContextFactory(options);
            var discovery = new RegistrationRetentionTenantDiscoveryRepository(factory, null);
            await Assert.That(await discovery.GetTenantIdsAsync(null, 1, CancellationToken.None)).IsEquivalentTo([active]);
            await Assert.That((await discovery.GetTenantIdsAsync(active, 1, CancellationToken.None)).Count).IsEqualTo(0);
            await using var scope = await new RegistrationRetentionExecutionScopeFactory(factory)
                .CreateAsync(active, CancellationToken.None);
            var result = await scope.CleanupAsync(new DateTime(2040, 1, 1, 0, 0, 0, DateTimeKind.Utc), 1, CancellationToken.None);
            await Assert.That(result.TotalDeleted).IsEqualTo(0);
            await Assert.That(await setup.Tenants.CountAsync()).IsEqualTo(2);
        }
    }

    private static async Task AssertUnsupportedAsync(DbContextOptions<ExploreDbContext> options)
    {
        var factory = new UnsupportedContextFactory(options);
        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await new RegistrationRetentionTenantDiscoveryRepository(factory, null)
                .GetTenantIdsAsync(null, 1, CancellationToken.None));
        await Assert.ThrowsAsync<NotSupportedException>(async () =>
        {
            await using var scope = await new RegistrationRetentionExecutionScopeFactory(factory)
                .CreateAsync(Guid.CreateVersion7(), CancellationToken.None);
            await scope.CleanupAsync(new DateTime(2040, 1, 1, 0, 0, 0, DateTimeKind.Utc), 1, CancellationToken.None);
        });
    }

    private sealed class UnsupportedContextFactory(DbContextOptions<ExploreDbContext> options)
        : IGlobalExploreDbContextFactory, ITenantExploreDbContextFactory
    {
        public Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            var context = new ExploreDbContext(options);
            context.BindTenantLease();
            return Task.FromResult(context);
        }

        public Task<ExploreDbContext> CreateDbContextAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExploreDbContext(options) { TenantContext = new TenantIdentity(tenantId) });
    }

    private sealed record TenantIdentity(Guid TenantId) : ITenantContext;
}
