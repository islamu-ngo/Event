using System.Security.Cryptography;
using System.Text;

namespace Explore.Domain;

/// <summary>
/// Immutable native acceptance of an authenticated measurement receipt. Retains the exact signed
/// assertion and original acceptance metadata, not proof of honest execution, conformance or permission.
/// Expiry or later withdrawal of source authority never rewrites this historical record.
/// </summary>
public sealed class ModerationConsumerMeasurementReceipt
{
    private ModerationConsumerMeasurementReceipt() { }

    public Guid Id { get; private set; }
    public Guid HostBindingId { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ChallengeId { get; private set; }
    public Guid IssuerEnrollmentId { get; private set; }
    public Guid IssuerKeyId { get; private set; }
    public Guid RunId { get; private set; }
    public string SignedToken { get; private set; } = string.Empty;
    public string TokenDigest { get; private set; } = string.Empty;
    public Guid AcceptedByHumanId { get; private set; }
    public long WriterGeneration { get; private set; }
    public long Revision { get; private set; }
    public DateTimeOffset AcceptedAt { get; private set; }
    public DateTimeOffset ValidUntil { get; private set; }

    /// <summary>
    /// Records native acceptance only after the accepting repository independently authenticates
    /// the signature and exact receipt coordinates and requalifies the current challenge,
    /// installation, issuer enrollment/key and publication within the accepting transaction.
    /// The owner must resolve an accountable native human, allocate a non-max writer generation,
    /// enforce one acceptance per challenge and run, and bound validity by every current source
    /// and the signed receipt. Id is the exact signed receipt ID, never a newly allocated substitute.
    /// This operation checks scalar spelling and integrity only; it neither verifies signatures
    /// nor derives a passing result or continuing authority from token presence.
    /// </summary>
    public static ModerationConsumerMeasurementReceipt Accept(
        Guid id, Guid hostBindingId, Guid trustDomainId, Guid tenantId, Guid challengeId,
        Guid issuerEnrollmentId, Guid issuerKeyId, Guid runId, string signedToken, string tokenDigest,
        Guid acceptedByHumanId, long writerGeneration, DateTimeOffset acceptedAt, DateTimeOffset validUntil)
    {
        var receipt = new ModerationConsumerMeasurementReceipt
        {
            Id = id,
            HostBindingId = hostBindingId,
            TrustDomainId = trustDomainId,
            TenantId = tenantId,
            ChallengeId = challengeId,
            IssuerEnrollmentId = issuerEnrollmentId,
            IssuerKeyId = issuerKeyId,
            RunId = runId,
            SignedToken = signedToken,
            TokenDigest = tokenDigest,
            AcceptedByHumanId = acceptedByHumanId,
            WriterGeneration = writerGeneration,
            Revision = 1,
            AcceptedAt = acceptedAt.ToUniversalTime(),
            ValidUntil = validUntil.ToUniversalTime()
        };
        receipt.ValidatePersistedState();
        return receipt;
    }

    public void ValidatePersistedState()
    {
        foreach (var id in new[] { Id, HostBindingId, TrustDomainId, TenantId, ChallengeId,
            IssuerEnrollmentId, IssuerKeyId, RunId })
            ModerationHostBinding.RequireUuidV7(id, nameof(Id));
        if (AcceptedByHumanId == Guid.Empty || WriterGeneration is < 1 or long.MaxValue || Revision != 1)
            throw new ArgumentException("Measurement acceptance requires an accountable native human and immutable original finite generation.");
        if (!CompactToken(SignedToken) || TokenDigest is not { Length: 64 }
            || !TokenDigest.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f')
            || TokenDigest != Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(SignedToken))))
            throw new ArgumentException("Measurement acceptance requires exact bounded compact-JWS bytes and their canonical SHA256 digest.");
        ModerationQualificationPolicySource.RequireValidity(AcceptedAt, ValidUntil);
    }

    private static bool CompactToken(string? token)
    {
        if (token is not { Length: > 0 and <= 65536 })
            return false;
        var parts = token.Split('.');
        if (parts.Length != 3)
            return false;
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
        foreach (var part in parts)
        {
            if (part.Length == 0 || part.Length % 4 == 1
                || !part.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))
                return false;
            // Unpadded base64url has zero unused bits; aliases must not acquire a second byte identity.
            int last = alphabet.IndexOf(part[^1], StringComparison.Ordinal);
            if (part.Length % 4 == 2 && (last & 15) != 0
                || part.Length % 4 == 3 && (last & 3) != 0)
                return false;
        }
        return true;
    }

    public override string ToString() => nameof(ModerationConsumerMeasurementReceipt);
}
