// Controls which modules are available to tenants and provides discovery endpoints.

namespace Explore.Infrastructure.Services;

using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Domain.Modules;
using Microsoft.Extensions.Caching.Memory;

/// <summary>
/// Service for module governance and discovery with caching.
/// </summary>
public class ModuleService : IModuleService
{
    private readonly IModuleDefinitionRepository _moduleDefinitionRepository;
    private readonly ITenantCapabilityRepository _tenantCapabilityRepository;
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(5);

    private const string AllModulesCacheKey = "Modules_All";
    private const string TenantModulesCacheKeyPrefix = "Modules_Tenant_";

    public ModuleService(
        IModuleDefinitionRepository moduleDefinitionRepository,
        ITenantCapabilityRepository tenantCapabilityRepository,
        IMemoryCache cache)
    {
        _moduleDefinitionRepository = moduleDefinitionRepository;
        _tenantCapabilityRepository = tenantCapabilityRepository;
        _cache = cache;
    }

    public async Task<IReadOnlyList<ModuleInfo>> GetAllModulesAsync(CancellationToken cancellationToken = default)
    {
        return await _cache.GetOrCreateAsync(AllModulesCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _cacheExpiration;
            var modules = await _moduleDefinitionRepository.GetAllActive();
            return modules.Select(m => MapToModuleInfo(m)).ToList();
        }) ?? new List<ModuleInfo>();
    }

    public async Task<IReadOnlyList<ModuleInfo>> GetEnabledModulesAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{TenantModulesCacheKeyPrefix}{tenantId}";

        return await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _cacheExpiration;

            var capabilities = await _tenantCapabilityRepository.GetEnabledByTenantId(tenantId);
            return capabilities
                .Where(c => c.Module != null)
                .Select(c => MapToModuleInfo(c.Module!, isEnabledForTenant: true))
                .ToList();
        }) ?? new List<ModuleInfo>();
    }

    public async Task<bool> IsModuleEnabledAsync(Guid tenantId, string moduleKey, CancellationToken cancellationToken = default)
    {
        // Use cached enabled modules for the tenant
        var enabledModules = await GetEnabledModulesAsync(tenantId, cancellationToken);
        return enabledModules.Any(m => m.ModuleKey.Equals(moduleKey, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<string?> GetModuleWizardSchemaUrlAsync(string moduleKey, CancellationToken cancellationToken = default)
    {
        var module = await _moduleDefinitionRepository.GetByKey(moduleKey, cancellationToken);
        return module?.WizardSchemaUrl;
    }

    public async Task<bool> EnableModuleAsync(Guid tenantId, string moduleKey, Guid? enabledBy = null, CancellationToken cancellationToken = default)
    {
        // Check if module exists and is active
        var module = await _moduleDefinitionRepository.GetByKey(moduleKey, cancellationToken);
        if (module == null || !module.IsActive)
            return false;

        // Check if capability already exists
        var existing = await _tenantCapabilityRepository.GetByTenantAndModuleKey(tenantId, moduleKey, cancellationToken);
        if (existing != null)
        {
            // Re-enable if disabled
            if (!existing.IsEnabled)
            {
                existing.IsEnabled = true;
                existing.EnabledAt = DateTime.UtcNow;
                existing.EnabledBy = enabledBy;
                await _tenantCapabilityRepository.Update(existing, cancellationToken);
                InvalidateCache(tenantId);
            }
            return true;
        }

        // Create new capability
        var capability = new TenantCapability
        {
            TenantId = tenantId,
            Tenant = null!,
            ModuleId = module.Id,
            Module = null!,
            IsEnabled = true,
            EnabledAt = DateTime.UtcNow,
            EnabledBy = enabledBy
        };

        await _tenantCapabilityRepository.Create(capability, cancellationToken);
        InvalidateCache(tenantId);
        return true;
    }

    public async Task<bool> DisableModuleAsync(Guid tenantId, string moduleKey, CancellationToken cancellationToken = default)
    {
        var capability = await _tenantCapabilityRepository.GetByTenantAndModuleKey(tenantId, moduleKey, cancellationToken);
        if (capability == null)
            return false;

        // Soft disable - keep the record but mark as disabled
        capability.IsEnabled = false;
        await _tenantCapabilityRepository.Update(capability, cancellationToken);
        InvalidateCache(tenantId);
        return true;
    }

    public void InvalidateCache(Guid? tenantId = null)
    {
        _cache.Remove(AllModulesCacheKey);

        if (tenantId.HasValue)
        {
            _cache.Remove($"{TenantModulesCacheKeyPrefix}{tenantId}");
        }
    }

    private static ModuleInfo MapToModuleInfo(ModuleDefinition module, bool? isEnabledForTenant = null)
    {
        return new ModuleInfo
        {
            Id = module.Id,
            ModuleKey = module.ModuleKey,
            Name = module.Name,
            Description = module.Description,
            IconName = module.IconName,
            Category = module.Category,
            DisplayOrder = module.DisplayOrder,
            WizardSchemaUrl = module.WizardSchemaUrl,
            IsActive = module.IsActive,
            IsEnabledForTenant = isEnabledForTenant
        };
    }
}
