using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Identity;
using Explore.Application.Authorization;
using Explore.Application.Exceptions;
using Explore.Application.DTOs.User;
using Explore.Application.Features.Users.Requests.Queries;

namespace Explore.Application.Features.Users.Handlers.Queries;

public class ResolveUserTenantRedirectionRequestHandler : IQueryHandler<ResolveUserTenantRedirectionRequest, UserTenantRedirectionDto>
{
    private readonly IUserTenantMembershipRepository _tenantUserRepository;
    private readonly IUserTenantNavigationRepository _userRepository;
    private readonly IUserContext _userContext;
    private readonly IAdminContext _adminContext;

    public ResolveUserTenantRedirectionRequestHandler(
        IUserTenantMembershipRepository tenantUserRepository,
        IUserTenantNavigationRepository userRepository,
        IUserContext userContext,
        IAdminContext adminContext)
    {
        _tenantUserRepository = tenantUserRepository;
        _userRepository = userRepository;
        _userContext = userContext;
        _adminContext = adminContext;
    }

    public async Task<UserTenantRedirectionDto> QueryAsync(
        ResolveUserTenantRedirectionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_userContext.IsAuthenticated || request.UserId == Guid.Empty
            || await _adminContext.ResolveUserIdAsync(cancellationToken) != request.UserId)
            throw new AuthorizationException(ResourceKinds.User, AuthorizationActions.Users.View);
        var user = await _userRepository.GetNavigationUserAsync(request.UserId, cancellationToken);
        if (user is null || user.IsDeleted)
            throw new AuthorizationException(ResourceKinds.User, AuthorizationActions.Users.View);
        var memberships = await _tenantUserRepository.GetActiveTenantsForUserAsync(request.UserId, cancellationToken);

        if (memberships.Count == 0)
        {
            return new UserTenantRedirectionDto
            {
                TenantId = null,
                TenantSlug = null,
                HasMultipleTenants = false
            };
        }

        if (memberships.Count == 1)
        {
            var single = memberships[0];
            return new UserTenantRedirectionDto
            {
                TenantId = single.TenantId,
                TenantSlug = single.Tenant.Slug,
                HasMultipleTenants = false
            };
        }

        // Multi-tenant user - check LastActiveTenantId
        if (user.LastActiveTenantId != null)
        {
            var activeMatch = memberships.FirstOrDefault(x => x.TenantId == user.LastActiveTenantId);
            if (activeMatch != null)
            {
                return new UserTenantRedirectionDto
                {
                    TenantId = activeMatch.TenantId,
                    TenantSlug = activeMatch.Tenant.Slug,
                    HasMultipleTenants = true
                };
            }
        }

        // Fallback to the latest active membership
        var latest = memberships
            .OrderByDescending(x => x.JoinedAt ?? x.CreatedAt)
            .First();

        return new UserTenantRedirectionDto
        {
            TenantId = latest.TenantId,
            TenantSlug = latest.Tenant.Slug,
            HasMultipleTenants = true
        };
    }
}
