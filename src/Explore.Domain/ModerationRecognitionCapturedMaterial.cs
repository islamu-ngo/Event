using System.Security.Cryptography;
using ISLAMU.Moderation.Domain;

namespace Explore.Domain;

/// <summary>
/// Complete computed material for one original purpose-limited contribution. Native pending
/// capture must durably receipt every such item before destroying its provider inputs.
/// This value contains neither provider identifier/key bytes nor erasure, matching or retention authority.
/// </summary>
public sealed record ModerationRecognitionCapturedMaterial
{
    public ModerationRecognitionCapturedMaterial(ModerationRecognitionToken token,
        string keyVerificationTag, ModerationRecognitionContribution contribution)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(contribution);
        if (token.TrustDomainId != contribution.Scope.TrustDomainId || token.Purpose != contribution.Purpose
            || keyVerificationTag is not { Length: 64 }
            || keyVerificationTag.Any(value => value is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new ArgumentException("Computed capture requires exact contribution purpose/trust and canonical original material identity.");
        Token = token;
        KeyVerificationTag = keyVerificationTag;
        Contribution = contribution;
    }

    public ModerationRecognitionToken Token { get; }
    public string KeyVerificationTag { get; }
    public ModerationRecognitionContribution Contribution { get; }

    /// <summary>
    /// Compares only independently computed original-generation material and current canonical
    /// contribution meaning. The native caller separately qualifies identity proof, settled erasure,
    /// current source/adoption, target jurisdiction, readable ring and retained fences.
    /// Neither a pending receipt nor this comparison authorizes admission or history restoration.
    /// </summary>
    public bool MatchesCurrent(ModerationRecognitionToken computed, string keyVerificationTag,
        ModerationDecision decision, ModerationSanction sanction, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(computed);
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(sanction);
        if (computed.TrustDomainId != Token.TrustDomainId || computed.ProviderKind != Token.ProviderKind
            || computed.Purpose != Token.Purpose || computed.KeyId != Token.KeyId
            || computed.KeyGeneration != Token.KeyGeneration || computed.SerializationVersion != Token.SerializationVersion
            || keyVerificationTag is not { Length: 64 }
            || keyVerificationTag.Any(value => value is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            return false;
        return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(computed.Digest), Convert.FromHexString(Token.Digest))
            && CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(keyVerificationTag), Convert.FromHexString(KeyVerificationTag))
            && Contribution.MatchesCurrent(decision, sanction, now);
    }

    public override string ToString() => nameof(ModerationRecognitionCapturedMaterial);
}
