namespace Explore.Domain;

public sealed class PrivacyErasurePolicyCoverage
{
    private PrivacyErasurePolicyCoverage()
    {
    }

    public Guid IntentId { get; private set; }
    public PrivacyErasureSubjectKind SubjectKind { get; private set; }
    public int PolicyVersion { get; private set; }
    public DateTime CoveredAtUtc { get; private set; }

    public static PrivacyErasurePolicyCoverage Record(
        PrivacyErasureIntent intent,
        int policyVersion,
        DateTime coveredAtUtc)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (policyVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(policyVersion));
        }

        if (coveredAtUtc == default || coveredAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Coverage timestamp must be a non-default UTC value.", nameof(coveredAtUtc));
        }

        if (coveredAtUtc < intent.RecordedAtUtc)
        {
            throw new ArgumentException("Coverage cannot precede the retained fact.", nameof(coveredAtUtc));
        }

        return new PrivacyErasurePolicyCoverage
        {
            IntentId = intent.IntentId,
            SubjectKind = intent.SubjectKind,
            PolicyVersion = policyVersion,
            CoveredAtUtc = coveredAtUtc
        };
    }
}
