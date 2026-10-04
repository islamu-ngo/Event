using System.Security.Cryptography;
using Explore.Application.Contracts.PrivacyErasure;

namespace Event.Api.IntegrationTests.Fixtures;

/// <summary>Retains one dynamic key for the lifetime of a fixture's database.</summary>
internal sealed class TestPrivacyIdentityFenceKeyProvider : IPrivacyIdentityFenceKeyProvider, IDisposable
{
    private readonly byte[] _material = RandomNumberGenerator.GetBytes(32);
    private readonly string _keyId = Guid.CreateVersion7().ToString("N");
    public string AuthorityPath { get; } = Path.Join(
        Path.GetTempPath(), $"api-identity-authority-{Guid.CreateVersion7():N}", "authority.db");

    public Task<PrivacyIdentityFenceKey> ResolveAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new PrivacyIdentityFenceKey(_keyId, _material));
    }

    public void Dispose() => CryptographicOperations.ZeroMemory(_material);
}
