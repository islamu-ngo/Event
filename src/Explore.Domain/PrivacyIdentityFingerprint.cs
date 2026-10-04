using Explore.Domain.Enums;

namespace Explore.Domain;

/// <summary>A purpose-bound digest, never an external account identifier.</summary>
public sealed record PrivacyIdentityFingerprint
{
    public PrivacyIdentityFingerprint(AuthenticationProviderKind identityKind, string keyId, string fingerprint)
    {
        if (!Enum.IsDefined(identityKind))
            throw new ArgumentOutOfRangeException(nameof(identityKind));
        ValidateKeyId(keyId);
        ValidateDigest(fingerprint);
        IdentityKind = identityKind;
        KeyId = keyId;
        Fingerprint = fingerprint;
    }

    public AuthenticationProviderKind IdentityKind { get; }
    public string KeyId { get; }
    public string Fingerprint { get; }

    public static void ValidateKeyId(string keyId)
    {
        if (string.IsNullOrEmpty(keyId) || keyId.Length > 64
            || keyId.Any(value => !char.IsAsciiLetterOrDigit(value) && value is not '-' and not '_'))
            throw new ArgumentException("A bounded ASCII identity fence key id is required.", nameof(keyId));
    }

    public static void ValidateDigest(string digest)
    {
        if (digest is null || digest.Length != 64
            || digest.Any(value => value is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new ArgumentException("An identity fence requires a SHA-256 digest.", nameof(digest));
    }
}
