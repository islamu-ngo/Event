using ISLAMU.Moderation.Domain;

namespace Explore.Domain;

/// <summary>Current PII-owned collection provenance, not age evidence or reusable permission.</summary>
public sealed class UserBirthDateCollectionReceipt
{
    private UserBirthDateCollectionReceipt() { }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid ObservationId { get; private set; }
    public Guid AssessmentId { get; private set; }
    public Guid AssessmentAdoptionId { get; private set; }
    public Guid? ConsentId { get; private set; }
    public Guid PolicyId { get; private set; }
    public long PolicyRevision { get; private set; }
    public Guid PublicationId { get; private set; }
    public Guid ProfileId { get; private set; }
    public DateTimeOffset CollectedAt { get; private set; }
    public DateTimeOffset ValidUntil { get; private set; }

    public static UserBirthDateCollectionReceipt Record(Guid userId, ModerationCollectionReceipt receipt,
        Guid observationId, Guid assessmentId, Guid assessmentAdoptionId, Guid? consentId,
        Guid policyId, long policyRevision, Guid publicationId, Guid profileId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (userId == Guid.Empty || receipt.Binding?.Target.HumanSubject?.Id != userId
            || receipt.State != ModerationIndividualRecordState.Current || now < receipt.RecordedAt || now >= receipt.ValidUntil
            || new[] { observationId, assessmentId, assessmentAdoptionId, policyId, publicationId, profileId }.Any(id => id == Guid.Empty)
            || policyRevision < 1 || consentId == Guid.Empty)
            throw new ArgumentException("Private collection provenance requires exact live original coordinates.");
        return new()
        {
            Id = receipt.Id,
            UserId = userId,
            ObservationId = observationId,
            AssessmentId = assessmentId,
            AssessmentAdoptionId = assessmentAdoptionId,
            ConsentId = consentId,
            PolicyId = policyId,
            PolicyRevision = policyRevision,
            PublicationId = publicationId,
            ProfileId = profileId,
            CollectedAt = now,
            ValidUntil = receipt.ValidUntil
        };
    }

    public override string ToString() => nameof(UserBirthDateCollectionReceipt);
}
