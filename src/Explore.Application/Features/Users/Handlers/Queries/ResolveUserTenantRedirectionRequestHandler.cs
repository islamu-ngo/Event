using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.User;
using Explore.Application.Features.Users.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.Users.Handlers.Queries;

public class ResolveUserTenantRedirectionRequestHandler : IRequestHandler<ResolveUserTenantRedirectionRequest, UserTenantRedirectionDto>
{
    private readonly ITenantUserRepository _tenantUserRepository;
    private readonly IUserRepository _userRepository;

    public ResolveUserTenantRedirectionRequestHandler(
        ITenantUserRepository tenantUserRepository,
        IUserRepository userRepository)
    {
        _tenantUserRepository = tenantUserRepository;
        _userRepository = userRepository;
    }

    public async Task<UserTenantRedirectionDto> Handle(
        ResolveUserTenantRedirectionRequest request,
        CancellationToken cancellationToken)
    {
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
        var user = await _userRepository.GetById(request.UserId);
        if (user?.LastActiveTenantId != null)
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
