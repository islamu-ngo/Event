using Explore.Domain.Interfaces;
namespace Explore.Domain;

public sealed class RegistrationSubmissionRevision : ITenantEntity, IAuditableEntity, ISoftDeletable, IConcurrencyAware
{
    private RegistrationSubmissionRevision()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Guid EventId { get; private set; }
    public Guid RegistrationSubmissionId { get; private set; }
    public int RevisionNumber { get; private set; }
    public RegistrationEvidenceHash ReceivedEvidenceHash { get; private set; } = null!;
    public string? ProviderRevisionId { get; private set; }
    public DateTime ReceivedAt { get; private set; }
    public Guid ConcurrencyStamp { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    internal static RegistrationSubmissionRevision Create(
        RegistrationSubmission submission,
        int revisionNumber,
        RegistrationEvidenceHash receivedEvidenceHash,
        DateTime receivedAt,
        string? providerRevisionId)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(receivedEvidenceHash);
        if (revisionNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revisionNumber));
        }

        return new RegistrationSubmissionRevision
        {
            Id = Guid.CreateVersion7(),
            TenantId = submission.TenantId,
            EventId = submission.EventId,
            RegistrationSubmissionId = submission.Id,
            RevisionNumber = revisionNumber,
            ReceivedEvidenceHash = receivedEvidenceHash,
            ProviderRevisionId = NormalizeEvidence(providerRevisionId, nameof(providerRevisionId)),
            ReceivedAt = receivedAt,
            CreatedAt = receivedAt
        };
    }

    private static string? NormalizeEvidence(string? value, string parameterName)
    {
        if (value is null)
        {
            return null;
        }

        string normalized = value.Trim();
        return normalized.Length is > 0 and <= 200
            ? normalized
            : throw new ArgumentException("Provider revision identifiers must be non-blank and bounded.", parameterName);
    }
}
