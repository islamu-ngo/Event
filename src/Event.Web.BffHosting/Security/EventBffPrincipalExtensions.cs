using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Event.Web.BffHosting.Security;

public enum EventBffOpaqueIdentityPurpose
{
    OpaqueProviderSubject,
    SessionId,
    RatePartition,
    SetupSession,
    CircuitSubject,
    AdminSubject,
    SessionRefreshSubject
}

public enum EventBffOpaqueIdentitySource
{
    ProviderSubject,
    SessionId
}

public readonly record struct EventBffOpaqueIdentity(
    string AuthenticationScheme,
    string Value,
    EventBffOpaqueIdentityPurpose Purpose,
    EventBffOpaqueIdentitySource Source)
{
    public string PartitionKey
    {
        get
        {
            var material = string.Concat(
                AuthenticationScheme,
                "\u001f",
                Purpose.ToString(),
                "\u001f",
                Source.ToString(),
                "\u001f",
                Value);
            return string.Concat(
                AuthenticationScheme,
                ":",
                Purpose.ToString(),
                ":",
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))));
        }
    }

    public override string ToString() =>
        string.Concat(AuthenticationScheme, ":", Purpose.ToString(), ":", Source.ToString());
}

public static class EventBffPrincipalExtensions
{
    private static readonly string[] GovernedClaimTypes =
        [JwtRegisteredClaimNames.Sub, ClaimTypes.NameIdentifier, JwtRegisteredClaimNames.Sid];

    public static bool TryGetOpaqueProviderSubject(
        this ClaimsPrincipal? principal,
        out EventBffOpaqueIdentity identity) =>
        TryResolve(
            principal,
            EventBffOpaqueIdentityPurpose.OpaqueProviderSubject,
            allowSessionFallback: false, out identity);

    public static bool TryGetSessionId(
        this ClaimsPrincipal? principal,
        out EventBffOpaqueIdentity identity) =>
        TryResolveSession(
            principal,
            EventBffOpaqueIdentityPurpose.SessionId,
            out identity);

    public static bool TryGetRatePartitionIdentity(
        this ClaimsPrincipal? principal,
        out EventBffOpaqueIdentity identity) =>
        TryResolve(
            principal,
            EventBffOpaqueIdentityPurpose.RatePartition,
            allowSessionFallback: true, out identity);

    public static bool TryGetSetupSessionIdentity(
        this ClaimsPrincipal? principal,
        out EventBffOpaqueIdentity identity) =>
        TryResolve(
            principal,
            EventBffOpaqueIdentityPurpose.SetupSession,
            allowSessionFallback: true, out identity);

    public static bool TryGetCircuitSubject(
        this ClaimsPrincipal? principal,
        out EventBffOpaqueIdentity identity) =>
        TryResolve(
            principal,
            EventBffOpaqueIdentityPurpose.CircuitSubject,
            allowSessionFallback: true, out identity);

    public static bool TryGetAdminSubject(
        this ClaimsPrincipal? principal,
        out EventBffOpaqueIdentity identity) =>
        TryResolve(
            principal,
            EventBffOpaqueIdentityPurpose.AdminSubject,
            allowSessionFallback: true, out identity);

    public static bool TryGetSessionRefreshSubject(
        this ClaimsPrincipal? principal,
        out EventBffOpaqueIdentity identity) =>
        TryResolve(
            principal,
            EventBffOpaqueIdentityPurpose.SessionRefreshSubject,
            allowSessionFallback: true, out identity);

    private static bool TryResolve(
        ClaimsPrincipal? principal,
        EventBffOpaqueIdentityPurpose purpose,
        bool allowSessionFallback,
        out EventBffOpaqueIdentity identity)
    {
        identity = default;
        if (!TryGetTrustedIdentity(principal, out var claimsIdentity))
        {
            return false;
        }

        var subject = ResolveSubject(claimsIdentity);
        if (subject.State == ClaimResolutionState.Invalid)
        {
            return false;
        }

        if (subject.State == ClaimResolutionState.Found)
        {
            identity = new(
                claimsIdentity.AuthenticationType!,
                subject.Value,
                purpose,
                EventBffOpaqueIdentitySource.ProviderSubject);
            return true;
        }

        return allowSessionFallback && TryResolveSession(claimsIdentity, purpose, out identity);
    }

    private static bool TryResolveSession(
        ClaimsPrincipal? principal,
        EventBffOpaqueIdentityPurpose purpose,
        out EventBffOpaqueIdentity identity)
    {
        identity = default;
        return TryGetTrustedIdentity(principal, out var claimsIdentity)
            && TryResolveSession(claimsIdentity, purpose, out identity);
    }

    private static bool TryResolveSession(
        ClaimsIdentity claimsIdentity,
        EventBffOpaqueIdentityPurpose purpose,
        out EventBffOpaqueIdentity identity)
    {
        identity = default;
        var session = ResolveSingle(claimsIdentity, JwtRegisteredClaimNames.Sid);
        if (session.State != ClaimResolutionState.Found)
        {
            return false;
        }

        identity = new(
            claimsIdentity.AuthenticationType!,
            session.Value,
            purpose,
            EventBffOpaqueIdentitySource.SessionId);
        return true;
    }

    private static ClaimResolution ResolveSubject(ClaimsIdentity identity)
    {
        var sub = ResolveSingle(identity, JwtRegisteredClaimNames.Sub);
        var nameIdentifier = ResolveSingle(identity, ClaimTypes.NameIdentifier);
        if (sub.State == ClaimResolutionState.Invalid
            || nameIdentifier.State == ClaimResolutionState.Invalid)
        {
            return ClaimResolution.Invalid;
        }

        if (sub.State == ClaimResolutionState.Found
            && nameIdentifier.State == ClaimResolutionState.Found
            && !string.Equals(sub.Value, nameIdentifier.Value, StringComparison.Ordinal))
        {
            return ClaimResolution.Invalid;
        }

        return sub.State == ClaimResolutionState.Found ? sub : nameIdentifier;
    }

    private static ClaimResolution ResolveSingle(ClaimsIdentity identity, string claimType)
    {
        var claims = identity.FindAll(claimType).Take(2).ToArray();
        if (claims.Length > 1 || claims.Length == 1 && !IsValidOpaqueValue(claims[0].Value))
        {
            return ClaimResolution.Invalid;
        }

        return claims.Length == 1
            ? ClaimResolution.Found(claims[0].Value)
            : ClaimResolution.Missing;
    }

    private static bool TryGetTrustedIdentity(
        ClaimsPrincipal? principal,
        out ClaimsIdentity identity)
    {
        identity = null!;
        if (principal is null)
        {
            return false;
        }

        var authenticated = principal.Identities.Where(candidate => candidate.IsAuthenticated).Take(2).ToArray();
        if (authenticated.Length != 1 || !IsTrustedBffScheme(authenticated[0].AuthenticationType))
        {
            return false;
        }

        if (principal.Identities
            .Where(candidate => !candidate.IsAuthenticated)
            .SelectMany(candidate => candidate.Claims)
            .Any(claim => GovernedClaimTypes.Contains(claim.Type, StringComparer.Ordinal)))
        {
            return false;
        }

        identity = authenticated[0];
        return true;
    }

    private static bool IsTrustedBffScheme(string? scheme) =>
        scheme is CookieAuthenticationDefaults.AuthenticationScheme
            or Event.Web.BffHosting.Authentication.EventBffAuthenticationSchemes.Keycloak
            or Event.Web.BffHosting.Authentication.EventBffAuthenticationSchemes.Google
            or Event.Web.BffHosting.Authentication.EventBffAuthenticationSchemes.Atproto;

    private static bool IsValidOpaqueValue(string value) =>
        !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsControl);

    private enum ClaimResolutionState
    {
        Missing,
        Found,
        Invalid
    }

    private readonly record struct ClaimResolution(ClaimResolutionState State, string Value)
    {
        public static ClaimResolution Missing => new(ClaimResolutionState.Missing, string.Empty);
        public static ClaimResolution Invalid => new(ClaimResolutionState.Invalid, string.Empty);
        public static ClaimResolution Found(string value) => new(ClaimResolutionState.Found, value);
    }
}
