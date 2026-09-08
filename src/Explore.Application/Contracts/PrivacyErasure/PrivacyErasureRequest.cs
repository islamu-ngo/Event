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
        int policyVersion)
    {
        Validate(intentId, subjectKind, subjectId, reasonCode, policyVersion);

        IntentId = intentId;
        SubjectKind = subjectKind;
        SubjectId = subjectId;
        ReasonCode = reasonCode;
        PolicyVersion = policyVersion;
    }

    public Guid IntentId { get; }
    public PrivacyErasureSubjectKind SubjectKind { get; }
    public Guid SubjectId { get; }
    public PrivacyErasureReasonCode ReasonCode { get; }
    public int PolicyVersion { get; }

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
