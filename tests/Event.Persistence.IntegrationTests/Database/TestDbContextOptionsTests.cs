// ABOUTME: Exercises the assembly-wide options boundary through raw, scoped, factory and pooled EF entry points.
// ABOUTME: Guards shared InMemory stores, service replacements, schema diversity and child-process failure propagation.

using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Schema;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Caching.Memory;
using TUnit.Core;
using TUnit.Core.Executors;

namespace Event.Persistence.IntegrationTests.Database;

public sealed class TestDbContextOptionsTests
{
    [Test]
    public async Task MixedRawAndDiConfigurations_DoNotPoisonUncachedProviders()
    {
        for (int index = 0; index < 24; index++)
        {
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var rawOptions = TestDbContextOptions.Create();
            PrimaryDatabaseProviderComposition.ConfigureApplication(rawOptions, new PrimaryDatabaseConnectionOptions
            {
                Provider = PrimaryDatabaseProvider.PostgreSql,
                Role = PrimaryDatabaseRole.Runtime,
                Host = "localhost",
                Database = "policy_model_only",
                Schema = $"policy_schema_{index}",
                Username = "unused",
                Password = "unused",
                TlsMode = PrimaryDatabaseTlsMode.Disabled,
            });
            using var raw = new DbContext(rawOptions.Options);
            await AssertIsolatedAsync(raw);
            await Assert.That(raw.GetService<IMigrationsSqlGenerator>())
                .IsTypeOf<ConfigurableNpgsqlMigrationsSqlGenerator>();

            var services = new ServiceCollection();
            services.AddDbContext<ScopedProbeContext>(options => options
                .UseSqlite("Data Source=:memory:")
                .UseMemoryCache(cache));
            services.AddDbContextFactory<FactoryProbeContext>(options => options
                .UseSqlite("Data Source=:memory:")
                .UseMemoryCache(cache));
            services.AddPooledDbContextFactory<PooledProbeContext>(options => options
                .UseTestInMemoryDatabase($"policy_pool_{index}")
                .UseMemoryCache(cache));
            using var provider = services.BuildIsolatedServiceProvider(validateScopes: true);
            using var scope = provider.CreateScope();
            await AssertIsolatedAsync(scope.ServiceProvider.GetRequiredService<ScopedProbeContext>());
            using var factoryContext = provider.GetRequiredService<IDbContextFactory<FactoryProbeContext>>().CreateDbContext();
            await AssertIsolatedAsync(factoryContext);
            using var pooledContext = provider.GetRequiredService<IDbContextFactory<PooledProbeContext>>().CreateDbContext();
            await AssertIsolatedAsync(pooledContext);
        }

        using var sentinel = new DbContext(TestDbContextOptions.Create()
            .UseTestInMemoryDatabase("policy_final_uncached_probe").Options);
        await AssertIsolatedAsync(sentinel);
    }

    [Test]
    public async Task InMemoryStores_AreSharedOnlyByExplicitTestOwnedRoots()
    {
        var root = new InMemoryDatabaseRoot();
        DbContextOptions<StoreProbeContext> options = TestDbContextOptions.Create<StoreProbeContext>()
            .UseTestInMemoryDatabase("shared", root).Options;
        using (var writer = new StoreProbeContext(options))
        {
            writer.Rows.Add(new ProbeRow { Id = 1 });
            await writer.SaveChangesAsync();
        }

        using var sameOptions = new StoreProbeContext(options);
        using var sameRoot = new StoreProbeContext(TestDbContextOptions.Create<StoreProbeContext>()
            .UseTestInMemoryDatabase("shared", root).Options);
        using var differentRoot = new StoreProbeContext(TestDbContextOptions.Create<StoreProbeContext>()
            .UseTestInMemoryDatabase("shared").Options);
        await Assert.That(await sameOptions.Rows.CountAsync()).IsEqualTo(1);
        await Assert.That(await sameRoot.Rows.CountAsync()).IsEqualTo(1);
        await Assert.That(await differentRoot.Rows.CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [TestExecutor<FreshEfProcessExecutor>]
    public async Task FreshProcessExecutor_SelectsExactParameterizedNode(int argument)
    {
        TestContext context = TestContext.Current!;
        await Assert.That(Environment.GetEnvironmentVariable(FreshEfProcessExecutor.ChildTestIdVariable))
            .IsEqualTo(context.Metadata.TestDetails.Identity.TestId);
        await Assert.That(Environment.GetEnvironmentVariable(FreshEfProcessExecutor.ParentProcessIdVariable))
            .IsNotEqualTo(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await Assert.That((int)context.Metadata.TestDetails.Method.TestMethodArguments[0]!).IsEqualTo(argument);
    }

    [Test]
    public async Task FreshProcessExecutor_PropagatesRealTestFailure()
    {
        if (Environment.GetEnvironmentVariable(FreshEfProcessExecutor.ChildTestIdVariable) is not null)
        {
            throw new InvalidOperationException("EF_ISOLATION_FAILURE_PROBE");
        }

        TestContext context = TestContext.Current!;
        IsolatedEfTestException? failure = await Assert.That(() => FreshEfProcessExecutor.RunAsync(
                context.Metadata.TestDetails.Identity.TestId, context.Execution.CancellationToken))
            .Throws<IsolatedEfTestException>();
        await Assert.That(failure!.ExitCode).IsEqualTo(2);
        await Assert.That(failure.Message).Contains("EF_ISOLATION_FAILURE_PROBE");
    }

    [Test]
    public async Task DiRegistrations_CannotOverrideIsolationOrLoseSharedStoreState()
    {
        var root = new InMemoryDatabaseRoot();
        var services = new ServiceCollection();
        services.AddDbContext<StoreProbeContext>(options => options
            .UseTestInMemoryDatabase("di_policy_store", root)
            .EnableServiceProviderCaching(true)
            .ConfigureWarnings(warnings => warnings.Log(CoreEventId.ManyServiceProvidersCreatedWarning)));

        using var provider = services.BuildIsolatedServiceProvider(validateScopes: true);
        using (var scope = provider.CreateScope())
        {
            var writer = scope.ServiceProvider.GetRequiredService<StoreProbeContext>();
            await AssertIsolatedAsync(writer);
            writer.Rows.Add(new ProbeRow { Id = 29 });
            await writer.SaveChangesAsync();
        }

        using var readerScope = provider.CreateScope();
        var reader = readerScope.ServiceProvider.GetRequiredService<StoreProbeContext>();
        await AssertIsolatedAsync(reader);
        await Assert.That((await reader.Rows.SingleAsync()).Id).IsEqualTo(29);
    }

    private static async Task AssertIsolatedAsync(DbContext context)
    {
        CoreOptionsExtension core = context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()!;
        await Assert.That(core.ServiceProviderCachingEnabled).IsFalse();
        await Assert.That(core.WarningsConfiguration.GetBehavior(CoreEventId.ManyServiceProvidersCreatedWarning))
            .IsEqualTo(WarningBehavior.Throw);
    }

    public sealed class ScopedProbeContext(DbContextOptions<ScopedProbeContext> options) : DbContext(options);
    public sealed class FactoryProbeContext(DbContextOptions<FactoryProbeContext> options) : DbContext(options);
    public sealed class PooledProbeContext(DbContextOptions<PooledProbeContext> options) : DbContext(options);
    public sealed class StoreProbeContext(DbContextOptions<StoreProbeContext> options) : DbContext(options)
    {
        public DbSet<ProbeRow> Rows => Set<ProbeRow>();
    }
    public sealed class ProbeRow
    {
        public int Id { get; set; }
    }
}
