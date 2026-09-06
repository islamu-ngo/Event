// ABOUTME: Canonical ClaimsPrincipal reading for platform user identity and provider bootstrap identity.
// ABOUTME: Single authority for the documented user-id fallback chain and provider account reconstruction.

using System.Security.Claims;
using Explore.Application.Constants;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;

namespace Explore.Application.Authentication;

/// <summary>
/// The one place that turns an authenticated <see cref="ClaimsPrincipal"/> into platform identity.
/// <para>
/// Identity derivation is a pure function of the principal, so callers that already hold one — controllers
/// through <c>ControllerBase.User</c>, middleware through <c>HttpContext.User</c>, infrastructure through
/// <c>IHttpContextAccessor</c> — never need to resolve a service to ask who the caller is.
/// </para>
/// <para>
/// Purpose-bound schemes (API key, setup secret, managed control plane, ATProto session, privacy-erasure
/// receipt) deliberately do not route through here. Their claims are protocol validation at the
/// authentication boundary, not ambient user identity, and collapsing them together would widen trust.
/// </para>
/// </summary>
public static class PlatformIdentityPrincipalExtensions
{
    private const string SubjectClaimType = "sub";
    private const string SessionIdClaimType = "sid";
    private static readonly HashSet<string> PurposeBoundAuthenticationSchemes =
    [
        ApiAuthenticationSchemeNames.ApiKey,
        ApiAuthenticationSchemeNames.SetupSecret,
        ApiAuthenticationSchemeNames.AdmissionScanner,
        ApiAuthenticationSchemeNames.ManagedControlPlane,
        ApiAuthenticationSchemeNames.AtprotoBootstrap,
        ApiAuthenticationSchemeNames.AtprotoSession,
        ApiAuthenticationSchemeNames.PrivacyErasureReceipt,
    ];

    /// <summary>
    /// Resolves the platform user id using the documented fallback chain
    /// <c>sub → nameidentifier → sid → internal_user_id</c>, accepting only GUID-parseable values.
    /// <para>
    /// The provider claims are tried before <c>internal_user_id</c> on purpose: for platform-managed accounts
    /// the provider subject <em>is</em> the local id, and preferring it keeps one identifier authoritative.
    /// A principal whose provider subject is not a GUID — an ATProto DID or a Google subject — falls through
    /// to <c>internal_user_id</c>, and if that is absent the caller must resolve the local account through
    /// <see cref="CurrentUserResolutionExtensions.ResolveCurrentUserIdAsync"/> instead of guessing.
    /// </para>
    /// </summary>
    public static Guid? GetPlatformUserId(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        ClaimsIdentity? identity = principal.GetAmbientPlatformIdentity();
        if (identity is null)
        {
            return null;
        }

        string?[] candidates =
        [
            identity.FindFirst(SubjectClaimType)?.Value,
            identity.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            identity.FindFirst(SessionIdClaimType)?.Value,
            identity.FindFirst(PlatformIdentityClaimTypes.InternalUserId)?.Value,
        ];

        foreach (var candidate in candidates)
        {
            if (Guid.TryParse(candidate, out var userId))
            {
                return userId;
            }
        }

        return null;
    }

    internal static ClaimsIdentity? GetAmbientPlatformIdentity(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        ClaimsIdentity[] identities = principal.Identities
            .Where(identity => identity.IsAuthenticated)
            .ToArray();

        return identities is
        [
            { AuthenticationType: { } authenticationType } identity
        ] && !PurposeBoundAuthenticationSchemes.Contains(authenticationType)
            ? identity
            : null;
    }

    /// <exception cref="UnauthorizedAccessException">The principal carries no usable platform user id.</exception>
    public static Guid GetRequiredPlatformUserId(this ClaimsPrincipal principal) =>
        principal.GetPlatformUserId()
        ?? throw new UnauthorizedAccessException("User is not authenticated or user ID is not available in the token.");

    public static string? GetEmail(this ClaimsPrincipal principal) =>
        GetClaimValue(RequirePrincipal(principal), "email", ClaimTypes.Email);

    public static string? GetUsername(this ClaimsPrincipal principal) =>
        GetClaimValue(RequirePrincipal(principal), "preferred_username", ClaimTypes.Name);

    public static string? GetFirstName(this ClaimsPrincipal principal) =>
        GetClaimValue(RequirePrincipal(principal), "given_name", ClaimTypes.GivenName);

    public static string? GetLastName(this ClaimsPrincipal principal) =>
        GetClaimValue(RequirePrincipal(principal), "family_name", ClaimTypes.Surname);

    /// <summary>
    /// Reconstructs the external provider account behind this principal for first-login bootstrap and account
    /// sync. Returns <see langword="null"/> when no provider subject is present, which callers must treat as
    /// unauthenticated rather than falling back to a partially populated identity.
    /// </summary>
    public static ProviderIdentity? GetProviderIdentity(this ClaimsPrincipal principal)
    {
        ClaimsIdentity? identity = RequirePrincipal(principal);
        string? subject = GetProviderSubject(identity);
        if (string.IsNullOrWhiteSpace(subject))
        {
            return null;
        }

        string provider = GetAuthProvider(identity);
        ProviderAccountKey? accountKey = GetProviderAccountKey(identity, provider, subject);
        if (accountKey is null)
        {
            return null;
        }

        string email = GetClaimValue(identity, "email", ClaimTypes.Email) ?? string.Empty;
        return new ProviderIdentity(
            subject,
            provider,
            accountKey,
            email,
            GetEmailVerified(identity, provider, email));
    }

    /// <summary>
    /// Returns the provider's stable account subject. Session identifiers are
    /// deliberately excluded because they identify authentication sessions,
    /// not provider accounts.
    /// </summary>
    public static string? GetProviderSubject(this ClaimsPrincipal principal) =>
        GetProviderSubject(RequirePrincipal(principal));

    /// <summary>
    /// Classifies account authority from trusted <c>auth_provider</c> claims or the validated OIDC issuer.
    /// Broker identity-provider hints never replace the authority that issued the account subject.
    /// ATProto identity is accepted only from its verified gateway adapter, never ambient OIDC claims.
    /// </summary>
    public static string GetAuthProvider(this ClaimsPrincipal principal) =>
        GetAuthProvider(RequirePrincipal(principal));

    private static string GetAuthProvider(ClaimsIdentity? identity)
    {
        string? authority = identity?.FindFirst("auth_provider")?.Value
            .Trim()
            .ToLowerInvariant();
        if (authority is "local" or "keycloak" or "atproto" or "google")
        {
            return authority;
        }

        try
        {
            return ClassifyOidcIssuer(NormalizeIssuerAuthority(identity?.FindFirst("iss")?.Value ?? string.Empty))
                .ToAuthenticationProviderCode();
        }
        catch (ArgumentException)
        {
            return AuthSchemeNames.Keycloak.ToLowerInvariant();
        }
    }

    /// <summary>Returns the one canonical persisted key for the authenticated provider account.</summary>
    public static string GetProviderId(this ClaimsPrincipal principal, string providerSubject, string provider) =>
        GetProviderAccountKey(RequirePrincipal(principal), provider, providerSubject)?.Value
        ?? throw new UnauthorizedAccessException("Provider authority is not available in the token.");

    public static ProviderAccountKey CreateOidcAccountKey(string issuer, string subject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        string authority = NormalizeIssuerAuthority(issuer);
        string exactSubject = subject;
        return new ProviderAccountKey(
            ClassifyOidcIssuer(authority),
            $"oidc:{authority.Length}:{authority}:{exactSubject}");
    }

    private static AuthenticationProviderKind ClassifyOidcIssuer(string normalizedIssuer) =>
        string.Equals(normalizedIssuer, "https://accounts.google.com", StringComparison.Ordinal)
            ? AuthenticationProviderKind.Google
            : AuthenticationProviderKind.Keycloak;

    public static ProviderAccountKey CreateAtprotoAccountKey(AtprotoDid did) =>
        new(AuthenticationProviderKind.Atproto, did.Value);

    private static ProviderAccountKey? GetProviderAccountKey(
        ClaimsIdentity? identity,
        string provider,
        string providerSubject)
    {
        if (string.Equals(provider, AuthSchemeNames.Atproto.ToLowerInvariant(), StringComparison.Ordinal))
        {
            string didValue = identity?.FindFirst("did")?.Value
                ?? identity?.FindFirst("atproto_did")?.Value
                ?? providerSubject;
            return AtprotoDid.TryParse(didValue, out AtprotoDid did)
                ? CreateAtprotoAccountKey(did)
                : null;
        }

        string? issuer = identity?.FindFirst("iss")?.Value;
        if (string.IsNullOrWhiteSpace(issuer))
        {
            return null;
        }

        try
        {
            return CreateOidcAccountKey(issuer, providerSubject);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string NormalizeIssuerAuthority(string issuer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        if (!Uri.TryCreate(issuer.Trim(), UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ArgumentException("OIDC issuer authority is invalid.", nameof(issuer));
        }

        var builder = new UriBuilder(uri)
        {
            Scheme = uri.Scheme.ToLowerInvariant(),
            Host = uri.IdnHost.ToLowerInvariant(),
            Path = uri.AbsolutePath.TrimEnd('/'),
            Query = string.Empty,
            Fragment = string.Empty
        };
        if (uri.IsDefaultPort)
        {
            builder.Port = -1;
        }

        return builder.Uri.AbsoluteUri.TrimEnd('/');
    }

    /// <summary>
    /// Honors an explicit <c>email_verified</c> claim, otherwise defaults per provider: the OIDC providers
    /// verify addresses themselves, while ATProto carries no email guarantee and must stay unverified.
    /// </summary>
    public static bool GetEmailVerified(this ClaimsPrincipal principal, string provider, string email) =>
        GetEmailVerified(RequirePrincipal(principal), provider, email);

    private static bool GetEmailVerified(ClaimsIdentity? identity, string provider, string email)
    {
        if (bool.TryParse(identity?.FindFirst("email_verified")?.Value, out var emailVerified))
        {
            return emailVerified;
        }

        return provider switch
        {
            "keycloak" => true,
            "google" => true,
            "atproto" => false,
            _ => !string.IsNullOrWhiteSpace(email),
        };
    }

    private static ClaimsIdentity? RequirePrincipal(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return principal.GetAmbientPlatformIdentity();
    }

    private static string? GetProviderSubject(ClaimsIdentity? identity) =>
        identity?.FindFirst(SubjectClaimType)?.Value
        ?? identity?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    private static string? GetClaimValue(ClaimsIdentity? identity, string protocolType, string frameworkType) =>
        identity?.FindFirst(protocolType)?.Value
        ?? identity?.FindFirst(frameworkType)?.Value;
}

/// <summary>External provider account behind an authenticated principal, used for bootstrap and sync only.</summary>
public sealed record ProviderIdentity(
    string Subject,
    string Provider,
    ProviderAccountKey AccountKey,
    string Email,
    bool EmailVerified)
{
    public string ProviderId => AccountKey.Value;
}
