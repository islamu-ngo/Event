namespace Explore.Domain;

public sealed class PrivacyErasureIntent
{
    private PrivacyErasureIntent()
    {
    }

    public Guid IntentId { get; private set; }
    public long AuthoritySequence { get; private set; }
    public PrivacyErasureSubjectKind SubjectKind { get; private set; }
    public Guid SubjectId { get; private set; }
    public PrivacyErasureReasonCode ReasonCode { get; private set; }
    public int PolicyVersion { get; private set; }
    public DateTime RequestedAtUtc { get; private set; }
    public DateTime RecordedAtUtc { get; private set; }
    public DateTime RetentionExpiresAtUtc { get; private set; }
    public bool IsLegalHoldPseudonymized { get; private set; }

    public static PrivacyErasureIntent Record(
        Guid intentId,
        long authoritySequence,
        PrivacyErasureSubjectKind subjectKind,
        Guid subjectId,
        PrivacyErasureReasonCode reasonCode,
        int policyVersion,
        DateTime requestedAtUtc,
        DateTime recordedAtUtc,
        DateTime? retentionExpiresAtUtc = null)
    {
        if (!IsUuidVersion7(intentId))
        {
            throw new ArgumentException(
                "Erasure intent idempotency keys must be non-empty RFC 4122 UUIDv7 values.",
                nameof(intentId));
        }

        if (authoritySequence <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(authoritySequence), "Authority sequence must be positive.");
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

        RequireUtc(requestedAtUtc, nameof(requestedAtUtc));
        RequireUtc(recordedAtUtc, nameof(recordedAtUtc));
        if (recordedAtUtc < requestedAtUtc)
        {
            throw new ArgumentException("Authority recording cannot precede the request.", nameof(recordedAtUtc));
        }

        DateTime retentionExpiry = retentionExpiresAtUtc
            ?? DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);
        RequireUtc(retentionExpiry, nameof(retentionExpiresAtUtc));
        if (retentionExpiry <= recordedAtUtc)
        {
            throw new ArgumentException(
                "Authority retention must extend beyond the recording timestamp.",
                nameof(retentionExpiresAtUtc));
        }

        return new PrivacyErasureIntent
        {
            IntentId = intentId,
            AuthoritySequence = authoritySequence,
            SubjectKind = subjectKind,
            SubjectId = subjectId,
            ReasonCode = reasonCode,
            PolicyVersion = policyVersion,
            RequestedAtUtc = requestedAtUtc,
            RecordedAtUtc = recordedAtUtc,
            RetentionExpiresAtUtc = retentionExpiry
        };
    }

    public void PseudonymizeForLegalHold(Guid intentAuditToken, Guid subjectAuditToken)
    {
        if (intentAuditToken == Guid.Empty)
        {
            throw new ArgumentException("Intent audit token is required.", nameof(intentAuditToken));
        }

        if (subjectAuditToken == Guid.Empty)
        {
            throw new ArgumentException("Subject audit token is required.", nameof(subjectAuditToken));
        }

        IntentId = intentAuditToken;
        SubjectId = subjectAuditToken;
        IsLegalHoldPseudonymized = true;
    }

    private static bool IsUuidVersion7(Guid value) =>
        value != Guid.Empty && value.Version == 7 && value.Variant is >= 8 and <= 11;

    private static void RequireUtc(DateTime value, string parameterName)
    {
        if (value == default || value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Timestamp must be a non-default UTC value.", parameterName);
        }
    }
}
