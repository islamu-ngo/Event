using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Explore.Application.Authentication;
using Explore.Domain;

namespace Explore.Application.Contracts.PrivacyErasure;

/// <summary>Ephemeral resolved key material; only its purpose-bound commitments may be retained.</summary>
public sealed class PrivacyIdentityFenceKey : IDisposable
{
    private static readonly UTF8Encoding ExactUtf8 = new(false, true);
    private readonly byte[] _material;
    private bool _disposed;

    public PrivacyIdentityFenceKey(string keyId, ReadOnlySpan<byte> material)
    {
        PrivacyIdentityFingerprint.ValidateKeyId(keyId);
        if (material.Length != 32)
            throw new ArgumentException("The privacy identity fence key must contain 32 bytes.", nameof(material));
        KeyId = keyId;
        _material = material.ToArray();
        VerificationTag = Digest("ISLAMU.Event.PrivacyIdentityFence.KeyVerification.v1", keyId);
    }

    public string KeyId { get; }
    public string VerificationTag { get; }

    public PrivacyIdentityFingerprint Fingerprint(ProviderAccountKey accountKey)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        return new PrivacyIdentityFingerprint(accountKey.ProviderKind, KeyId,
            Digest("ISLAMU.Event.PrivacyIdentityFence.Account.v1",
                ((int)accountKey.ProviderKind).ToString(System.Globalization.CultureInfo.InvariantCulture),
                accountKey.Value));
    }

    private string Digest(params string[] fields)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var hash = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, _material);
        Span<byte> length = stackalloc byte[4];
        foreach (string field in fields)
        {
            byte[] bytes = ExactUtf8.GetBytes(field);
            BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
            CryptographicOperations.ZeroMemory(bytes);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(_material);
        _disposed = true;
    }
}
