using System.Security.Claims;
using Explore.Application.Authentication;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Domain.Constants;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;

namespace Explore.Infrastructure.Identity;

/// <summary>
/// Transforms the authenticated ClaimsPrincipal by adding admin authority claims
/// resolved from the database via <see cref="IAdminContext"/>.
/// These claims remain server-side; browser UI authority must come from BFF/API/HAL/status endpoints.
/// </summary>
public sealed class AdminClaimsTransformation : IClaimsTransformation
{
    private readonly IAdminContext _adminContext;
    private readonly IUserExternalLoginRepository _userExternalLoginRepository;
    private readonly ILogger<AdminClaimsTransformation> _logger;

    public AdminClaimsTransformation(
        IAdminContext adminContext,
        IUserExternalLoginRepository userExternalLoginRepository,
        ILogger<AdminClaimsTransformation> logger)
    {
        _adminContext = adminContext;
        _userExternalLoginRepository = userExternalLoginRepository;
        _logger = logger;
    }

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return principal;
        }

        var userId = await ResolveInternalUserIdAsync(principal);
        if (!userId.HasValue)
        {
            return principal;
        }

        if (!principal.HasClaim(c => c.Type == PlatformIdentityClaimTypes.InternalUserId))
        {
            var internalIdentity = new ClaimsIdentity();
            internalIdentity.AddClaim(new Claim(PlatformIdentityClaimTypes.InternalUserId, userId.Value.ToString()));
            principal.AddIdentity(internalIdentity);
        }

        if (principal.HasClaim(c => c.Type == AdminClaimTypes.InstanceAdmin
                                    || c.Type == AdminClaimTypes.TenantAdmin
                                    || c.Type == AdminClaimTypes.OrganizationAdmin
                                    || c.Type == AdminClaimTypes.GroupAdmin))
        {
            return principal;
        }

        var identity = new ClaimsIdentity();

        try
        {
            // Resolve instance admin authority
            var isInstanceAdmin = await _adminContext.IsInstanceAdminAsync(userId.Value);
            if (isInstanceAdmin)
            {
                identity.AddClaim(new Claim(AdminClaimTypes.InstanceAdmin, "true"));
            }

            // Resolve tenant admin authority (one claim per tenant)
            var tenantIds = await _adminContext.GetAdminTenantIdsAsync(userId.Value);
            foreach (var tenantId in tenantIds)
            {
                identity.AddClaim(new Claim(AdminClaimTypes.TenantAdmin, tenantId.ToString()));
            }

            // Resolve organization admin authority (one claim per organization)
            var orgIds = await _adminContext.GetAdminOrganizationIdsAsync(userId.Value);
            foreach (var orgId in orgIds)
            {
                identity.AddClaim(new Claim(AdminClaimTypes.OrganizationAdmin, orgId.ToString()));
            }

            var groupIds = await _adminContext.GetAdminGroupIdsAsync(userId.Value);
            foreach (var groupId in groupIds)
            {
                identity.AddClaim(new Claim(AdminClaimTypes.GroupAdmin, groupId.ToString()));
            }

            if (identity.Claims.Any())
            {
                principal.AddIdentity(identity);
                _logger.LogDebug("AdminClaimsTransformation: Added administrative authority claims");
            }
        }
        catch (Exception ex)
        {
            // Fail open for claims transformation — log the error but don't block authentication.
            // The authorization layer (Cerbos/MediatR behavior) provides the hard security boundary.
            _logger.LogWarning(ex,
                "AdminClaimsTransformation: Failed to resolve admin authority. " +
                "Admin UI will be hidden but server-side authorization remains enforced.");
        }

        return principal;
    }

    private async Task<Guid?> ResolveInternalUserIdAsync(ClaimsPrincipal principal)
    {
        var providerIdentity = principal.GetProviderIdentity();
        if (providerIdentity is null)
            return principal.GetPlatformUserId();

        var externalLogin = await _userExternalLoginRepository.GetByProviderAndKey(
            providerIdentity.AccountKey);
        return externalLogin?.UserId;
    }
}
