using System.Security.Claims;
using Explore.Application.Authentication;
using Explore.Application.Constants;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Explore.Infrastructure.Identity;

/// <summary>
/// Resolves the current user's administrative authority using database tables only.
/// Identity is read from authenticated claims (sub/nameidentifier/sid) and authority
/// is resolved from platform role assignments, tenant user role grants, OrganizationMembers, and GroupMembers.
/// </summary>
public class AdminContext : IAdminContext, IAdminCacheInvalidator
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IPlatformUserRoleRepository _platformUserRoleRepository;
    private readonly ITenantUserRoleGrantRepository _tenantAdminRepo;
    private readonly IOrganizationMemberRepository _orgMemberRepo;
    private readonly IGroupMemberRepository _groupMemberRepo;
    private readonly IUserExternalLoginRepository _userExternalLoginRepository;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AdminContext> _logger;

    private static readonly TimeSpan CacheExpiration = TimeSpan.FromMinutes(5);
    private const string CacheKeyPrefix = "AdminContext_";

    public AdminContext(
        IHttpContextAccessor httpContextAccessor,
        IPlatformUserRoleRepository platformUserRoleRepository,
        ITenantUserRoleGrantRepository tenantAdminRepo,
        IOrganizationMemberRepository orgMemberRepo,
        IGroupMemberRepository groupMemberRepo,
        IUserExternalLoginRepository userExternalLoginRepository,
        IMemoryCache cache,
        ILogger<AdminContext> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _platformUserRoleRepository = platformUserRoleRepository;
        _tenantAdminRepo = tenantAdminRepo;
        _orgMemberRepo = orgMemberRepo;
        _groupMemberRepo = groupMemberRepo;
        _userExternalLoginRepository = userExternalLoginRepository;
        _cache = cache;
        _logger = logger;
    }

    public Guid? UserId
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            return user?.GetProviderIdentity() is null
                ? user?.GetPlatformUserId()
                : null;
        }
    }

    public async Task<bool> IsInstanceAdminAsync(CancellationToken cancellationToken = default)
    {
        var uid = await ResolveUserIdAsync(cancellationToken);
        return uid == null ? false : await IsInstanceAdminAsync(uid.Value, cancellationToken);
    }

    public async Task<Guid?> ResolveUserIdAsync(CancellationToken cancellationToken = default)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user is null)
            return null;

        var providerIdentity = user.GetProviderIdentity();
        if (providerIdentity is null)
            return user.GetPlatformUserId();

        var cacheKey = $"{CacheKeyPrefix}ResolvedId_{providerIdentity.Provider}_{providerIdentity.AccountKey.Value}";
        if (_cache.TryGetValue<Guid>(cacheKey, out var cachedUserId))
            return cachedUserId;

        var externalLogin = await _userExternalLoginRepository.GetByProviderAndKey(
            providerIdentity.AccountKey);
        Guid? resolvedUserId = externalLogin?.UserId;

        if (resolvedUserId.HasValue)
            _cache.Set(cacheKey, resolvedUserId.Value, TimeSpan.FromMinutes(10));

        return resolvedUserId;
    }

    public async Task<bool> IsInstanceAdminAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        // DB-first authority: resolve from database only.
        var cacheKey = $"{CacheKeyPrefix}Instance_{userId}";
        return await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.SlidingExpiration = CacheExpiration;
            try
            {
                var isRoleAdmin = await _platformUserRoleRepository.IsUserPlatformAdmin(userId);
                if (isRoleAdmin)
                {
                    _logger.LogInformation("AdminContext: IsInstanceAdmin=true (platform.admin role detected in database)");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AdminContext: failed role-based instance admin check");
            }

            _logger.LogWarning("AdminContext: IsInstanceAdmin=false (platform.admin role not found)");
            return false;
        });
    }

    public async Task<bool> IsTenantAdminAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var uid = await ResolveUserIdAsync(cancellationToken);
        if (uid == null)
            return false;

        var cacheKey = $"{CacheKeyPrefix}Tenant_{uid}_{tenantId}";
        return await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.SlidingExpiration = CacheExpiration;
            var isAdmin = await _tenantAdminRepo.IsTenantAdmin(tenantId, uid.Value);

            if (isAdmin)
            {
                _logger.LogInformation("AdminContext: IsTenantAdmin=true");
            }
            else
            {
                _logger.LogDebug("AdminContext: IsTenantAdmin=false");
            }

            return isAdmin;
        });
    }

    public async Task<bool> IsOrganizationAdminAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var uid = await ResolveUserIdAsync(cancellationToken);
        if (uid == null)
            return false;

        var cacheKey = $"{CacheKeyPrefix}Org_{uid}_{organizationId}";
        return await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.SlidingExpiration = CacheExpiration;
            var membership = await _orgMemberRepo.GetByOrganizationAndUser(organizationId, uid.Value);
            return membership != null && IsOrganizationAdminRole(membership.RoleId);
        });
    }

    public async Task<IReadOnlyList<Guid>> GetAdminTenantIdsAsync(CancellationToken cancellationToken = default)
    {
        var uid = await ResolveUserIdAsync(cancellationToken);
        return uid == null
            ? (IReadOnlyList<Guid>)Array.Empty<Guid>()
            : await GetAdminTenantIdsAsync(uid.Value, cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetAdminTenantIdsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{CacheKeyPrefix}TenantIds_{userId}";
        return await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.SlidingExpiration = CacheExpiration;
            var admins = await _tenantAdminRepo.GetByUserId(userId);
            var adminTenantIds = admins
                .Where(a => a.RoleId == (int)RoleEnum.TenantAdmin)
                .Select(a => a.TenantId)
                .Distinct()
                .ToList();

            return (IReadOnlyList<Guid>)adminTenantIds.AsReadOnly();
        }) ?? Array.Empty<Guid>();
    }

    public async Task<IReadOnlyList<Guid>> GetAdminOrganizationIdsAsync(CancellationToken cancellationToken = default)
    {
        var uid = await ResolveUserIdAsync(cancellationToken);
        return uid == null
            ? (IReadOnlyList<Guid>)Array.Empty<Guid>()
            : await GetAdminOrganizationIdsAsync(uid.Value, cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetAdminOrganizationIdsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{CacheKeyPrefix}OrgIds_{userId}";
        return await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.SlidingExpiration = CacheExpiration;
            var memberships = await _orgMemberRepo.GetMembershipsByUser(userId, cancellationToken);
            var adminOrgIds = memberships
                .Where(m => IsOrganizationAdminRole(m.RoleId))
                .Select(m => m.OrganizationTenant.OrganizationId)
                .Distinct()
                .ToList()
                .AsReadOnly();

            return (IReadOnlyList<Guid>)adminOrgIds;
        }) ?? Array.Empty<Guid>();
    }

    public async Task<IReadOnlyList<Guid>> GetAdminOrganizationIdsAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Guid> allIds = await GetAdminOrganizationIdsAsync(userId, cancellationToken);
        var memberships = await _orgMemberRepo.GetMembershipsByUser(userId, cancellationToken);
        HashSet<Guid> tenantIds = memberships
            .Where(membership => membership.TenantId == tenantId)
            .Select(membership => membership.OrganizationTenant.OrganizationId)
            .ToHashSet();
        return allIds.Where(tenantIds.Contains).ToList();
    }

    public async Task<bool> IsGroupAdminAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        var uid = await ResolveUserIdAsync(cancellationToken);
        if (uid == null)
            return false;

        var cacheKey = $"{CacheKeyPrefix}Group_{uid}_{groupId}";
        return await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.SlidingExpiration = CacheExpiration;
            var membership = await _groupMemberRepo.GetByGroupAndUser(groupId, uid.Value);
            return membership != null && IsGroupAdminRole(membership.RoleId);
        });
    }

    public async Task<IReadOnlyList<Guid>> GetAdminGroupIdsAsync(CancellationToken cancellationToken = default)
    {
        var uid = await ResolveUserIdAsync(cancellationToken);
        return uid == null
            ? (IReadOnlyList<Guid>)Array.Empty<Guid>()
            : await GetAdminGroupIdsAsync(uid.Value, cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetAdminGroupIdsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{CacheKeyPrefix}GroupIds_{userId}";
        return await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.SlidingExpiration = CacheExpiration;
            var memberships = await _groupMemberRepo.GetMembershipsByUser(userId, cancellationToken);
            var adminGroupIds = memberships
                .Where(m => IsGroupAdminRole(m.RoleId))
                .Select(m => m.GroupTenant.GroupId)
                .Distinct()
                .ToList()
                .AsReadOnly();

            return (IReadOnlyList<Guid>)adminGroupIds;
        }) ?? Array.Empty<Guid>();
    }

    public async Task<IReadOnlyList<Guid>> GetAdminGroupIdsAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Guid> allIds = await GetAdminGroupIdsAsync(userId, cancellationToken);
        var memberships = await _groupMemberRepo.GetMembershipsByUser(userId, cancellationToken);
        HashSet<Guid> tenantIds = memberships
            .Where(membership => membership.TenantId == tenantId)
            .Select(membership => membership.GroupTenant.GroupId)
            .ToHashSet();
        return allIds.Where(tenantIds.Contains).ToList();
    }

    private static bool IsOrganizationAdminRole(int roleId)
    {
        return roleId == (int)RoleEnum.OrgAdmin;
    }

    private static bool IsGroupAdminRole(int roleId)
    {
        return roleId == (int)RoleEnum.GroupAdmin;
    }


    /// <inheritdoc />
    public void InvalidateUser(Guid userId)
    {
        _cache.Remove($"{CacheKeyPrefix}Instance_{userId}");
        _cache.Remove($"{CacheKeyPrefix}TenantIds_{userId}");
        _cache.Remove($"{CacheKeyPrefix}OrgIds_{userId}");
        _cache.Remove($"{CacheKeyPrefix}GroupIds_{userId}");

        // For single-tenant mode, we can proactively clear the default tenant admin cache
        _cache.Remove($"{CacheKeyPrefix}Tenant_{userId}_{PlatformDefaults.DefaultTenantId}");

        _logger.LogInformation("AdminContext: Invalidated authority cache for one user");
    }

    /// <inheritdoc />
    public void InvalidateAll()
    {
        // IMemoryCache has no "clear all" method. We track known keys for targeted eviction.
        // For bulk invalidation, we swap to a new CancellationTokenSource-based approach.
        // However, since AdminContext is scoped per-request and cache keys are user-specific,
        // the practical approach is to let entries expire naturally (5-min sliding window).
        // PolicySyncService should call InvalidateUser for specific affected users when known.
        _logger.LogInformation("AdminContext: Full cache invalidation requested. " +
            "User-specific entries will expire via 5-minute sliding window");
    }
}
