using Microsoft.AspNetCore.DataProtection.KeyManagement;

namespace Explore.ServiceDefaults.DataProtection;

internal sealed class ExternalDataProtectionKeyManager(ExternalDataProtectionProvider provider) : IKeyManager
{
    public IReadOnlyCollection<IKey> GetAllKeys()
    {
        provider.EnsureAdmitted();
        return provider.GetRetainedKeys();
    }

    public CancellationToken GetCacheExpirationToken() => CancellationToken.None;

    public IKey CreateNewKey(DateTimeOffset activationDate, DateTimeOffset expirationDate) => throw WriteForbidden();
    public void RevokeKey(Guid keyId, string? reason = null) => throw WriteForbidden();
    public void RevokeAllKeys(DateTimeOffset revocationDate, string? reason = null) => throw WriteForbidden();

    private static InvalidOperationException WriteForbidden() => new("data_protection_runtime_write_forbidden");
}
