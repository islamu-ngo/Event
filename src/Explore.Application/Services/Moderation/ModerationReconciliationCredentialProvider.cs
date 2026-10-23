using Explore.Application.Authentication;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Domain.Enums;
using Microsoft.Extensions.Options;

namespace Explore.Application.Services.Moderation;

/// <summary>Authenticates possession, not installation provenance. Maintenance authority proves the latter in the store.</summary>
public sealed class ModerationReconciliationCredentialProvider(
    IOptionsMonitor<ModerationReconciliationOptions> options, IExternalApiKeyRepository keys,
    IModerationReconciliationSecretProvider secrets, TimeProvider clock)
{
    public async Task<ApiKeyPrincipalContext?> AuthenticateAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var settings = options.CurrentValue;
        if (!settings.Enabled || settings.TenantId != tenantId || tenantId == Guid.Empty)
            return null;
        var credential = await secrets.ReadAsync(cancellationToken);
        if (credential is null || !ApiKeyHashing.TryParsePersistedApiKey(credential, out var keyId, out var secret))
            return null;
        var key = await keys.GetByKeyIdForAuthentication(keyId, cancellationToken);
        if (key is null || !string.Equals(key.KeyId, keyId, StringComparison.Ordinal)
            || key.TenantId is { } credentialTenant && credentialTenant != tenantId
            || key.ExternalApiKeyStatusId is not ((int)ExternalApiKeyStatusEnum.Active
                or (int)ExternalApiKeyStatusEnum.PendingRotation)
            || key.ExpiresAt is { } expires && expires <= clock.GetUtcNow().UtcDateTime
            || !ApiKeyHashing.MatchesHash(secret, key.SecretHash))
            return null;
        return new(key.KeyId, key.TenantId, key.OwnerType, key.OwnerId,
            Array.AsReadOnly(key.Scopes.Split([' ', ','],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
        {
            CredentialSource = ApiKeyCredentialSource.Persisted
        };
    }
}
