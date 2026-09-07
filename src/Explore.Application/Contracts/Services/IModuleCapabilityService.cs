namespace Explore.Application.Contracts.Services;

public interface IModuleCapabilityService
{
    Task SyncTenantModuleCapabilitiesAsync(
        Guid tenantId,
        bool enableIslamic,
        bool enableTech,
        Guid? actorUserId,
        CancellationToken cancellationToken = default);

    Task SyncTenantModuleCapabilityPatchAsync(
        Guid tenantId,
        bool? enableIslamic,
        bool? enableTech,
        Guid? actorUserId,
        CancellationToken cancellationToken = default);
}
