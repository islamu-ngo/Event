using Explore.Application.Authentication;
using Explore.Domain.Enums;

namespace Explore.Application.Contracts.Services;

/// <summary>Exact authenticated key coordinates, distinct from its owner's human identity.</summary>
public sealed record EventResourceMachinePrincipal(
    string KeyId, Guid? AuthorityTenantId, ExternalApiKeyOwnerType OwnerType, Guid OwnerId,
    ApiKeyCredentialSource CredentialSource)
{
    public static EventResourceMachinePrincipal Capture(ApiKeyPrincipalContext principal) =>
        new(principal.KeyId, principal.TenantId, principal.OwnerType, principal.OwnerId, principal.CredentialSource);
}
