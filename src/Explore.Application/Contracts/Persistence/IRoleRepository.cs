using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Contracts.Persistence;

public interface IRoleRepository : IGenericRepository<Role, int>
{
    /// <summary>
    /// Gets all roles for a specific scope (Platform, Tenant, Organization).
    /// </summary>
    Task<IReadOnlyList<Role>> GetByScopeAsync(RoleScopeEnum scope);

    /// <summary>
    /// Gets all roles for a normalized role scope lookup ID.
    /// </summary>
    Task<IReadOnlyList<Role>> GetByScopeIdAsync(int roleScopeId);

    /// <summary>
    /// Gets a role by its unique MasterCode (e.g., "org.admin", "tenant.owner").
    /// </summary>
    Task<Role?> GetByMasterCodeAsync(string masterCode);

    /// <summary>
    /// Gets a role by ID with eager loading. Alias for GetById with async naming.
    /// </summary>
    Task<Role?> GetByIdAsync(int id);

    /// <summary>
    /// Gets all roles. Async alias for GetAll.
    /// </summary>
    Task<IReadOnlyList<Role>> GetAllAsync();

    /// <summary>
    /// Gets all permissions granted to a specific role via RolePermission join table.
    /// </summary>
    Task<IReadOnlyList<Permission>> GetPermissionsForRoleAsync(int roleId);

    /// <summary>
    /// Assigns permissions to a role by creating RolePermission entries.
    /// Does not remove existing permissions — use <see cref="ReplacePermissionsAsync"/> for that.
    /// </summary>
    Task AssignPermissionsAsync(int roleId, IEnumerable<int> permissionIds, Guid? grantedBy = null);

    /// <summary>
    /// Replaces all permissions for a role. Removes existing RolePermission entries and creates new ones.
    /// </summary>
    Task ReplacePermissionsAsync(int roleId, IEnumerable<int> permissionIds, Guid? grantedBy = null);

    /// <summary>
    /// Removes all RolePermission entries for the specified role.
    /// </summary>
    Task RemoveAllPermissionsAsync(int roleId);

    /// <summary>
    /// Checks if a role has any active members/grants assigned (OrganizationMember or TenantUserRoleGrant with this RoleId).
    /// Used to prevent deletion of roles still in use.
    /// </summary>
    Task<bool> HasActiveMembersAsync(int roleId);
}
