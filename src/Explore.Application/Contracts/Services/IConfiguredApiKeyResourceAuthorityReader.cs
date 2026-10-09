using Explore.Application.Authentication;

namespace Explore.Application.Contracts.Services;

/// <summary>Reads current configured-key metadata without exposing credential material or falling back to persisted keys.</summary>
public interface IConfiguredApiKeyResourceAuthorityReader
{
    ValueTask<ApiKeyPrincipalContext?> ReadAsync(
        string keyId, DateTimeOffset evaluationUtc, CancellationToken cancellationToken);
}
