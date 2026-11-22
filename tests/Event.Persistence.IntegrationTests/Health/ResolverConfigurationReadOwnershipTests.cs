using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NSubstitute;

namespace Event.Persistence.IntegrationTests.Health;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class ResolverConfigurationReadOwnershipTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task PublicResolverBootstrapReadsStoredRoutingWithoutPromotingRuntime()
    {
        await fixture.ResetAsync();
        string key = GovernanceSettingKeys.Routing.PathPrefix;
        await using var migration = fixture.CreateDbContext();
        migration.BindTenantLease();
        var original = await migration.SystemSettings.AsNoTracking()
            .SingleOrDefaultAsync(row => row.SettingKey == key);
        try
        {
            await migration.SystemSettings.Where(row => row.SettingKey == key).ExecuteDeleteAsync();
            var stored = new SystemSetting
            {
                Id = Guid.CreateVersion7(),
                SettingKey = key,
                Value = JsonSerializer.Serialize(" /original/ "),
                ValueType = SettingValueType.String,
                CreatedAt = DateTime.UtcNow
            };
            migration.SystemSettings.Add(stored);
            await migration.SaveChangesAsync();
            var options = TestDbContextOptions.Create<ExploreDbContext>();
            PrimaryDatabaseProviderComposition.ConfigureApplication(options,
                fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime));
            options.AddInterceptors(PostgresTenantSessionInterceptor.RuntimeInstance);
            await using var runtime = new ExploreDbContext(options.Options);
            runtime.BindTenantLease();
            var services = new ServiceCollection();
            services.AddMemoryCache();
            services.AddSingleton<ISystemSettingRepository>(
                SystemSettingRepository.CreateReadOnly(runtime, Substitute.For<ISettingMutationLock>()));
            services.AddScoped<IInstanceSystemSettingsReader>(_ => new InstanceSystemSettingsReader(
                new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global)),
                fixture.NativeQueueEnrollment));
            services.AddScoped<IResolverConfigService, ResolverConfigService>();
            await using var provider = services.BuildServiceProvider();
            var resolver = provider.GetRequiredService<IResolverConfigService>();
            var token = TestContext.Current!.Execution.CancellationToken;

            var first = await resolver.GetConfigurationAsync(token);
            await Assert.That(first.PathPrefix).IsEqualTo("/original");
            await Assert.That(first.HeaderEnabled).IsTrue();
            first.PathPrefix = "/mutated";
            first.ReservedSlugs = ["mutated"];
            var cached = await resolver.GetConfigurationAsync(token);
            await Assert.That(cached.PathPrefix).IsEqualTo("/original");
            await Assert.That(cached.ReservedSlugs).Contains("admin");
            await Assert.That(cached.ReservedSlugs).DoesNotContain("mutated");
            stored.Value = JsonSerializer.Serialize(" /current/ ");
            await migration.SaveChangesAsync();
            resolver.InvalidateCache();
            await Assert.That((await resolver.GetConfigurationAsync(token)).PathPrefix).IsEqualTo("/current");
            await Assert.That(runtime.TenantFilterTenantId).IsNull();
            await Assert.That(runtime.ChangeTracker.Entries()).IsEmpty();
            string table = new HumanPrincipalSourceSql(runtime).Table(typeof(SystemSetting));
            var denial = await Assert.ThrowsAsync<PostgresException>(async () =>
                await runtime.Database.ExecuteSqlRawAsync($"SELECT * FROM {table} LIMIT 0", token));
            await Assert.That(denial.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        }
        finally
        {
            migration.ChangeTracker.Clear();
            await migration.SystemSettings.Where(row => row.SettingKey == key).ExecuteDeleteAsync();
            if (original is not null)
                migration.SystemSettings.Add(original);
            await migration.SaveChangesAsync();
        }
    }
}
