using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Authorization;

public interface ICapabilityCeilingService
{
    /// <summary>
    /// Validates that a caller can assign the given permissions to a role at the target scope.
    /// Returns validation errors (empty if valid).
    /// </summary>
    Task<List<string>> ValidatePermissionAssignmentAsync(
        IEnumerable<int> callerRoleIds,
        RoleScopeEnum callerHighestScope,
        IEnumerable<int> permissionIdsToAssign,
        RoleScopeEnum targetRoleScope);

    /// <summary>
    /// Checks if a role can be modified (not a system role).
    /// </summary>
    Task<(bool IsAllowed, string? Error)> CanModifyRoleAsync(int roleId);

    /// <summary>
    /// Checks if the caller's scope allows creating/modifying roles at the target scope.
    /// </summary>
    (bool IsAllowed, string? Error) ValidateScopeBoundary(
        RoleScopeEnum callerHighestScope,
        RoleScopeEnum targetRoleScope);
}

public class CapabilityCeilingService : ICapabilityCeilingService
{
    private readonly IPermissionRepository _permissionRepository;
    private readonly IRoleRepository _roleRepository;

    public CapabilityCeilingService(
        IPermissionRepository permissionRepository,
        IRoleRepository roleRepository)
    {
        _permissionRepository = permissionRepository;
        _roleRepository = roleRepository;
    }

    public async Task<List<string>> ValidatePermissionAssignmentAsync(
        IEnumerable<int> callerRoleIds,
        RoleScopeEnum callerHighestScope,
        IEnumerable<int> permissionIdsToAssign,
        RoleScopeEnum targetRoleScope)
    {
        var errors = new List<string>();

        // Rule 3: Scope boundary — caller can only create roles at their scope or lower
        var scopeCheck = ValidateScopeBoundary(callerHighestScope, targetRoleScope);
        if (!scopeCheck.IsAllowed)
        {
            errors.Add(scopeCheck.Error!);
            return errors;
        }

        // Rule 1: Grant ceiling — can only grant permissions the caller has
        var assignable = await _permissionRepository.GetAssignablePermissionsAsync(
            callerRoleIds, targetRoleScope);
        var assignableIds = assignable.Select(p => p.Id).ToHashSet();

        var requestedIds = permissionIdsToAssign.ToList();
        var unauthorized = requestedIds.Where(id => !assignableIds.Contains(id)).ToList();

        if (unauthorized.Count > 0)
        {
            errors.Add($"Cannot assign permissions you don't have: [{string.Join(", ", unauthorized)}]");
        }

        // Rule 2: IsFiltered check is already handled by GetAssignablePermissionsAsync
        // (it excludes IsFiltered permissions unless the caller has them)

        return errors;
    }

    public async Task<(bool IsAllowed, string? Error)> CanModifyRoleAsync(int roleId)
    {
        var role = await _roleRepository.GetByIdAsync(roleId);

        if (role == null)
            return (false, "Role not found.");

        // Rule 4: System roles cannot be modified
        if (role.IsSystem)
            return (false, $"System role '{role.FullName}' cannot be modified or deleted.");

        return (true, null);
    }

    public (bool IsAllowed, string? Error) ValidateScopeBoundary(
        RoleScopeEnum callerHighestScope,
        RoleScopeEnum targetRoleScope)
    {
        // Rule 3: Scope boundary
        // Platform admin (0) can create any scope
        // Tenant admin (1) can create Tenant (1) or Organization (2) roles
        // Org admin (2) can only create Organization (2) roles
        if ((int)targetRoleScope < (int)callerHighestScope)
        {
            return (false, $"Cannot create roles at scope '{targetRoleScope}'. Your highest scope is '{callerHighestScope}'.");
        }

        return (true, null);
    }
}
