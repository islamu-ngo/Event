using Explore.Application.Authentication;
using Explore.Application.Contracts.Operations;
using Explore.Domain.Enums;
using Explore.Persistence.QueryFilters;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Database;

/// <summary>
/// Re-reads a tenant package caller's authority on a fresh tenantless Global context for each check.
/// </summary>
/// <remarks>
/// The tenant settings are read by Runtime in its own Serializable snapshot, where Runtime holds no
/// grant on platform or tenant role grants; moderation definitions use ConfigurationManifestPackageModerationReader. This owner therefore reads identity and grants
/// with plain SELECTs over columns Global already holds, takes no row locks and opens no transaction.
/// It mirrors <c>AdministrativeAuthorityQuery</c>: a live user bound to the credential's external
/// login, an active tenant, and either <c>platform.admin</c> or an active, unrevoked TenantAdmin grant.
/// </remarks>
internal sealed class ConfigurationManifestPackageAuthority(
    IServiceProvider provider, IGlobalExploreDbContextFactory globalFactory,
    IHttpContextAccessor requests, TimeProvider clock) : IConfigurationManifestPackageAuthority
{
    public async Task RequireAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
        var request = requests.HttpContext
            ?? throw new UnauthorizedAccessException("Package export requires a human request.");
        var principal = request.User;
        var account = principal.GetProviderIdentity()?.AccountKey
            ?? throw new UnauthorizedAccessException("Package export requires an authenticated provider account.");
        var lifetime = await PolicyMutationExecutionScopeFactory.ResolveLifetimeAsync(provider, cancellationToken)
            ?? throw new UnauthorizedAccessException("Package export requires a validated credential lifetime.");
        RequireLiveCredential();

        await using var context = await globalFactory.CreateDbContextAsync(cancellationToken);
        context.RequireBoundTenantLease();
        if (context.TenantFilterTenantId is not null || context.IsTenantFilterBypassed
            || context.IsConfigurationInstanceImportOwner || context.Database.CurrentTransaction is not null)
            throw new UnauthorizedAccessException("configuration_package_authority_requires_unbound_global");

        Guid[] users = await context.UserExternalLogins.AsNoTracking()
            .Where(login => login.AuthenticationProviderId == (int)account.ProviderKind
                && login.ProviderKey == account.Value && !login.User.IsDeleted)
            .Select(login => login.UserId).Distinct().Take(2).ToArrayAsync(cancellationToken);
        if (users is not [var userId])
            throw new UnauthorizedAccessException("Package export requires one live user bound to the credential.");

        bool activeTenant = await context.Tenants
            .IgnoreTenantFilter(TenantFilterBypassReasons.ConfigurationManifestPackageExport).AsNoTracking()
            .AnyAsync(row => row.Id == tenantId && row.TenantStatus.IsActiveState, cancellationToken);
        bool administrator = activeTenant && (await context.PlatformUserRoles.AsNoTracking()
                .AnyAsync(row => row.UserId == userId && row.Role.RoleScopeId == (int)RoleScopeEnum.Platform
                    && row.Role.MasterCode == "platform.admin", cancellationToken)
            || await context.TenantUserRoleGrants
                .IgnoreTenantFilter(TenantFilterBypassReasons.ConfigurationManifestPackageExport).AsNoTracking()
                .AnyAsync(row => row.TenantId == tenantId && row.TenantUser.TenantId == tenantId
                    && row.TenantUser.UserId == userId && !row.TenantUser.IsDeleted
                    && row.TenantUser.StatusId == (int)TenantUserStatusEnum.Active
                    && row.RevokedAt == null && row.RoleId == (int)RoleEnum.TenantAdmin
                    && row.Role.RoleScopeId == (int)RoleScopeEnum.Tenant, cancellationToken));
        if (!administrator)
            throw new UnauthorizedAccessException("Package export requires current administrator authority for the tenant.");
        RequireLiveCredential();

        void RequireLiveCredential()
        {
            if (!ReferenceEquals(requests.HttpContext, request) || !ReferenceEquals(request.User, principal)
                || principal.GetProviderIdentity()?.AccountKey != account
                || lifetime.AccountKey != account || !lifetime.IsValidAt(clock.GetUtcNow()))
                throw new UnauthorizedAccessException("Package export requires the original live credential.");
        }
    }
}
