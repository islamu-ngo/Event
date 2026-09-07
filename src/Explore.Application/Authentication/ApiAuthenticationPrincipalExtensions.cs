// ABOUTME: Parses Local session, credential-replacement, and API-key authorities from API principals.
// ABOUTME: Centralizes bounded claim extraction and canonical platform identity delegation for API consumers.

using System.Security.Claims;
using System.Globalization;
using Explore.Application.Configuration;
using Explore.Application.Constants;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain.Enums;

namespace Explore.Application.Authentication;

public static class ApiAuthenticationPrincipalExtensions
{
    public static LocalSessionAuthority? TryGetLocalSessionAuthority(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ClaimsIdentity[] authenticated = principal.Identities.Where(identity => identity.IsAuthenticated).ToArray();
        if (authenticated is not [var identity]
            || identity.AuthenticationType != ApiAuthenticationSchemeNames.LocalIdentity)
        {
            return null;
        }
        string? subject = SingleClaim(identity, "sub");
        string? stamp = SingleClaim(identity, LocalSessionToken.SecurityStampClaim);
        string? emailVerified = SingleClaim(identity, "email_verified");
        if (!Guid.TryParseExact(subject, "D", out Guid subjectId) || subjectId == Guid.Empty
            || !string.Equals(subject, subjectId.ToString("D"), StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(stamp) || stamp.Length > 256
            || emailVerified is not ("true" or "false")
            || SingleClaim(identity, "iss") != LocalIdentityOptions.Issuer
            || SingleClaim(identity, "aud") != LocalIdentityOptions.Audience
            || SingleClaim(identity, "auth_provider") != "local"
            || identity.HasClaim(claim => claim.Type == LocalCredentialChallengeToken.PurposeClaim))
        {
            return null;
        }
        return new LocalSessionAuthority(
            localSubjectId: subjectId, securityStamp: stamp, emailVerified: emailVerified == "true");
    }

    public static LocalCredentialReplacementAuthority? TryGetLocalCredentialReplacementAuthority(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ClaimsIdentity[] authenticated = principal.Identities.Where(identity => identity.IsAuthenticated).ToArray();
        if (authenticated is not [var identity]
            || identity.AuthenticationType != ApiAuthenticationSchemeNames.LocalCredentialReplacement)
        {
            return null;
        }
        string? subject = SingleClaim(identity, "sub");
        string? operation = SingleClaim(identity, LocalCredentialChallengeToken.OperationIdClaim);
        string? stamp = SingleClaim(identity, LocalCredentialChallengeToken.SecurityStampClaim);
        string? tokenId = SingleClaim(identity, "jti");
        if (!Guid.TryParseExact(subject, "D", out Guid subjectId) || subjectId == Guid.Empty
            || !string.Equals(subject, subjectId.ToString("D"), StringComparison.Ordinal)
            || !Guid.TryParseExact(operation, "D", out Guid operationId) || operationId == Guid.Empty
            || !string.Equals(operation, operationId.ToString("D"), StringComparison.Ordinal)
            || !Guid.TryParseExact(tokenId, "N", out Guid tokenGuid) || tokenGuid == Guid.Empty
            || !string.Equals(tokenId, tokenGuid.ToString("N"), StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(stamp)
            || SingleClaim(identity, "iss") != LocalIdentityOptions.Issuer
            || SingleClaim(identity, "aud") != LocalCredentialChallengeToken.Audience
            || SingleClaim(identity, LocalCredentialChallengeToken.PurposeClaim) != LocalCredentialChallengeToken.Purpose
            || !long.TryParse(SingleClaim(identity, "iat"), NumberStyles.None, CultureInfo.InvariantCulture, out long issuedAt)
            || !long.TryParse(SingleClaim(identity, "nbf"), NumberStyles.None, CultureInfo.InvariantCulture, out long notBefore)
            || !long.TryParse(SingleClaim(identity, "exp"), NumberStyles.None, CultureInfo.InvariantCulture, out long expiresAt)
            || issuedAt != notBefore)
        {
            return null;
        }
        try
        {
            return new LocalCredentialReplacementAuthority(
                subject: new LocalCredentialReplacementSubject(
                    localSubjectId: subjectId, operationId: operationId, securityStamp: stamp),
                issuedAtUtc: DateTimeOffset.FromUnixTimeSeconds(issuedAt),
                expiresAtUtc: DateTimeOffset.FromUnixTimeSeconds(expiresAt));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string? SingleClaim(ClaimsIdentity identity, string claimType)
    {
        Claim[] claims = identity.FindAll(claimType).Take(2).ToArray();
        return claims is [var claim] ? claim.Value : null;
    }

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
