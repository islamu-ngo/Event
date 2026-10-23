using System.Runtime.CompilerServices;
using System.Security.Claims;
using Explore.Application.Contracts.Infrastructure;
using System.Text.Json.Serialization;

// The API owns cryptographic authentication and is the issuer of this in-process observation.
[assembly: InternalsVisibleTo("Explore.API")]

namespace Explore.Application.Authentication;

public interface IHumanAuthenticationLifetimeObserver
{
    /// <summary>
    /// Returns the current request's validated credential lifetime, or null when authentication,
    /// identity custody or finite lifetime evidence is absent. Call before preparing an action.
    /// </summary>
    ValueTask<HumanAuthenticationLifetime?> ObserveAsync(
        ClaimsPrincipal principal, CancellationToken cancellationToken);
}

/// <summary>
/// Immutable credential evidence, not account resolution or an authorization grant. The parent
/// binds AccountKey to its resolved subject and rechecks IsValidAt after its final blocking operation.
/// </summary>
public sealed class HumanAuthenticationLifetime
{
    internal HumanAuthenticationLifetime(string authenticationScheme, ProviderAccountKey accountKey,
        DateTimeOffset? notBefore, DateTimeOffset expiresAt, TimeSpan clockSkew,
        LocalSessionAuthority? localSession = null)
    {
        AuthenticationScheme = authenticationScheme;
        AccountKey = accountKey;
        NotBefore = notBefore;
        ExpiresAt = expiresAt;
        ClockSkew = clockSkew;
        LocalSession = localSession;
    }

    [JsonIgnore] public string AuthenticationScheme { get; }
    [JsonIgnore] public ProviderAccountKey AccountKey { get; }
    [JsonIgnore] public DateTimeOffset? NotBefore { get; }
    [JsonIgnore] public DateTimeOffset ExpiresAt { get; }
    [JsonIgnore] public TimeSpan ClockSkew { get; }
    [JsonIgnore] public LocalSessionAuthority? LocalSession { get; }

    /// <summary>
    /// Preserves the validating scheme's inclusive clock-skew tolerance. The allowance is anchored
    /// to the original signed expiry, never to preparation, provider completion or transaction time.
    /// </summary>
    public bool IsValidAt(DateTimeOffset now) =>
        (!NotBefore.HasValue || now >= NotBefore.Value.Subtract(ClockSkew))
        && now <= ExpiresAt.Add(ClockSkew);

    public override string ToString() => nameof(HumanAuthenticationLifetime);
}
