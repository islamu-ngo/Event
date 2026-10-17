using Explore.Application.Contracts.Recovery;
using Explore.Infrastructure.Recovery;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Event.Persistence.IntegrationTests;

public sealed class TicketingRecoveryHealthPortableTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RegisteredReadinessUsesActualSeparateGlobalPool(bool boundRequest)
    {
        string path = Path.Combine(Path.GetTempPath(), $"recovery-health-{Guid.CreateVersion7():N}.db");
        var options = new PrimaryDatabaseConnectionOptions
        {
            Role = PrimaryDatabaseRole.Global,
            Provider = PrimaryDatabaseProvider.Sqlite,
            Database = path
        };
        try
        {
            var builder = TestDbContextOptions.Create<ExploreDbContext>();
            PrimaryDatabaseProviderComposition.ConfigureApplication(builder, options);
            Guid tenantA = Guid.CreateVersion7(), tenantB = Guid.CreateVersion7();
            await using (var seed = new ExploreDbContext(builder.Options))
            {
                await seed.Database.EnsureCreatedAsync();
                await seed.Database.OpenConnectionAsync();
                await seed.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");
                await TicketingRecoveryHealthTestData.SeedAsync(seed, tenantA, tenantB, unknown: false);
                await seed.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON");
            }
            await using var request = new ExploreDbContext(builder.Options)
            {
                TenantContext = boundRequest ? new TicketingRecoveryHealthTestData.TenantIdentity(tenantA) : null
            };
            await Assert.That(await request.TicketingRecoveryCheckpoints
                .CountAsync(row => row.TenantId == tenantB)).IsEqualTo(0);
            var factory = new GlobalExploreDbContextFactory(options, poolSize: 1);
            await using var services = TicketingRecoveryHealthTestData.Services(factory, request);
            var report = await services.GetRequiredService<HealthCheckService>()
                .CheckHealthAsync(registration => registration.Tags.Contains("ready"));
            var entry = report.Entries[TicketingRecoveryHealthCheck.Name];
            await Assert.That(entry.Status).IsEqualTo(HealthStatus.Unhealthy);
            await Assert.That(entry.Data["pending_reissues"]).IsEqualTo(2);
            await Assert.That(entry.Data["poison_effects"]).IsEqualTo(1);
            await Assert.That(entry.Data["oldest_due_age_seconds"]).IsEqualTo(30d);
            await Assert.That(entry.Data.Keys).IsEquivalentTo(TicketingRecoveryHealthCheck.DataKeys);
            var health = await services.GetRequiredService<ITicketingRecoveryHealthObserver>()
                .GetAggregateHealthAsync(CancellationToken.None);
            await Assert.That(health).IsEqualTo(new TicketingRecoveryAggregateHealth(
                1, 1, 2, 0, 2, 1, TicketingRecoveryHealthTestData.ObservedAt.AddSeconds(-30)));
            await Assert.That(request.TenantFilterTenantId).IsEqualTo(boundRequest ? tenantA : (Guid?)null);
            await Assert.That(request.IsTenantFilterBypassed).IsFalse();
        }
        finally
        {
            using var cleanup = new Microsoft.Data.Sqlite.SqliteConnection(
                PrimaryDatabaseConfiguration.BuildConnectionString(options).ConnectionString);
            Microsoft.Data.Sqlite.SqliteConnection.ClearPool(cleanup);
            File.Delete(path);
        }
    }

    [Test]
    public async Task MissingObservationAuthorityIsUnhealthyUnlessControlsAreDisabled()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.Configure<Explore.Secrets.Configuration.TicketingRecoveryOperatorOptions>(options => options.Enabled = true);
        services.AddHealthChecks().AddCheck<TicketingRecoveryHealthCheck>(TicketingRecoveryHealthCheck.Name);
        await using var provider = services.BuildServiceProvider();
        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();
        await Assert.That(report.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(report.Entries[TicketingRecoveryHealthCheck.Name].Data.Count).IsEqualTo(0);
        var disabled = new TicketingRecoveryHealthCheck(null!,
            Microsoft.Extensions.Options.Options.Create(new Explore.Secrets.Configuration.TicketingRecoveryOperatorOptions()),
            TimeProvider.System);
        await Assert.That((await disabled.CheckHealthAsync(new HealthCheckContext())).Status).IsEqualTo(HealthStatus.Healthy);
    }

    [Test]
    public async Task TenantBoundLeaseCannotMasqueradeAsGlobalFleetAuthority()
    {
        await using var services = TicketingRecoveryHealthTestData.Services(new BoundFactory());
        var report = await services.GetRequiredService<HealthCheckService>().CheckHealthAsync();
        await Assert.That(report.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(report.Entries[TicketingRecoveryHealthCheck.Name].Data.Count).IsEqualTo(0);
    }

    private sealed class BoundFactory : IGlobalExploreDbContextFactory
    {
        public Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExploreDbContext(TestDbContextOptions.Create<ExploreDbContext>()
                .UseSqlite("Data Source=:memory:").Options)
            {
                TenantContext = new TicketingRecoveryHealthTestData.TenantIdentity(Guid.CreateVersion7())
            });
    }
}
