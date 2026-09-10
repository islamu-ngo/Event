using System.Net.Http.Headers;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using Event.Web.BffHosting.Authentication;
using Event.Web.BffHosting.Security;
using Explore.Blazor.Client.Clients;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;

namespace Explore.Blazor.Services;

/// <summary>
/// Enriches the authenticated BFF cookie principal with admin authority claims by calling
/// the API's admin-authority endpoint at sign-in and refresh boundaries.
/// <para>
/// Positive results (user has admin authority) are cached for 5 minutes.
/// Negative results (user has no admin authority) are cached for 30 seconds to allow quick
/// recognition after role assignments (e.g., instance onboarding). Remote fetch failures are
/// cached briefly to avoid retry storms while downstream auth services are unhealthy.
/// </para>
/// </summary>
public sealed class BffAdminClaimsTransformation
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly IBffOnboardingStatusProvider _onboardingStatusProvider;
    private readonly ILogger<BffAdminClaimsTransformation> _logger;

    private static readonly TimeSpan PositiveCacheDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan NegativeCacheDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FailureCacheDuration = TimeSpan.FromSeconds(10);

    internal const string CacheKeyPrefix = "BffAdminClaims_";
    internal const string HttpClientName = "AdminAuthority";

    private const string InstanceAdminClaim = "explore:admin:instance";
    private const string TenantAdminClaim = "explore:admin:tenant";
    private const string OrganizationAdminClaim = "explore:admin:organization";
    private const string GroupAdminClaim = "explore:admin:group";
    private const string InternalUserIdClaim = "internal_user_id";

    public BffAdminClaimsTransformation(
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        IBffOnboardingStatusProvider onboardingStatusProvider,
        ILogger<BffAdminClaimsTransformation> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _onboardingStatusProvider = onboardingStatusProvider;
        _logger = logger;
    }

    public async Task<bool> EnrichPrincipalAsync(
        ClaimsPrincipal principal,
        AuthenticationProperties? properties,
        bool forceRefresh = false,
        bool synchronizeUser = false,
        CancellationToken cancellationToken = default)
    {
        if (!await ValidateLocalSessionAsync(
                principal, properties, properties?.GetTokenValue("access_token"), cancellationToken))
        {
            return false;
        }

        if (principal.Identity?.IsAuthenticated != true
            || !principal.TryGetAdminSubject(out var sub))
        {
            RemoveAdminClaims(principal);
            return false;
        }

        var accessToken = properties?.GetTokenValue("access_token");
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            RemoveAdminClaims(principal);
            _logger.LogDebug(
                "BFF admin enrichment skipped | Outcome={Outcome} Reason={Reason} Purpose={Purpose}",
                "skipped", "access_token_missing", "admin");
            return false;
        }

        var cacheKey = $"{CacheKeyPrefix}{sub.PartitionKey}";
        var initialStatus = await _onboardingStatusProvider
            .GetStatusAsync(cancellationToken)
            .ConfigureAwait(false);
        if (initialStatus.Disposition == BffOnboardingDisposition.Closed
            || initialStatus.Disposition == BffOnboardingDisposition.ConfiguredAdministratorPending
            && !HasConfiguredProvider(principal, initialStatus))
        {
            RemoveAdminClaims(principal);
            _cache.Remove(cacheKey);
            return false;
        }

        if (synchronizeUser)
        {
            var internalUserId = await SynchronizeUserAsync(accessToken, cancellationToken);
            if (internalUserId is null)
            {
                RemoveAdminClaims(principal);
                _cache.Remove(cacheKey);
                return false;
            }

            ReplaceInternalUserIdClaim(principal, internalUserId.Value);
            _cache.Remove(cacheKey);
            _onboardingStatusProvider.Invalidate();
        }

        var onboardingStatus = await _onboardingStatusProvider
            .GetStatusAsync(cancellationToken)
            .ConfigureAwait(false);
        if (onboardingStatus.Disposition != BffOnboardingDisposition.Completed)
        {
            RemoveAdminClaims(principal);
            _cache.Remove(cacheKey);
            return false;
        }

        if (forceRefresh)
        {
            _cache.Remove(cacheKey);
        }

        if (_cache.TryGetValue(cacheKey, out BffAdminAuthorityCacheEntry? cached) && cached is not null)
        {
            if (cached.Authority is not null)
            {
                ReplaceAdminClaims(principal, cached.Authority);
                return cached.Authority.HasAnyAuthority == true;
            }

            RemoveAdminClaims(principal);
            return false;
        }

        var authority = await FetchAdminAuthorityAsync(accessToken, cancellationToken);
        if (authority is not null)
        {
            var ttl = authority.HasAnyAuthority == true ? PositiveCacheDuration : NegativeCacheDuration;
            _cache.Set(cacheKey, BffAdminAuthorityCacheEntry.Success(authority), ttl);
            ReplaceAdminClaims(principal, authority);
            return authority.HasAnyAuthority == true;
        }

        _cache.Set(cacheKey, BffAdminAuthorityCacheEntry.Failure, FailureCacheDuration);
        RemoveAdminClaims(principal);
        return false;
    }

    internal async Task<bool> ValidateLocalSessionAsync(
        ClaimsPrincipal principal,
        AuthenticationProperties? properties,
        string? accessToken,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        const string localIssuer = "islamu-event-local";
        const string localAudience = "islamu-event-api";
        var providerClaims = principal.FindAll("auth_provider").ToArray();
        var protectedProvider = properties?.Items.TryGetValue(
            EventBffAuthenticationConstants.AuthenticationProviderPropertyKey, out var provider) == true
            ? provider : null;
        var oidcScheme = properties?.Items.TryGetValue(
            EventBffAuthenticationConstants.OidcSchemePropertyKey, out var scheme) == true
            ? scheme : null;
        JwtSecurityToken? token = null;
        if (!string.IsNullOrWhiteSpace(accessToken) && accessToken.Length <= 8192)
        {
            try
            {
                token = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
            }
            catch (Exception exception) when (exception is ArgumentException
                or Microsoft.IdentityModel.Tokens.SecurityTokenException)
            {
            }
        }

        var hasLocalIndicator = string.Equals(protectedProvider, "local", StringComparison.Ordinal)
            || providerClaims.Any(claim => claim.Value == "local")
            || token?.Claims.Any(claim => claim.Type == "iss" && claim.Value == localIssuer
                || claim.Type == "auth_provider" && claim.Value == "local") == true;
        if (!hasLocalIndicator)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return true;
        }

        var subjectId = Guid.Empty;
        var hasLocalAuthorityShape = (protectedProvider is null or "local")
            && string.IsNullOrEmpty(oidcScheme)
            && providerClaims is [{ Value: "local" }]
            && principal.Identities.Any(identity => identity.IsAuthenticated
                && identity.HasClaim("auth_provider", "local"))
            && principal.TryGetOpaqueProviderSubject(out var subject)
            && Guid.TryParseExact(subject.Value, "D", out subjectId)
            && subjectId != Guid.Empty
            && string.Equals(subject.Value, subjectId.ToString("D"), StringComparison.Ordinal)
            && principal.TryGetSessionId(out _)
            && token is not null
            && HasSingleTokenClaim(token, "iss", localIssuer)
            && HasSingleTokenClaim(token, "aud", localAudience)
            && HasSingleTokenClaim(token, "auth_provider", "local")
            && HasSingleTokenClaim(token, "sub", subjectId.ToString("D"))
            && !token.Claims.Any(claim => claim.Type == "purpose");
        if (hasLocalAuthorityShape)
        {
            try
            {
                using var client = _httpClientFactory.CreateClient(HttpClientName);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                var currentUser = await new UserClient(client).GetCurrentUserAsync(
                    cancellationToken: cancellationToken);
                if (currentUser.Id == subjectId)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return true;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // Provider payloads and token material must never enter diagnostics.
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        RemoveAdminClaims(principal);
        if (principal.TryGetAdminSubject(out var adminSubject))
        {
            _cache.Remove($"{CacheKeyPrefix}{adminSubject.PartitionKey}");
        }
        return false;
    }

    private static bool HasSingleTokenClaim(JwtSecurityToken token, string type, string expected)
    {
        var claims = token.Claims.Where(claim => claim.Type == type).Take(2).ToArray();
        return claims is [var claim] && string.Equals(claim.Value, expected, StringComparison.Ordinal);
    }

    private async Task<Guid?> SynchronizeUserAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var apiClient = new UserClient(client);
            var response = await apiClient.SyncUserAsync(cancellationToken: cancellationToken);
            return response.Success == true && response.Id is { } internalUserId && internalUserId != Guid.Empty
                ? internalUserId
                : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(
                "BFF admin synchronization completed | Outcome={Outcome} Reason={Reason} Purpose={Purpose} StatusCode={StatusCode}",
                "rejected", "downstream_status", "admin", ex.StatusCode);
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "BFF admin synchronization completed | Outcome={Outcome} Reason={Reason} Purpose={Purpose}",
                "rejected", "timeout", "admin");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "BFF admin synchronization completed | Outcome={Outcome} Reason={Reason} Purpose={Purpose} FailureType={FailureType}",
                "rejected", "exception", "admin", ex.GetType().Name);
            return null;
        }
    }

    private async Task<AdminAuthorityDto?> FetchAdminAuthorityAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var apiClient = new UserClient(client);
            return await apiClient.GetCurrentUserAdminAuthorityAsync(cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "BFF admin authority fetch completed | Outcome={Outcome} Reason={Reason} Purpose={Purpose}",
                "rejected", "timeout", "admin");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "BFF admin authority fetch completed | Outcome={Outcome} Reason={Reason} Purpose={Purpose} FailureType={FailureType}",
                "rejected", "exception", "admin", ex.GetType().Name);
            return null;
        }
    }

    private static void ReplaceAdminClaims(ClaimsPrincipal principal, AdminAuthorityDto authority)
    {
        RemoveAdminClaims(principal);

        if (authority.HasAnyAuthority != true)
        {
            return;
        }

        var identity = new ClaimsIdentity();

        if (authority.IsInstanceAdmin == true)
        {
            identity.AddClaim(new Claim(InstanceAdminClaim, "true"));
        }

        foreach (var tenantId in authority.AdminTenantIds ?? [])
        {
            identity.AddClaim(new Claim(TenantAdminClaim, tenantId.ToString()));
        }

        foreach (var orgId in authority.AdminOrganizationIds ?? [])
        {
            identity.AddClaim(new Claim(OrganizationAdminClaim, orgId.ToString()));
        }

        foreach (var groupId in authority.AdminGroupIds ?? [])
        {
            identity.AddClaim(new Claim(GroupAdminClaim, groupId.ToString()));
        }

        if (identity.Claims.Any())
        {
            principal.AddIdentity(identity);
        }
    }

    private static void ReplaceInternalUserIdClaim(ClaimsPrincipal principal, Guid internalUserId)
    {
        foreach (var identity in principal.Identities)
        {
            foreach (var claim in identity.Claims.Where(claim => claim.Type == InternalUserIdClaim).ToList())
            {
                identity.RemoveClaim(claim);
            }
        }

        principal.AddIdentity(new ClaimsIdentity([new Claim(InternalUserIdClaim, internalUserId.ToString())]));
    }

    private static bool HasConfiguredProvider(
        ClaimsPrincipal principal,
        BffOnboardingStatus status)
    {
        var claims = principal.FindAll("auth_provider").Take(2).ToArray();
        return claims.Length == 1 && status.AllowsProvider(claims[0].Value);
    }

    private static void RemoveAdminClaims(ClaimsPrincipal principal)
    {
        foreach (var identity in principal.Identities)
        {
            foreach (var claim in identity.Claims
                         .Where(c => c.Type is InstanceAdminClaim
                             or TenantAdminClaim
                             or OrganizationAdminClaim
                             or GroupAdminClaim)
                         .ToList())
            {
                identity.RemoveClaim(claim);
            }
        }
    }

    /// <summary>
    /// Invalidates the cached admin authority for the specified user.
    /// Call this after role changes (e.g., onboarding completion) to force a fresh API lookup.
    /// </summary>
    public void InvalidateUser(string userId)
    {
        _cache.Remove($"{CacheKeyPrefix}{userId}");
    }
}

internal sealed record BffAdminAuthorityCacheEntry(AdminAuthorityDto? Authority)
{
    public static readonly BffAdminAuthorityCacheEntry Failure = new((AdminAuthorityDto?)null);

    public static BffAdminAuthorityCacheEntry Success(AdminAuthorityDto authority) => new(authority);
}
