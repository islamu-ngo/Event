using System.Security.Claims;
using Explore.Application.Constants;
using Explore.Domain.Enums;

namespace Explore.Application.Authentication;

public static class ApiAuthenticationPrincipalExtensions
{
    public static string? GetApiKeyId(this ClaimsPrincipal principal)
    {
        return principal.FindFirst(ApiAuthenticationClaimTypes.ApiKeyId)?.Value;
    }

    public static ApiKeyPrincipalContext? TryGetApiKeyPrincipalContext(this ClaimsPrincipal principal)
    {
        var keyId = principal.GetApiKeyId();
        var tenantIdValue = principal.FindFirst(ApiAuthenticationClaimTypes.TenantId)?.Value;
        var ownerTypeValue = principal.FindFirst(ApiAuthenticationClaimTypes.OwnerType)?.Value;
        var ownerIdValue = principal.FindFirst(ApiAuthenticationClaimTypes.OwnerId)?.Value;

        if (string.IsNullOrWhiteSpace(keyId) ||
            !Enum.TryParse<ExternalApiKeyOwnerType>(ownerTypeValue, ignoreCase: true, out var ownerType) ||
            !Guid.TryParse(ownerIdValue, out var ownerId))
        {
            return null;
        }

        // TenantId is optional — InstanceAdmin keys have no tenant claim.
        Guid? tenantId = Guid.TryParse(tenantIdValue, out var parsedTenantId) ? parsedTenantId : null;

        var scopes = principal.FindAll(ApiAuthenticationClaimTypes.Scope)
            .Select(claim => claim.Value)
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new ApiKeyPrincipalContext(keyId, tenantId, ownerType, ownerId, scopes);
    }

    /// <summary>
    /// Delegates to <see cref="PlatformIdentityPrincipalExtensions.GetPlatformUserId"/> so diagnostics report
    /// the same identity the platform actually authorizes against, rather than a shorter private chain.
    /// </summary>
    public static Guid? GetAuthenticatedUserId(this ClaimsPrincipal principal) => principal.GetPlatformUserId();

    public static string? GetAuthenticationMethod(this ClaimsPrincipal principal)
    {
        return principal.FindFirst(ApiAuthenticationClaimTypes.AuthMethod)?.Value
            ?? (principal.Identity?.IsAuthenticated == true ? "jwt" : null);
    }
}

public sealed record ApiKeyPrincipalContext(
    string KeyId,
    Guid? TenantId,
    ExternalApiKeyOwnerType OwnerType,
    Guid OwnerId,
    IReadOnlyList<string> Scopes);
