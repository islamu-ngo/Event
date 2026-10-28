using Explore.Application.Authentication;

namespace Explore.API.Authentication;

/// <summary>
/// Server-issued receipt of the exact configured source used for successful authentication.
/// It contains no raw credential and is not an authorization grant or transferable source lease.
/// </summary>
public sealed class ConfiguredApiKeyAuthenticationOrigin
{
    internal ConfiguredApiKeyAuthenticationOrigin(
        ApiKeyAuthenticationOptions source, ApiKeyClientDescriptor descriptor,
        string verifiedHash, ApiKeyPrincipalContext principal, DateTimeOffset? expiresAtUtc,
        TimeProvider clock, long timestamp, DateTimeOffset observedAtUtc)
    {
        Source = source;
        Descriptor = descriptor;
        VerifiedHash = verifiedHash;
        Principal = principal with { Scopes = Array.AsReadOnly(principal.Scopes.ToArray()) };
        ExpiresAtUtc = expiresAtUtc?.ToUniversalTime();
        Clock = clock;
        Timestamp = timestamp;
        ObservedAtUtc = observedAtUtc;
    }

    public ApiKeyPrincipalContext Principal { get; }
    public DateTimeOffset? ExpiresAtUtc { get; }
    internal ApiKeyAuthenticationOptions Source { get; }
    internal ApiKeyClientDescriptor Descriptor { get; }
    internal string VerifiedHash { get; }
    internal TimeProvider Clock { get; }
    internal long Timestamp { get; }
    internal DateTimeOffset ObservedAtUtc { get; }

    public override string ToString() => nameof(ConfiguredApiKeyAuthenticationOrigin);
}
