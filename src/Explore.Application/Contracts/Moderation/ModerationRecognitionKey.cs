using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Explore.Application.Authentication;
using Explore.Domain;
using Explore.Domain.Enums;
using ISLAMU.Moderation.Domain.Policies;

namespace Explore.Application.Contracts.Moderation;

/// <summary>
/// Ephemeral secret-authority key material for versioned, purpose-separated recognition HMACs.
/// Native callers must independently qualify the exact provider binding, capture purpose and
/// readable key generation. Computation alone grants no matching or retention authority.
/// </summary>
public sealed class ModerationRecognitionKey : IDisposable
{
    private static readonly UTF8Encoding ExactUtf8 = new(false, true);
    private readonly byte[] material;
    private bool disposed;

    public ModerationRecognitionKey(Guid trustDomainId, string keyId, long keyGeneration, ReadOnlySpan<byte> keyMaterial)
    {
        ModerationHostBinding.RequireUuidV7(trustDomainId, nameof(trustDomainId));
        if (string.IsNullOrEmpty(keyId) || keyId.Length > 64
            || keyId.Any(value => !char.IsAsciiLetterOrDigit(value) && value is not '-' and not '_'))
            throw new ArgumentException("Recognition requires a bounded ASCII key identity.", nameof(keyId));
        if (keyGeneration is < 1 or long.MaxValue || keyMaterial.Length != 32)
            throw new ArgumentException("Recognition requires an exact finite key generation and 32-byte key material.");
        TrustDomainId = trustDomainId;
        KeyId = keyId;
        KeyGeneration = keyGeneration;
        material = keyMaterial.ToArray();
        VerificationTag = Digest("ISLAMU.Moderation.Recognition.Key.v1",
            trustDomainId.ToString("D"), keyId, keyGeneration.ToString(CultureInfo.InvariantCulture));
    }

    public Guid TrustDomainId { get; }
    public string KeyId { get; }
    public long KeyGeneration { get; }
    public string VerificationTag { get; }

    /// <summary>
    /// Preserves the trusted provider account value exactly. The wire format is ordered UTF-8
    /// fields, each prefixed by its four-byte big-endian byte length: domain/version, purpose,
    /// trust UUID, key ID, key-generation decimal, provider-kind decimal and opaque account key.
    /// No local issuer/subject/DID normalization or contact-based inference occurs here.
    /// </summary>
    public ModerationRecognitionToken Compute(ProviderAccountKey accountKey, ModerationReferenceRecognition purpose)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(accountKey);
        if (accountKey.ProviderKind is not (AuthenticationProviderKind.Keycloak or AuthenticationProviderKind.Atproto
            or AuthenticationProviderKind.Google or AuthenticationProviderKind.Local))
            throw new ArgumentException("Recognition requires a supported native provider identity.", nameof(accountKey));
        var purposeField = purpose switch
        {
            ModerationReferenceRecognition.SeparatelyQualifiedFinalAction => "final-action",
            ModerationReferenceRecognition.SeparatelyQualifiedFinalEnrollment => "final-enrollment",
            _ => throw new ArgumentException("Recognition requires a separately qualified final purpose.", nameof(purpose))
        };
        var digest = Digest("ISLAMU.Moderation.Recognition.Account.v1", purposeField,
            TrustDomainId.ToString("D"), KeyId, KeyGeneration.ToString(CultureInfo.InvariantCulture),
            ((int)accountKey.ProviderKind).ToString(CultureInfo.InvariantCulture), accountKey.Value);
        return new(TrustDomainId, accountKey.ProviderKind, purpose, KeyId, KeyGeneration,
            ModerationRecognitionToken.CurrentSerializationVersion, digest);
    }

    private string Digest(params string[] fields)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        using var hash = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, material);
        Span<byte> length = stackalloc byte[4];
        foreach (var field in fields)
        {
            var bytes = new byte[ExactUtf8.GetByteCount(field)];
            try
            {
                ExactUtf8.GetBytes(field, bytes);
                BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
                hash.AppendData(length);
                hash.AppendData(bytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
        var digest = hash.GetHashAndReset();
        try { return Convert.ToHexStringLower(digest); }
        finally { CryptographicOperations.ZeroMemory(digest); }
    }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(material);
        disposed = true;
    }

    public override string ToString() => nameof(ModerationRecognitionKey);
}
