using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Contracts.Persistence;

public interface IPermissionRepository : IGenericRepository<Permission, int>
{
    /// <summary>
    /// Gets a permission by its unique MasterCode (e.g., "event:update", "organization:delete").
    /// </summary>
    Task<Permission?> GetByMasterCodeAsync(string masterCode);

    /// <summary>
    /// Gets all permissions for a specific resource kind (e.g., all "event" permissions).
    /// </summary>
    Task<IReadOnlyList<Permission>> GetByResourceKindAsync(string resourceKind);

    /// <summary>
    /// Gets all permissions for a specific scope (Platform, Tenant, Organization).
    /// </summary>
    Task<IReadOnlyList<Permission>> GetByScopeAsync(RoleScopeEnum scope);

    /// <summary>
    /// Checks if any of the given role IDs have a specific permission.
    /// Core method used by LocalAuthorizationProvider for dynamic permission checks.
    /// </summary>
    Task<bool> HasPermissionAsync(IEnumerable<int> roleIds, string permissionMasterCode);

    /// <summary>
    /// Gets permissions assignable by the caller (capability ceiling filter).
    /// Excludes IsFiltered permissions unless caller has them, respects scope boundary.
    /// </summary>
    Task<IReadOnlyList<Permission>> GetAssignablePermissionsAsync(
        IEnumerable<int> callerRoleIds,
        RoleScopeEnum targetScope);
}
