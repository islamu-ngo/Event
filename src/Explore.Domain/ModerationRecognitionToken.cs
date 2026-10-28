using Explore.Domain.Enums;
using ISLAMU.Moderation.Domain.Policies;

namespace Explore.Domain;

/// <summary>
/// Minimal computed matching material with its exact purpose, trust and key-generation coordinates.
/// It is not proof of native identity, capture permission, current sanction or erasure settlement.
/// </summary>
public sealed record ModerationRecognitionToken
{
    public const int CurrentSerializationVersion = 1;

    public ModerationRecognitionToken(Guid trustDomainId, AuthenticationProviderKind providerKind,
        ModerationReferenceRecognition purpose, string keyId, long keyGeneration, int serializationVersion, string digest)
    {
        ModerationHostBinding.RequireUuidV7(trustDomainId, nameof(trustDomainId));
        if (providerKind is not (AuthenticationProviderKind.Keycloak or AuthenticationProviderKind.Atproto
                or AuthenticationProviderKind.Google or AuthenticationProviderKind.Local)
            || purpose is not (ModerationReferenceRecognition.SeparatelyQualifiedFinalAction
                or ModerationReferenceRecognition.SeparatelyQualifiedFinalEnrollment)
            || keyGeneration is < 1 or long.MaxValue || serializationVersion != CurrentSerializationVersion)
            throw new ArgumentException("Recognition requires an exact supported provider, purpose, key generation and serialization version.");
        if (string.IsNullOrEmpty(keyId) || keyId.Length > 64
            || keyId.Any(value => !char.IsAsciiLetterOrDigit(value) && value is not '-' and not '_'))
            throw new ArgumentException("Recognition requires a bounded ASCII key identity.", nameof(keyId));
        if (digest is null || digest.Length != 64
            || digest.Any(value => value is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new ArgumentException("Recognition requires a canonical SHA-256 HMAC digest.", nameof(digest));
        TrustDomainId = trustDomainId;
        ProviderKind = providerKind;
        Purpose = purpose;
        KeyId = keyId;
        KeyGeneration = keyGeneration;
        SerializationVersion = serializationVersion;
        Digest = digest;
    }

    public Guid TrustDomainId { get; }
    public AuthenticationProviderKind ProviderKind { get; }
    public ModerationReferenceRecognition Purpose { get; }
    public string KeyId { get; }
    public long KeyGeneration { get; }
    public int SerializationVersion { get; }
    public string Digest { get; }
    public override string ToString() => nameof(ModerationRecognitionToken);
}
