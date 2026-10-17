using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Infrastructure.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class ResolverConfigServiceTests
{
    [Test]
    public async Task GetConfigurationAsync_WhenCachedResultIsMutated_ReturnsAnIsolatedDto()
    {
        var repository = Substitute.For<ISystemSettingRepository>();
        repository.GetByKey(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((SystemSetting?)null);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var services = new ServiceCollection().BuildServiceProvider();
        var service = new ResolverConfigService(repository, cache, services);

        var first = await service.GetConfigurationAsync();
        first.PathPrefix = "/mutated";
        first.AllowTenantCustomDomains = false;
        first.ReservedSlugs = ["mutated"];

        var second = await service.GetConfigurationAsync();

        await Assert.That(ReferenceEquals(first, second)).IsFalse();
        await Assert.That(second.PathEnabled).IsTrue();
        await Assert.That(second.PathPrefix).IsEqualTo(string.Empty);
        await Assert.That(second.ReservedSlugs).Contains("admin");
        await Assert.That(second.ReservedSlugs).DoesNotContain("mutated");
        await Assert.That(second.AllowTenantCustomDomains).IsTrue();
    }
}
