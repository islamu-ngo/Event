// ABOUTME: Reproduces EF's uncached-build/global-cache interaction without contaminating the suite process.
// ABOUTME: This is the sole deliberate raw-options exception to the assembly's test construction policy.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using TUnit.Core.Executors;

namespace Event.Persistence.IntegrationTests.Database;

public sealed class EfCacheContaminationProbeTests
{
    [Test]
    [TestExecutor<FreshEfProcessExecutor>]
    public async Task TwentyCachedConfigurations_PoisonEvenAnUncachedBuild()
    {
        var caches = new List<MemoryCache>();
        try
        {
            for (int index = 0; index < 20; index++)
            {
                // Unlike ILoggerFactory in EF 10, IMemoryCache participates in provider identity.
                var cache = new MemoryCache(new MemoryCacheOptions());
                caches.Add(cache);
                using var context = new DbContext(new DbContextOptionsBuilder()
                    .UseInMemoryDatabase($"cached_poison_{index}")
                    .UseMemoryCache(cache)
                    .Options);
            }

            InvalidOperationException? failure = await Assert.That(() =>
                {
                    using var victim = new DbContext(TestDbContextOptions.Create()
                        .UseTestInMemoryDatabase("uncached_victim").Options);
                })
                .Throws<InvalidOperationException>();
            await Assert.That(failure!.Message).Contains(CoreEventId.ManyServiceProvidersCreatedWarning.Name!);
        }
        finally
        {
            foreach (MemoryCache cache in caches)
            {
                cache.Dispose();
            }
        }
    }
}
