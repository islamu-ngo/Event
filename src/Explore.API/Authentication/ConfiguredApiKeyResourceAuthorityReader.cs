using Explore.Application.Authentication;
using Explore.Application.Constants;
using Explore.Application.Contracts.Services;
using Explore.Domain.Enums;
using Microsoft.Extensions.Options;

namespace Explore.API.Authentication;

public sealed class ConfiguredApiKeyResourceAuthorityReader(
    IOptionsMonitor<ApiKeyAuthenticationOptions> options) : IConfiguredApiKeyResourceAuthorityReader
{
    public ValueTask<ApiKeyPrincipalContext?> ReadAsync(
        string keyId, DateTimeOffset evaluationUtc, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(keyId) || evaluationUtc.Offset != TimeSpan.Zero)
            return ValueTask.FromResult<ApiKeyPrincipalContext?>(null);

        ApiKeyClientDescriptor[] candidates = options.Get(ApiAuthenticationSchemeNames.ApiKey).Clients
            .Where(client => string.Equals(client.KeyId, keyId, StringComparison.Ordinal))
            .Take(2).ToArray();
        if (candidates is not [var client] || !client.IsActive || client.TenantId == Guid.Empty
            || client.ExpiresAtUtc is { } expiry && expiry <= evaluationUtc
            || !Enum.TryParse(client.OwnerType, ignoreCase: true, out ExternalApiKeyOwnerType ownerType)
            || !Enum.IsDefined(ownerType)
            || !Guid.TryParse(client.OwnerId, out Guid ownerId) || ownerId == Guid.Empty)
            return ValueTask.FromResult<ApiKeyPrincipalContext?>(null);

        return ValueTask.FromResult<ApiKeyPrincipalContext?>(new(
            client.KeyId, client.TenantId, ownerType, ownerId, client.Scopes.ToArray())
        {
            CredentialSource = ApiKeyCredentialSource.Configured
        });
    }
}
