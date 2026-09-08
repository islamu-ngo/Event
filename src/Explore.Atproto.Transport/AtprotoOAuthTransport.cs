namespace Explore.Atproto.Transport;

public sealed class AtprotoOAuthSecurityException(string failureCode, Exception? innerException = null)
    : HttpRequestException("ATProto OAuth security policy rejected the request.", innerException)
{
    public string FailureCode { get; } = failureCode;
}

public enum AtprotoOAuthEndpointKind
{
    PushedAuthorization,
    Token,
    Revocation
}

public sealed record AtprotoAuthorizationServerProfile(
    string Issuer,
    Uri PushedAuthorizationRequestEndpoint,
    Uri TokenEndpoint,
    Uri? RevocationEndpoint);

public sealed record AtprotoOAuthEndpointBinding(string Issuer, AtprotoOAuthEndpointKind Kind);

public sealed class AtprotoAuthorizationServerRegistry(TimeProvider? timeProvider = null, TimeSpan? timeToLive = null)
{
    private readonly object _sync = new();
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly TimeSpan _timeToLive = timeToLive ?? TimeSpan.FromMinutes(5);
    private IReadOnlyDictionary<string, RegisteredEndpoint> _endpoints =
        new Dictionary<string, RegisteredEndpoint>(StringComparer.Ordinal);

    public void Register(AtprotoAuthorizationServerProfile profile)
    {
        var candidates = new List<KeyValuePair<string, AtprotoOAuthEndpointBinding>>
        {
            Candidate(profile.PushedAuthorizationRequestEndpoint, profile.Issuer, AtprotoOAuthEndpointKind.PushedAuthorization),
            Candidate(profile.TokenEndpoint, profile.Issuer, AtprotoOAuthEndpointKind.Token)
        };
        if (profile.RevocationEndpoint is not null)
        {
            candidates.Add(Candidate(
                profile.RevocationEndpoint,
                profile.Issuer,
                AtprotoOAuthEndpointKind.Revocation));
        }

        if (candidates.Select(candidate => candidate.Key).Distinct(StringComparer.Ordinal).Count() != candidates.Count)
        {
            throw new AtprotoOAuthSecurityException("endpoint_kind_collision");
        }

        lock (_sync)
        {
            var now = _timeProvider.GetUtcNow();
            var next = _endpoints
                .Where(entry => entry.Value.ExpiresAt > now)
                .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
            foreach (var candidate in candidates)
            {
                if (next.TryGetValue(candidate.Key, out var existing) && existing.Binding != candidate.Value)
                {
                    throw new AtprotoOAuthSecurityException("endpoint_collision");
                }
            }

            var expiresAt = now + _timeToLive;
            foreach (var candidate in candidates)
            {
                next[candidate.Key] = new(candidate.Value, expiresAt);
            }

            _endpoints = next;
        }
    }

    public bool TryResolve(Uri endpoint, out AtprotoOAuthEndpointBinding binding)
    {
        lock (_sync)
        {
            if (_endpoints.TryGetValue(EndpointKey(endpoint), out var registered)
                && registered.ExpiresAt > _timeProvider.GetUtcNow())
            {
                binding = registered.Binding;
                return true;
            }
        }

        binding = null!;
        return false;
    }

    private static KeyValuePair<string, AtprotoOAuthEndpointBinding> Candidate(
        Uri endpoint,
        string issuer,
        AtprotoOAuthEndpointKind kind) => new(EndpointKey(endpoint), new(issuer, kind));

    private static string EndpointKey(Uri endpoint) =>
        endpoint.GetComponents(UriComponents.AbsoluteUri, UriFormat.UriEscaped);

    private sealed record RegisteredEndpoint(AtprotoOAuthEndpointBinding Binding, DateTimeOffset ExpiresAt);
}
