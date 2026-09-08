using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Instance;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Models.Common;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Infrastructure.Services;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class ResolverConfigServiceTests
{
    [Test]
    public async Task ApplyConfigurationAsync_WhenOneLeafIsSupplied_WritesOnlyNormalizedKeyAndInvalidatesAfterWrite()
    {
        var repository = Substitute.For<ISystemSettingRepository>();
        var cache = Substitute.For<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
        var calls = new List<string>();
        var writes = new List<SystemSetting>();
        repository.UpsertAsync(
                Arg.Do<SystemSetting>(setting =>
                {
                    writes.Add(setting);
                    calls.Add("write");
                }),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(null));
        cache.When(value => value.Remove(Arg.Any<object>()))
            .Do(_ => calls.Add("invalidate"));
        var service = new ResolverConfigService(repository, cache);

        await service.ApplyConfigurationAsync(
            new PatchResolverConfigurationDto
            {
                PathPrefix = OptionalUpdate<string?>.Set(" tenants/ ")
            },
            new ResolverConfigurationDto
            {
                PathEnabled = true,
                PathPrefix = " tenants/ "
            },
            Guid.CreateVersion7());

        await Assert.That(writes.Select(setting => setting.SettingKey)).IsEquivalentTo([
            GovernanceSettingKeys.Routing.PathPrefix
        ]);
        await Assert.That(writes.Single().Value).IsEqualTo("\"/tenants\"");
        await Assert.That(calls).IsEquivalentTo(["write", "invalidate"]);
        cache.Received(1).Remove("ResolverConfigService.Configuration");
    }

    [Test]
    public async Task GetConfigurationAsync_WhenCachedResultIsMutated_ReturnsAnIsolatedDto()
    {
        var repository = Substitute.For<ISystemSettingRepository>();
        repository.GetByKey(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((SystemSetting?)null);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new ResolverConfigService(repository, cache);

        var first = await service.GetConfigurationAsync();
        first.PathPrefix = "/mutated";
        first.AllowTenantCustomDomains = false;

        var second = await service.GetConfigurationAsync();

        await Assert.That(ReferenceEquals(first, second)).IsFalse();
        await Assert.That(second.PathPrefix).IsEqualTo("/t");
        await Assert.That(second.AllowTenantCustomDomains).IsTrue();
    }
}
