using System.Security.Cryptography;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.ServiceDefaults.Secrets;
using Microsoft.Extensions.Configuration;

namespace Explore.Infrastructure.Secrets;

public sealed class PrivacyIdentityFenceKeyProvider(
    SecretAuthorityAcquisition owner, IConfiguration configuration) : IPrivacyIdentityFenceKeyProvider
{
    public async Task<PrivacyIdentityFenceKey> ResolveAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var prepared = await owner.PrepareAvailableGroupAsync(
            [new("/api", "PRIVACY_ERASURE_IDENTITY_FENCE_KEY", "material"),
             new("/api", "PRIVACY_ERASURE_IDENTITY_FENCE_KEY_ID", "key-id")], cancellationToken);
        if (!prepared.IsSuccess)
            throw new InvalidOperationException("privacy_identity_fence_key_unavailable");
        using var lease = prepared.Value;
        string? encoded = lease.Configuration["material"];
        string? keyId = configuration["PrivacyErasure:IdentityFence:KeyId"] ?? lease.Configuration["key-id"];
        if (string.IsNullOrEmpty(encoded) || string.IsNullOrEmpty(keyId))
            throw new InvalidOperationException("privacy_identity_fence_key_unavailable");
        byte[] material;
        try
        {
            material = Convert.FromBase64String(encoded);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("privacy_identity_fence_key_invalid");
        }
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new PrivacyIdentityFenceKey(keyId, material);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(material);
        }
    }
}
