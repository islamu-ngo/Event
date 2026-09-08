// ABOUTME: Verifies the one-shot migration host can resolve only the manifest startup dependency graph.
// ABOUTME: Proves deferred effects remain durable without loading cache resolvers or unrelated runtime services.

namespace Explore.Infrastructure.Tests.Infrastructure.ConfigurationManifest;

using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Handlers.Commands;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Managed;
using Explore.Infrastructure.ConfigurationManifest;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
            .GetRequiredService<IConfigurationManifestStartupRunner>();
        var applier = scope.ServiceProvider
            .GetRequiredService<IConfigurationManifestApplier>();
        var effects = scope.ServiceProvider
            .GetRequiredService<IConfigurationManifestEffectDeliveryStrategy>();

        await Assert.That(runner).IsTypeOf<ConfigurationManifestStartupRunner>();
        await Assert.That(applier).IsTypeOf<ApplyConfigurationManifestCommandHandler>();
        await Assert.That(effects)
            .IsTypeOf<DeferredConfigurationManifestEffectDelivery>();
        await Assert.That(scope.ServiceProvider
                .GetService<IConfigurationManifestEffectDispatcher>())
            .IsNull();
        await Assert.That(scope.ServiceProvider
                .GetService<IConfigurationImportEffectDelivery>())
            .IsNull();
        await Assert.That(scope.ServiceProvider
                .GetService<IConfigurationDirectTransferChunkStore>())
            .IsNull();
    }
}
