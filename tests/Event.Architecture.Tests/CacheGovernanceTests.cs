namespace Event.Architecture.Tests;

using Explore.API.Extensions;
using Explore.Application.Caching;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

/// <summary>
/// Verifies cache telemetry configuration, discovery boundaries and tenant-tag isolation.
/// </summary>
public sealed class CacheGovernanceTests
{
    [Test]
    public async Task HybridCache_TagMetrics_RemainDisabledInApiRegistration()
    {
        await using var provider = CreateProvider();

        await Assert.That(provider.GetRequiredService<IOptions<HybridCacheOptions>>().Value.ReportTagMetrics)
            .IsFalse()
            .Because("raw cache tags can contain tenant, user and filter values; tag-metric dimensions must remain disabled for every family");
    }

    [Test]
    public async Task PublicDiscovery_DoesNotDependOnHybridCache()
    {
        Type[] publicReaders =
        [
            typeof(Explore.Application.Features.Events.Handlers.Queries.GetEventListRequestHandler),
            typeof(Explore.Application.Features.Events.Discovery.EventDiscoveryLocalSource),
            typeof(Explore.Application.Features.Events.Discovery.EventDiscoveryTraversal)
        ];
        var cacheDependencies = publicReaders.SelectMany(type => type.GetConstructors())
            .SelectMany(constructor => constructor.GetParameters())
            .Where(parameter => typeof(HybridCache).IsAssignableFrom(parameter.ParameterType)).ToArray();
        await Assert.That(cacheDependencies).IsEmpty();
    }

    [Test]
    public async Task EventListInvalidation_EvictsOnlyTheSelectedTenant()
    {
        await using var provider = CreateProvider();
        var cache = provider.GetRequiredService<HybridCache>();
        var firstTenant = Guid.CreateVersion7();
        var secondTenant = Guid.CreateVersion7();
        var firstKey = $"events:list:tenant:{firstTenant:N}";
        var secondKey = $"events:list:tenant:{secondTenant:N}";
        await cache.SetAsync(firstKey, "first-original", tags: [CacheTags.EventListByTenant(firstTenant)]);
        await cache.SetAsync(secondKey, "second-original", tags: [CacheTags.EventListByTenant(secondTenant)]);

        await cache.RemoveByTagAsync(CacheTags.EventListByTenant(firstTenant));

        var first = await cache.GetOrCreateAsync(firstKey,
            static _ => ValueTask.FromResult("first-refreshed"), tags: [CacheTags.EventListByTenant(firstTenant)]);
        var second = await cache.GetOrCreateAsync(secondKey,
            static _ => ValueTask.FromResult("second-refreshed"), tags: [CacheTags.EventListByTenant(secondTenant)]);
        await Assert.That(first).IsEqualTo("first-refreshed");
        await Assert.That(second).IsEqualTo("second-original");
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApiCaching();
        return services.BuildServiceProvider();
    }
}
