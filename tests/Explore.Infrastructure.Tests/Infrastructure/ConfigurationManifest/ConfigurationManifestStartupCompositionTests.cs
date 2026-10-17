namespace Explore.Infrastructure.Tests.Infrastructure.ConfigurationManifest;

using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Managed;
using Explore.Infrastructure.ConfigurationManifest;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

public sealed class ConfigurationManifestStartupCompositionTests
{
    [Test]
    public async Task DeferredStartupGraph_ResolvesWithoutRuntimeEffectServices()
    {
        var services = new ServiceCollection();
        IConfiguration configuration = new ConfigurationBuilder().Build();
        services.AddSingleton(configuration);
        services.AddLogging();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton(Substitute.For<IHostApplicationLifetime>());
        services.AddSingleton<IGlobalExploreDbContextFactory>(new GlobalExploreDbContextFactory(
            new PrimaryDatabaseConnectionOptions
            {
                Role = PrimaryDatabaseRole.Global,
                Provider = PrimaryDatabaseProvider.Sqlite,
                Database = Path.Combine(Path.GetTempPath(), $"manifest-startup-{Guid.CreateVersion7():N}.db")
            }));
        services.AddSingleton(new ExploreDbContext(
            new DbContextOptionsBuilder<ExploreDbContext>().Options));
        services.AddSingleton(
            Substitute.For<IDbContextFactory<ExploreDbContext>>());
        services.AddConfigurationManifestPersistence();
        services.AddConfigurationManifestStartup(
            configuration,
            ConfigurationManifestEffectDeliveryMode.DeferredToRuntime);

        await using ServiceProvider provider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
        await using AsyncServiceScope scope = provider.CreateAsyncScope();

        var runner = scope.ServiceProvider
            .GetRequiredService<Explore.Application.Features.ConfigurationManifest.Application.IConfigurationManifestStartupRunner>();
        var applier = scope.ServiceProvider
            .GetRequiredService<IConfigurationManifestApplier>();
        var effects = scope.ServiceProvider
            .GetRequiredService<IConfigurationManifestEffectDeliveryStrategy>();

        await Assert.That(runner.GetType().Assembly)
            .IsSameReferenceAs(typeof(ConfigurationManifestPersistenceServicesRegistration).Assembly);
        await Assert.That(runner.GetType().IsNotPublic).IsTrue();
        await Assert.That(runner.GetType().IsSealed).IsTrue();
        var bootstrapOwner = scope.ServiceProvider.GetRequiredService<IConfigurationManifestBootstrapOwnerFactory>();
        await Assert.That(bootstrapOwner.GetType().IsNotPublic).IsTrue();
        await Assert.That(bootstrapOwner.GetType().IsSealed).IsTrue();
        await runner.RunAsync(CancellationToken.None);
        await Assert.That(applier).IsTypeOf<ConfigurationManifestApplier>();
        await Assert.That(effects)
            .IsTypeOf<DeferredConfigurationManifestEffectDelivery>();
        await Assert.That(scope.ServiceProvider
                .GetService<IConfigurationManifestEffectDispatcher>())
            .IsNull();
        await Assert.That(scope.ServiceProvider
                .GetService<IConfigurationImportEffectDelivery>())
            .IsNull();
        await Assert.That(scope.ServiceProvider
                .GetService<IHumanConfigurationImportAdmission>())
            .IsNull();
        await Assert.That(scope.ServiceProvider
                .GetService<IConfigurationDirectTransferChunkStore>())
            .IsNull();
    }
}
