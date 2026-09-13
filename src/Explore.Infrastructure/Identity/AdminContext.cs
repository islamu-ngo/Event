using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Explore.Infrastructure.Identity;

/// <summary>
/// Resolves the current user's administrative authority using database tables only.
/// Identity is read from authenticated claims (sub/nameidentifier/sid) and authority
/// is resolved from platform role assignments, tenant user role grants, OrganizationMembers, and GroupMembers.
/// </summary>
public class AdminContext : IAdminContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IPlatformUserRoleRepository _platformUserRoleRepository;
    private readonly ITenantUserRoleGrantRepository _tenantAdminRepo;
    private readonly IOrganizationMemberRepository _orgMemberRepo;
    private readonly IGroupMemberRepository _groupMemberRepo;
    private readonly IUserExternalLoginRepository _userExternalLoginRepository;
    private readonly ILogger<AdminContext> _logger;

    public AdminContext(
        IHttpContextAccessor httpContextAccessor,
        IPlatformUserRoleRepository platformUserRoleRepository,
        ITenantUserRoleGrantRepository tenantAdminRepo,
        IOrganizationMemberRepository orgMemberRepo,
        IGroupMemberRepository groupMemberRepo,
        IUserExternalLoginRepository userExternalLoginRepository,
        ILogger<AdminContext> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _platformUserRoleRepository = platformUserRoleRepository;
        _tenantAdminRepo = tenantAdminRepo;
        _orgMemberRepo = orgMemberRepo;
        _groupMemberRepo = groupMemberRepo;
        _userExternalLoginRepository = userExternalLoginRepository;
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
        cancellationToken.ThrowIfCancellationRequested();
        var user = _httpContextAccessor.HttpContext?.User;
        if (user is null)
            return null;

        var providerIdentity = user.GetProviderIdentity();
        if (providerIdentity is null)
            return user.GetPlatformUserId();

        var externalLogin = await _userExternalLoginRepository.GetByProviderAndKey(
            providerIdentity.AccountKey);
        cancellationToken.ThrowIfCancellationRequested();
        return externalLogin?.UserId;
    }

    public async Task<bool> IsInstanceAdminAsync(Guid userId, CancellationToken cancellationToken = default)
    {
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
    }

    public async Task<bool> IsTenantAdminAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var uid = await ResolveUserIdAsync(cancellationToken);
        if (uid == null)
            return false;

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
    }

    public async Task<bool> IsOrganizationAdminAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var uid = await ResolveUserIdAsync(cancellationToken);
        if (uid == null)
            return false;

        var membership = await _orgMemberRepo.GetByOrganizationAndUser(organizationId, uid.Value);
        return membership != null && IsOrganizationAdminRole(membership.RoleId);
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
        var admins = await _tenantAdminRepo.GetByUserId(userId);
        return admins
            .Where(a => a.RoleId == (int)RoleEnum.TenantAdmin
                && a.TenantUser.StatusId == (int)TenantUserStatusEnum.Active
                && !a.TenantUser.IsDeleted)
            .Select(a => a.TenantId)
            .Distinct()
            .ToList()
            .AsReadOnly();
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
        var memberships = await _orgMemberRepo.GetMembershipsByUser(userId, cancellationToken);
        return memberships
            .Where(m => IsOrganizationAdminRole(m.RoleId))
            .Select(m => m.OrganizationTenant.OrganizationId)
            .Distinct()
            .ToList()
            .AsReadOnly();
    }

    public async Task<IReadOnlyList<Guid>> GetAdminOrganizationIdsAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var memberships = await _orgMemberRepo.GetMembershipsByUser(userId, cancellationToken);
        return memberships
            .Where(membership => membership.TenantId == tenantId && IsOrganizationAdminRole(membership.RoleId))
            .Select(membership => membership.OrganizationTenant.OrganizationId)
            .Distinct()
            .ToList()
            .AsReadOnly();
    }

    public async Task<bool> IsGroupAdminAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        var uid = await ResolveUserIdAsync(cancellationToken);
        if (uid == null)
            return false;

        var membership = await _groupMemberRepo.GetByGroupAndUser(groupId, uid.Value);
        return membership != null && IsGroupAdminRole(membership.RoleId);
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
        var memberships = await _groupMemberRepo.GetMembershipsByUser(userId, cancellationToken);
        return memberships
            .Where(m => IsGroupAdminRole(m.RoleId))
            .Select(m => m.GroupTenant.GroupId)
            .Distinct()
            .ToList()
            .AsReadOnly();
    }

    public async Task<IReadOnlyList<Guid>> GetAdminGroupIdsAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var memberships = await _groupMemberRepo.GetMembershipsByUser(userId, cancellationToken);
        return memberships
            .Where(membership => membership.TenantId == tenantId && IsGroupAdminRole(membership.RoleId))
            .Select(membership => membership.GroupTenant.GroupId)
            .Distinct()
            .ToList()
            .AsReadOnly();
    }

    private static bool IsOrganizationAdminRole(int roleId)
    {
        return roleId == (int)RoleEnum.OrgAdmin;
    }

    private static bool IsGroupAdminRole(int roleId)
    {
        return roleId == (int)RoleEnum.GroupAdmin;
    }
}
