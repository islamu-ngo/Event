using System.Collections.Immutable;
using System.Security.Claims;
using Explore.Application.Constants;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Authentication;

/// <summary>
/// Successful persisted-key authentication evidence. The server identity binding
/// prevents copied claims or an internal machine execution from becoming this proof.
/// </summary>
public sealed class PersistedApiKeyAuthenticationProof
{
    private readonly ClaimsIdentity _identity;

    public PersistedApiKeyAuthenticationProof(ClaimsIdentity identity, ExternalApiKey authenticatedKey)
    {
        _identity = identity;
        RowId = authenticatedKey.Id;
        KeyId = authenticatedKey.KeyId;
        TenantId = authenticatedKey.TenantId;
        OwnerType = authenticatedKey.OwnerType;
        OwnerId = authenticatedKey.OwnerId;
        StatusId = authenticatedKey.ExternalApiKeyStatusId;
        ExpiresAt = authenticatedKey.ExpiresAt;
        Scopes = authenticatedKey.Scopes.Split([' ', ','],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToImmutableArray();
    }

    public string Origin => "persisted";
    public Guid RowId { get; }
    public string KeyId { get; }
    public Guid? TenantId { get; }
    public ExternalApiKeyOwnerType OwnerType { get; }
    public Guid OwnerId { get; }
    public int StatusId { get; }
    public DateTime? ExpiresAt { get; }
    public ImmutableArray<string> Scopes { get; }

    public bool IsBoundTo(ClaimsPrincipal principal)
    {
        var authenticated = principal.Identities.Where(identity => identity.IsAuthenticated).Take(2).ToArray();
        if (authenticated.Length != 1 || !ReferenceEquals(authenticated[0], _identity)
            || !ReferenceEquals(principal.Identity, _identity)
            || _identity.AuthenticationType != ApiAuthenticationSchemeNames.ApiKey
            || _identity.Claims.Any(claim => claim.Type is "sub" or "sid" or "nameidentifier"
                or "auth_provider" or "iss" or ClaimTypes.NameIdentifier or ClaimTypes.Sid))
            return false;
        var users = _identity.FindAll(PlatformIdentityClaimTypes.InternalUserId).Take(2).ToArray();
        return OwnerType == ExternalApiKeyOwnerType.User
            ? users.Length == 1 && Guid.TryParseExact(users[0].Value, "D", out Guid owner) && owner == OwnerId
            : users.Length == 0;
    }
}
