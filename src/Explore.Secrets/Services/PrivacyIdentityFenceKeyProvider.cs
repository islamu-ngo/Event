using System.Security.Cryptography;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Secrets.Configuration;
using Microsoft.Extensions.Configuration;

namespace Explore.Secrets.Services;

public sealed class PrivacyIdentityFenceKeyProvider(IConfiguration configuration) : IPrivacyIdentityFenceKeyProvider
{
    public Task<PrivacyIdentityFenceKey> ResolveAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IConfiguration authority = SecretAuthorityConfiguration.Build(
            configuration, SecretAuthorityConfiguration.GetEnvironmentName(configuration), "/privacy");
        string? encoded = authority["PRIVACY_ERASURE_IDENTITY_FENCE_KEY"];
        string? keyId = configuration["PrivacyErasure:IdentityFence:KeyId"]
            ?? configuration["PRIVACY_ERASURE_IDENTITY_FENCE_KEY_ID"];
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
            return Task.FromResult(new PrivacyIdentityFenceKey(keyId, material));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(material);
        }
    }
}
