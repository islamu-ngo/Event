using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;

namespace Explore.Application.Authorization;

public interface IPermissionRegistryService
{
    /// <summary>
    /// Gets all active permissions. Cached for performance.
    /// </summary>
    Task<IReadOnlyList<Permission>> GetAllPermissionsAsync();

    /// <summary>
    /// Validates that a MasterCode exists and is active.
    /// </summary>
    Task<bool> ValidateMasterCodeAsync(string masterCode);

    /// <summary>
    /// Gets permissions grouped by GroupName for UI display.
    /// </summary>
    Task<IReadOnlyList<Permission>> GetPermissionsByGroupAsync(string groupName);

    /// <summary>
    /// Gets permissions for a scope, optionally excluding filtered (dangerous) ones.
    /// Non-super-admins should always see excludeFiltered=true.
    /// </summary>
    Task<IReadOnlyList<Permission>> GetFilteredPermissionsAsync(
        RoleScopeEnum? scope = null,
        bool excludeFiltered = true);

    /// <summary>
    /// Gets permissions for a normalized role scope lookup ID, optionally excluding filtered ones.
    /// </summary>
    Task<IReadOnlyList<Permission>> GetFilteredPermissionsByScopeIdAsync(
        int? roleScopeId = null,
        bool excludeFiltered = true);

    /// <summary>
    /// Invalidates the cached permissions. Call after permission table changes.
    /// </summary>
    void InvalidateCache();
}

public class PermissionRegistryService : IPermissionRegistryService
{
    private readonly IPermissionRepository _permissionRepository;
    private readonly IMemoryCache _cache;

    private const string AllPermissionsCacheKey = "permission_registry:all";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

    public PermissionRegistryService(
        IPermissionRepository permissionRepository,
        IMemoryCache cache)
    {
        _permissionRepository = permissionRepository;
        _cache = cache;
    }

    public async Task<IReadOnlyList<Permission>> GetAllPermissionsAsync()
    {
        if (_cache.TryGetValue(AllPermissionsCacheKey, out IReadOnlyList<Permission>? cached) && cached != null)
            return cached;

        var all = await _permissionRepository.GetAll();
        var active = all.Where(p => p.IsActive).ToList().AsReadOnly();

        _cache.Set(AllPermissionsCacheKey, (IReadOnlyList<Permission>)active, CacheDuration);
        return active;
    }

    public async Task<bool> ValidateMasterCodeAsync(string masterCode)
    {
        var all = await GetAllPermissionsAsync();
        return all.Any(p => p.MasterCode == masterCode);
    }

    public async Task<IReadOnlyList<Permission>> GetPermissionsByGroupAsync(string groupName)
    {
        var all = await GetAllPermissionsAsync();
        return all.Where(p => p.GroupName == groupName).ToList().AsReadOnly();
    }

    public async Task<IReadOnlyList<Permission>> GetFilteredPermissionsAsync(
        RoleScopeEnum? scope = null,
        bool excludeFiltered = true)
    {
        return await GetFilteredPermissionsByScopeIdAsync(scope.HasValue ? (int)scope.Value : null, excludeFiltered);
    }

    public async Task<IReadOnlyList<Permission>> GetFilteredPermissionsByScopeIdAsync(
        int? roleScopeId = null,
        bool excludeFiltered = true)
    {
        var all = await GetAllPermissionsAsync();

        var filtered = all.AsEnumerable();

        if (roleScopeId.HasValue)
            filtered = filtered.Where(p => p.RoleScopeId == roleScopeId.Value);

        if (excludeFiltered)
            filtered = filtered.Where(p => !p.IsFiltered);

        return filtered.ToList().AsReadOnly();
    }

    public void InvalidateCache()
    {
        _cache.Remove(AllPermissionsCacheKey);
    }
}
