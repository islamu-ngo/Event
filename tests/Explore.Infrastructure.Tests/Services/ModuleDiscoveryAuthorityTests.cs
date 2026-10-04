using Explore.Application.Contracts.Persistence;
using Explore.Domain.Modules;
using Explore.Infrastructure.Services;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;
using TUnit.Assertions;
using TUnit.Core;

namespace Explore.Infrastructure.Tests.Services;

public sealed class ModuleDiscoveryAuthorityTests
{
    [Test]
    [Arguments("Mod_Islamic")]
    [Arguments("mod_islamic")]
    public async Task Predicate_selection_ignores_a_warm_module_menu_cache_after_revocation(string moduleKey)
    {
        Guid tenantId = Guid.CreateVersion7();
        var definitions = Substitute.For<IModuleDefinitionRepository>();
        var capabilities = Substitute.For<ITenantCapabilityRepository>();
        bool enabled = true;
        var module = new ModuleDefinition
        {
            Id = Guid.CreateVersion7(), ModuleKey = "Mod_Islamic", Name = "Islamic", IsActive = true
        };
        capabilities.GetEnabledByTenantId(tenantId).Returns(_ => enabled
            ? new List<TenantCapability>
            {
                new() { TenantId = tenantId, Tenant = null!, ModuleId = module.Id, Module = module, IsEnabled = true }
            }
            : []);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new ModuleService(definitions, capabilities, cache);
        await Assert.That((await service.GetEnabledModulesAsync(tenantId)).Count).IsEqualTo(1);
        await Assert.That(await service.IsModuleEnabledAsync(tenantId, moduleKey)).IsTrue();

        enabled = false;

        await Assert.That(await service.IsModuleEnabledAsync(tenantId, moduleKey)).IsFalse();
    }
}
