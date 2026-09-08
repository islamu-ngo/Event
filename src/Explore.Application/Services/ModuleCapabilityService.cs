using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Domain.Modules;

namespace Explore.Application.Services;

public class ModuleCapabilityService : IModuleCapabilityService
{
    private const string CoreModuleKey = "Mod_Core";
    private const string IslamicModuleKey = "Mod_Islamic";
    private const string TechModuleKey = "Mod_Tech";

    private readonly ITenantCapabilityRepository _tenantCapabilityRepository;
    private readonly IModuleDefinitionRepository _moduleDefinitionRepository;

    public ModuleCapabilityService(
        ITenantCapabilityRepository tenantCapabilityRepository,
        IModuleDefinitionRepository moduleDefinitionRepository)
    {
        _tenantCapabilityRepository = tenantCapabilityRepository;
        _moduleDefinitionRepository = moduleDefinitionRepository;
    }

    public async Task SyncTenantModuleCapabilitiesAsync(
        Guid tenantId,
        bool enableIslamic,
        bool enableTech,
        Guid? actorUserId,
        CancellationToken cancellationToken = default)
    {
        await UpsertCapabilityAsync(tenantId, CoreModuleKey, true, actorUserId, cancellationToken);
        await UpsertCapabilityAsync(tenantId, IslamicModuleKey, enableIslamic, actorUserId, cancellationToken);
        await UpsertCapabilityAsync(tenantId, TechModuleKey, enableTech, actorUserId, cancellationToken);
    }

    public async Task SyncTenantModuleCapabilityPatchAsync(
        Guid tenantId,
        bool? enableIslamic,
        bool? enableTech,
        Guid? actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (enableIslamic.HasValue)
            await UpsertCapabilityAsync(tenantId, IslamicModuleKey, enableIslamic.Value, actorUserId, cancellationToken);

        if (enableTech.HasValue)
            await UpsertCapabilityAsync(tenantId, TechModuleKey, enableTech.Value, actorUserId, cancellationToken);
    }

    private async Task UpsertCapabilityAsync(
        Guid tenantId,
        string moduleKey,
        bool isEnabled,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var module = await _moduleDefinitionRepository.GetByKey(moduleKey, cancellationToken);
        if (module == null) return;

        var existing = await _tenantCapabilityRepository.GetByTenantAndModuleKey(tenantId, moduleKey, cancellationToken);
        if (existing == null)
        {
            await _tenantCapabilityRepository.Create(new TenantCapability
            {
                TenantId = tenantId,
                Tenant = null!,
                ModuleId = module.Id,
                Module = null!,
                IsEnabled = isEnabled,
                EnabledAt = DateTime.UtcNow,
                EnabledBy = actorUserId
            }, cancellationToken);
            return;
        }

        existing.IsEnabled = isEnabled;
        if (isEnabled && existing.EnabledAt == default)
        {
            existing.EnabledAt = DateTime.UtcNow;
            existing.EnabledBy = actorUserId;
        }
        await _tenantCapabilityRepository.Update(existing, cancellationToken);
    }
}
