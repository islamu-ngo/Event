using Explore.Application.Authentication;

namespace Explore.Application.Contracts.Identity;

/// <summary>
/// Exposes the machine principal derived from the current HTTP request's authenticated ClaimsPrincipal
/// when authentication was performed via an external API key. Returns <c>null</c> values and <c>false</c>
/// for user-authenticated or anonymous callers so authorization code can branch safely.
/// </summary>
/// <remarks>
/// This accessor is the single source of truth for "is the caller a machine" across both the Cerbos
/// <c>CerbosAuthorizationService</c> and the database-driven <c>FallbackAuthorizationService</c>.
/// Scoped per request. Not safe to cache beyond request lifetime because the underlying HttpContext changes.
/// </remarks>
public interface IMachinePrincipalAccessor
{
    /// <summary>
    /// The API-key-derived principal context for the current request, or <c>null</c> when the caller is
    /// not authenticated via an external API key (JWT / anonymous / unknown schemes).
    /// </summary>
    ApiKeyPrincipalContext? Current { get; }

    /// <summary>
    /// Convenience flag: <c>true</c> when <see cref="Current"/> is not <c>null</c>.
    /// </summary>
    bool IsMachineCaller { get; }
}
