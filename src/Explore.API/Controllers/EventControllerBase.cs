// ABOUTME: Abstract base controller exposing request-scoped identity and concurrency parsing to API actions.
// ABOUTME: Derives identity from the request principal so no controller resolves services or parses claims.

using Explore.Application.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Controllers;

/// <summary>
/// Identity here is a projection of <see cref="ControllerBase.User"/> through
/// <see cref="PlatformIdentityPrincipalExtensions"/>, not a service the base class resolves for itself. That
/// keeps the single documented fallback chain authoritative while leaving controllers free of an identity
/// constructor dependency they would otherwise all have to thread through.
/// <para>
/// Controllers whose provider subject is not a platform user id — ATProto and Google logins — resolve their
/// local account with <c>mediator.ResolveCurrentUserIdAsync(User, cancellationToken)</c> rather than reading
/// claims themselves.
/// </para>
/// </summary>
public abstract class EventControllerBase : ControllerBase
{
    protected Guid? CurrentUserId => User.GetPlatformUserId();

    /// <exception cref="UnauthorizedAccessException">User is not authenticated.</exception>
    protected Guid RequiredUserId => User.GetRequiredPlatformUserId();

    /// <summary>
    /// Parses a strong <c>If-Match</c> entity tag into a concurrency stamp. Weak validators are rejected
    /// because a weak comparison cannot prove the caller saw the exact version it intends to replace.
    /// </summary>
    protected static bool TryParseConcurrencyStamp(string? ifMatch, out Guid concurrencyStamp)
    {
        concurrencyStamp = default;
        if (string.IsNullOrWhiteSpace(ifMatch))
        {
            return false;
        }

        var value = ifMatch.Trim();
        if (value.Length != 38 || value[0] != '"' || value[^1] != '"')
        {
            return false;
        }

        return Guid.TryParseExact(value.AsSpan(1, 36), "D", out concurrencyStamp)
            && concurrencyStamp != Guid.Empty;
    }
}
