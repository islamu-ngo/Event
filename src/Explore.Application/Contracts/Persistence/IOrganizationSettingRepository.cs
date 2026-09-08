// for organization-specific setting overrides.

namespace Explore.Application.Contracts.Persistence;

using Explore.Domain;

/// <summary>
/// Repository for organization-specific setting overrides.
/// </summary>
public interface IOrganizationSettingRepository
{
    Task<OrganizationSetting?> GetByOrganizationAndKey(
        Guid tenantId,
        Guid organizationId,
        string key,
        CancellationToken cancellationToken = default);

    Task<List<OrganizationSetting>> GetAllForOrganization(
        Guid tenantId,
        Guid organizationId,
        CancellationToken cancellationToken = default);

    Task SetValueAsync(
        Guid tenantId,
        Guid organizationId,
        string key,
        string value,
        Guid actorId,
        CancellationToken cancellationToken = default);

    Task<bool> RemoveOverride(
        Guid tenantId,
        Guid organizationId,
        string key,
        CancellationToken cancellationToken = default);
}
