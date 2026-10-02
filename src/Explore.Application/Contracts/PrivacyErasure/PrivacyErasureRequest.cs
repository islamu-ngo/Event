using System.Text.Json.Serialization;
using Explore.Domain;

namespace Explore.Application.Contracts.PrivacyErasure;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PrivacyErasureRequest
{
    [JsonConstructor]
    public PrivacyErasureRequest(
        Guid intentId,
        PrivacyErasureSubjectKind subjectKind,
        Guid subjectId,
        PrivacyErasureReasonCode reasonCode,
        int policyVersion,
        IReadOnlyList<PrivacyIdentityFingerprint>? identityFences = null,
        string? identityKeyId = null,
        string? identityKeyVerificationTag = null)
    {
        Validate(intentId, subjectKind, subjectId, reasonCode, policyVersion);

        IntentId = intentId;
        SubjectKind = subjectKind;
        SubjectId = subjectId;
        ReasonCode = reasonCode;
        PolicyVersion = policyVersion;
        IdentityFences = Array.AsReadOnly((identityFences ?? []).Distinct().ToArray());
        if (identityKeyId is not null || identityKeyVerificationTag is not null || IdentityFences.Count > 0)
        {
            PrivacyIdentityFingerprint.ValidateKeyId(identityKeyId!);
            PrivacyIdentityFingerprint.ValidateDigest(identityKeyVerificationTag!);
            if (IdentityFences.Any(fence => fence.KeyId != identityKeyId))
                throw new ArgumentException("Identity fence key metadata does not match.");
        }
        IdentityKeyId = identityKeyId;
        IdentityKeyVerificationTag = identityKeyVerificationTag;
    }

    public Guid IntentId { get; }
    public PrivacyErasureSubjectKind SubjectKind { get; }
    public Guid SubjectId { get; }
    public PrivacyErasureReasonCode ReasonCode { get; }
    public int PolicyVersion { get; }
    public IReadOnlyList<PrivacyIdentityFingerprint> IdentityFences { get; }
    public string? IdentityKeyId { get; }
    public string? IdentityKeyVerificationTag { get; }

    public static PrivacyErasureRequest Create(
        Guid intentId,
        PrivacyErasureSubjectKind subjectKind,
        Guid subjectId,
        PrivacyErasureReasonCode reasonCode,
        int policyVersion) =>
        new(intentId, subjectKind, subjectId, reasonCode, policyVersion);

    private static void Validate(
        Guid intentId,
        PrivacyErasureSubjectKind subjectKind,
        Guid subjectId,
        PrivacyErasureReasonCode reasonCode,
        int policyVersion)
    {
        if (intentId == Guid.Empty || intentId.Version != 7 || intentId.Variant is < 8 or > 11)
        {
            throw new ArgumentException(
                "Erasure intent idempotency keys must be non-empty RFC 4122 UUIDv7 values.",
                nameof(intentId));
        }

        if (subjectKind != PrivacyErasureSubjectKind.User)
        {
            throw new ArgumentOutOfRangeException(nameof(subjectKind), "Only User privacy erasure is executable.");
        }

        if (subjectId == Guid.Empty)
        {
            throw new ArgumentException("Subject id is required.", nameof(subjectId));
        }

        if (!Enum.IsDefined(reasonCode))
        {
            throw new ArgumentOutOfRangeException(nameof(reasonCode));
        }

        if (policyVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(policyVersion), "Policy version must be positive.");
        }
    }
}
