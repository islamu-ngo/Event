// Uses a 1:1 shared primary-key relationship with User for hard-deleteable PII.

using ISLAMU.Moderation.Domain;

namespace Explore.Domain;

public class UserPii
{
    private DateOnly? _dateOfBirth;
    private Guid? _birthDateCollectionReceiptId;
    private long _birthDateRevision;

    public Guid UserId { get; set; }
    public User? User { get; set; }

    public required string Email { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }

    /// <summary>
    /// Private declaration for protected identity owners, never assured age or an audience
    /// grant. Method access keeps it out of ordinary property-based serialization.
    /// Native owners must authorize the read and must not project it to general user DTOs.
    /// </summary>
    public DateOnly? ReadDeclaredBirthDate() => _dateOfBirth;

    public Guid? ReadBirthDateCollectionReceiptId() => _birthDateCollectionReceiptId;
    public long ReadBirthDateRevision() => _birthDateRevision;

    /// <summary>
    /// Checks the structural current receipt and exact human identity before retaining a
    /// declaration. Native owners authenticate collection/adoption/prerequisites before
    /// sensitive-body acceptance and retain their fences through the write. Supplied
    /// coordinates alone cannot authorize collection, correction or native receipt reuse.
    /// </summary>
    public void RecordDeclaredBirthDate(
        DateOnly birthDate, long expectedRevision, ModerationCollectionReceipt receipt,
        ModerationIndividualBinding currentBinding, ModerationCollectionPrerequisiteAssessment currentAssessment,
        IEnumerable<ModerationCollectionPrerequisite> currentPrerequisites, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(currentBinding);
        if (_birthDateRevision != expectedRevision || _birthDateRevision == long.MaxValue
            || UserId == Guid.Empty || currentBinding.Target.Kind != ModerationIndividualTargetKind.Human
            || currentBinding.Target.HumanSubject?.Id != UserId
            || currentBinding.Purpose is not (ModerationIndividualPurpose.IdentityEnrollment
                or ModerationIndividualPurpose.ProfileCompletion)
            || !receipt.MatchesCurrentMetadata(currentBinding, currentAssessment, currentPrerequisites, now))
            throw new InvalidOperationException("A private declaration requires the exact human, identity purpose, current collection metadata and revision.");
        _dateOfBirth = birthDate;
        _birthDateCollectionReceiptId = receipt.Id;
        _birthDateRevision = checked(_birthDateRevision + 1);
    }

    /// <summary>
    /// Removes the declaration and its collection link without a live collection policy
    /// or consent prerequisite. Native correction authorizes the caller; ordinary erasure
    /// deletes this entire PII row rather than depending on this revision-checked method.
    /// </summary>
    public void ClearDeclaredBirthDate(long expectedRevision)
    {
        if (_birthDateRevision != expectedRevision || _birthDateRevision == long.MaxValue)
            throw new InvalidOperationException("Removing a declaration requires its exact current revision.");
        _dateOfBirth = null;
        _birthDateCollectionReceiptId = null;
        _birthDateRevision = checked(_birthDateRevision + 1);
    }
}
